# AntiBotStarter smoke test (Windows PowerShell 5+ / pwsh):
#   1. publishes the app,
#   2. checks that Production refuses an insecure configuration,
#   3. runs the real server (Kestrel, Development, SQL Server LocalDB with migrations) and sends real HTTP requests
#      against every anti-bot layer, including a full registration: antiforgery + form token + real Turnstile
#      verification (Cloudflare test keys) + email outbox + background dispatcher.
# Requirements: .NET 10 SDK, SQL Server LocalDB (installed with Visual Studio), internet (Turnstile siteverify).
# Usage (from the repo root):  powershell -ExecutionPolicy Bypass -File scripts\smoke-test.ps1
$ErrorActionPreference = 'Continue'   # native stderr must not abort the script on Windows PowerShell 5
$repo = Join-Path $PSScriptRoot '..' | Resolve-Path
$work = Join-Path $repo 'artifacts\smoke'
$pub  = Join-Path $work 'publish'
New-Item -ItemType Directory -Force -Path $work | Out-Null
$fail = 0
function Check($name, $cond) { if ($cond) { "PASS $name" } else { "FAIL $name"; $script:fail++ } }

"==== publish ===="
dotnet publish (Join-Path $repo 'src\AntiBotStarter.Server') -c Release -o $pub -nologo -v:minimal 2>&1 | Select-Object -Last 5
Check 'publish copied Angular into wwwroot' (Test-Path (Join-Path $pub 'wwwroot\index.html'))

"==== Production must refuse an insecure config ===="
$env:ASPNETCORE_ENVIRONMENT = 'Production'
Remove-Item Env:ConnectionStrings__Default -ErrorAction SilentlyContinue
Push-Location $pub
$out = & dotnet (Join-Path $pub 'AntiBotStarter.Server.dll') --urls http://localhost:5298 2>&1 | Out-String
Pop-Location
Check 'Production refuses to start' ($out -match 'Insecure configuration for production')
Check 'Production flags the test CAPTCHA/empty secret' ($out -match 'Captcha:SecretKey')
Check 'Production flags PublicBaseUrl' ($out -match 'PublicBaseUrl')
Check 'Production flags the log-only email transport' ($out -match 'Email:Transport')
Check 'Production flags the missing database' ($out -match 'ConnectionStrings:Default')
Check 'Production flags unencrypted Data Protection keys' ($out -match 'KeyVaultKeyId')
Check 'Production flags missing Redis / single-instance decision' ($out -match 'Distributed:')
Check 'AllowedHosts=localhost is accepted' (-not ($out -match 'AllowedHosts must'))
($out -split "`n" | Select-String 'Insecure|AntiBot:|App:|Email:|ConnectionStrings|DataProtection|Distributed|Database' | Select-Object -First 12) -join "`n"

"==== Development run (LocalDB, HTTPS) ===="
# Antiforgery and auth cookies are Secure (__Host-): the app refuses to issue them over plain HTTP, so this run uses
# HTTPS with the ASP.NET Core development certificate.
dotnet dev-certs https --check --trust 2>&1 | Out-Null
Check 'ASP.NET Core development certificate is trusted' ($LASTEXITCODE -eq 0)
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ConnectionStrings__Default = 'Server=(localdb)\MSSQLLocalDB;Database=AntiBotStarterSmoke;Trusted_Connection=True;MultipleActiveResultSets=true'
$log = Join-Path $work 'server.log'
$p = Start-Process dotnet -ArgumentList "`"$(Join-Path $pub 'AntiBotStarter.Server.dll')`" --urls https://localhost:5299" -WorkingDirectory $pub -PassThru -WindowStyle Hidden -RedirectStandardOutput $log -RedirectStandardError (Join-Path $work 'server.err')
try {
  $base = 'https://localhost:5299'
  for ($i = 0; $i -lt 60; $i++) { try { Invoke-WebRequest "$base/health" -UseBasicParsing -TimeoutSec 2 | Out-Null; break } catch { Start-Sleep 1 } }
  $ua = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36'
  function Hit($path, $agent, $method = 'Get', $body = $null, $headers = @{}, $session = $null) {
    try {
      $params = @{ Uri = "$base$path"; UserAgent = $agent; UseBasicParsing = $true; TimeoutSec = 30; Method = $method; Headers = $headers }
      if ($session) { $params.WebSession = $session }
      if ($body) { $params.Body = ($body | ConvertTo-Json -Compress); $params.ContentType = 'application/json' }
      return Invoke-WebRequest @params
    }
    catch { return $_.Exception.Response }
  }
  function Body($r) {
    if ($r -is [System.Net.HttpWebResponse]) { return (New-Object IO.StreamReader($r.GetResponseStream())).ReadToEnd() }
    return $r.Content
  }

  $r = Hit '/' $ua;                        Check 'index.html served to a browser' ([int]$r.StatusCode -eq 200 -and $r.Content -match 'app-root')
  Check 'index.html contains the trap link' ($r.Content -match '/_internal/archive')
  Check 'CSP header present' ($r.Headers['Content-Security-Policy'] -match "frame-ancestors 'none'")
  Check 'X-Robots-Tag noindex' ($r.Headers['X-Robots-Tag'] -match 'noindex')
  Check 'no Server header' (-not $r.Headers['Server'])
  $r = Hit '/health/ready' $ua;            Check 'readiness: database (LocalDB) healthy' ([int]$r.StatusCode -eq 200 -and $r.Content -eq 'Healthy')
  $r = Hit '/api/config' $ua;              Check '/api/config 200' ([int]$r.StatusCode -eq 200)
  $r = Hit '/robots.txt' $ua;              Check 'robots.txt Disallow all + GPTBot' ($r.Content -match 'User-agent: GPTBot' -and $r.Content -match "User-agent: \*\r?\nDisallow: /")
  $r = Hit '/api/config' 'python-requests/2.32.3';  Check 'python-requests blocked (403)' ([int]$r.StatusCode -eq 403)
  $r = Hit '/api/config' 'Mozilla/5.0 (compatible; GPTBot/1.2; +https://openai.com/gptbot)'; Check 'GPTBot blocked (403)' ([int]$r.StatusCode -eq 403)
  $r = Hit '/api/config' 'Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)'; Check 'fake Googlebot blocked (403)' ([int]$r.StatusCode -eq 403)
  $r = Hit '/api/products' $ua;            Check 'products require auth (401)' ([int]$r.StatusCode -eq 401)

  # ---- Full registration like a browser: a web session keeps the (Secure, __Host-) cookies ----
  # (Windows PowerShell 5.1 ignores a hand-written Cookie header, so a WebRequestSession is required.)
  $s = New-Object Microsoft.PowerShell.Commands.WebRequestSession
  Hit '/api/antiforgery' $ua 'Get' $null @{} $s | Out-Null
  $cookies = $s.Cookies.GetCookies([Uri]$base)
  $xsrf = ($cookies | Where-Object Name -eq 'XSRF-TOKEN').Value
  Check 'antiforgery cookies issued' ($xsrf -and ($cookies | Where-Object Name -eq '__Host-abs-af'))
  $hdr = @{ 'X-XSRF-TOKEN' = [Uri]::UnescapeDataString($xsrf) }

  function Register($email) {
    $token = ((Hit '/api/forms/token?purpose=register' $ua 'Get' $null @{} $s).Content | ConvertFrom-Json).token
    Start-Sleep -Milliseconds 3300   # the form token rejects submissions faster than 3 s
    # Cloudflare's test site key yields this dummy token; the test secret key accepts it (real siteverify call).
    return Hit '/api/auth/register' $ua 'Post' @{ email = $email; password = 'Smoke-Test-Password-2026!'; website = ''; formToken = $token; captchaToken = 'XXXX.DUMMY.TOKEN.XXXX' } $hdr $s
  }

  $email = "smoke-$([Guid]::NewGuid().ToString('N').Substring(0, 8))@example.com"
  $r = Register $email
  Check 'registration accepted (antiforgery + form token + real Turnstile verification)' ([int]$r.StatusCode -eq 202)
  $sent = $false
  for ($i = 0; $i -lt 20 -and -not $sent; $i++) { Start-Sleep 1; $sent = (Get-Content $log -Raw) -match [regex]::Escape("[DEV EMAIL] To: $email") }
  Check 'confirmation email queued in SQL and sent by the background dispatcher' $sent
  Check 'confirmation email carries the confirm link' ((Get-Content $log -Raw) -match 'confirm-email\?userId=')

  $r = Register 'smoke@mailinator.com'
  Check 'disposable email domain rejected (400)' ([int]$r.StatusCode -eq 400 -and (Body $r) -match 'disposable_email')

  $r = Hit '/api/auth/register' $ua 'Post' @{ email = 'x@example.com'; password = 'x'; website = ''; formToken = ''; captchaToken = '' }
  Check 'POST without antiforgery rejected (400)' ([int]$r.StatusCode -eq 400 -and (Body $r) -match 'antiforgery_failed')

  # ---- Trap last: it blocks this machine's IP ----
  $r = Hit '/_internal/archive' $ua;       Check 'trap answers 404' ([int]$r.StatusCode -eq 404)
  $r = Hit '/api/config' $ua;              Check 'after the trap the client is blocked (403)' ([int]$r.StatusCode -eq 403)
  $r = Hit '/health' $ua;                  Check 'health still works for the blocked client' ([int]$r.StatusCode -eq 200)
}
finally {
  Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
  Remove-Item Env:ConnectionStrings__Default -ErrorAction SilentlyContinue
}
Check 'server logged the security events' ((Get-Content $log -Raw) -match 'AntiBot crawler_trap')
"==== $fail failure(s) ===="
exit $fail
