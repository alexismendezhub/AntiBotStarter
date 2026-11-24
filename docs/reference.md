# Anti Bot Starter: reference

Everything beyond the README: how it is built, how to use and configure it, the API and the tests.

## 1. What it does

Paths are relative to `src/AntiBotStarter.<Layer>/`.

| # | Layer | Where | What it does |
|----|------|------------|-------------------------------------|
| 1 | Real IP behind a proxy | `Server/Security/ProxySetup.cs` | `UseForwardedHeaders` **only** trusts `KnownProxies`/`KnownNetworks`. Without this, a bot fakes `X-Forwarded-For` and evades the rate limit. |
| 2 | Security headers | `Server/Middleware/SecurityHeadersMiddleware.cs` | CSP (allows only Turnstile), `X-Frame-Options`, `nosniff`, `Referrer-Policy`, `Permissions-Policy`, COOP/CORP, `X-Robots-Tag`, `no-store` on `/api`. |
| 3 | Origin lock | `Server/Middleware/OriginLockMiddleware.cs` | Rejects anything that does not come through Front Door (`X-Azure-FDID`) or Cloudflare (secret header). Constant-time comparison. |
| 4 | Anti-crawler trap | `Server/Middleware/CrawlerTrapMiddleware.cs` | Path in `Disallow` + invisible link. Visitors are blocked for 24 h. IPv6 is blocked per /64. |
| 5 | User-Agent filter | `Server/Middleware/BotFilterMiddleware.cs` | Blocks empty UA, AI crawlers, scraping tools and **fake** search engines (reverse + forward DNS verification). |
| 6 | Dynamic robots.txt | `Application/Security/RobotsTxtBuilder.cs` | `Disallow: /` by default; when search engines are allowed it only blocks `/api/` and the trap. Lists the AI crawlers. |
| 7 | Rate limiting | `Server/Security/RateLimitingSetup.cs` | Global per IP (sliding window), `auth` per IP (strict fixed window), `api` token bucket per user or IP. 429 + `Retry-After`. |
| 8 | Kestrel limits | `Server/AntiBotServiceCollectionExtensions.cs` (`HardenKestrel`) | Max body, header timeout, minimum data rate (anti-Slowloris), max connections, no `Server` header. |
| 9 | Antiforgery | `Server/Security/AntiforgerySetup.cs` | `__Host-` HttpOnly cookie + `XSRF-TOKEN` → Angular sends `X-XSRF-TOKEN`. Validated on every POST. |
| 10 | Honeypot | `Server/Forms/FormProtectionFilter.cs` + HTML | Off-screen `website` field; if it arrives with a value, the request is rejected. |
| 11 | Form token | `Application/Forms/FormTokenService.cs` | Signed with Data Protection: minimum time (3 s), expiry (30 min), **single use**, bound to its purpose. |
| 12 | CAPTCHA | `Infrastructure/Forms/TurnstileCaptchaVerifier.cs` | Cloudflare Turnstile verified **on the server**, fails closed and checks the hostname. Swappable (reCAPTCHA/hCaptcha). |
| 13 | Identity | `Infrastructure/Identity/IdentitySetup.cs` | Lockout (5 attempts / 15 min), confirmed email required, 12+ character passwords, Strict `__Host-` cookie. |
| 14 | Breached passwords | `Infrastructure/Identity/PwnedPasswordValidator.cs` | Have I Been Pwned with k-anonymity (against credential stuffing). |
| 15 | Anti-enumeration | `Server/Endpoints/AuthEndpoints.cs` | Same message and **same timing** (decoy hash) for unknown user, wrong password, lockout and unconfirmed email. Registration answers the same whether or not the email exists. |
| 16 | Safe links | `Server/Endpoints/AuthEndpoints.cs` | Confirmation link built from `App:PublicBaseUrl`, never from the `Host` header. Plus `AllowedHosts`. |
| 17 | Data anti-scraping | `Server/Endpoints/ProductEndpoints.cs` | Requires a session, non-sequential GUIDs, `pageSize` ≤ 50, page ≤ 20, no total count, per-user token bucket. |
| 18 | CORS | `Server/Security/ProxySetup.cs` (`AddAntiBotCors`) | Closed by default: Angular is served from the same origin. |
| 19 | Observability | `Server/Security/SecurityEvents.cs` | Structured log (EventId 9000) + `antibot.rejections{reason}` metric (OpenTelemetry/App Insights). |
| 20 | Startup validation | `Server/StartupValidation.cs` | In Production it refuses to start without: a real CAPTCHA (not the test keys), an https `PublicBaseUrl`, `AllowedHosts` ≠ `*`, a real email transport with an encrypted connection, SQL Server, no migrate-on-startup, a Key Vault key for Data Protection, and Redis (or an explicit single-instance decision). |
| 21 | Angular | `src/antibotstarter.client` | Turnstile, honeypot, form token, XSRF, 429/403 interceptor, trap, `meta robots`, SRI, CSP-friendly build (no inline critical CSS). Pages for forgot/reset password, resend confirmation, two-step sign-in and account security (MFA with QR). |
| 22 | Email outbox | `Infrastructure/Email/` | Every email is queued in SQL with recipient and body **encrypted** (Data Protection), sent by a background dispatcher (retries with backoff, claims rows safely across instances), and erased after sending. See [section 5](#5-email-abuse-protection). |
| 23 | Email abuse limits | `Infrastructure/Email/EmailOutbox.cs` | Per-recipient cooldown and daily cap, site-wide hourly and daily caps (with warning), disposable-domain blocking (10,400 domains, CC0 list), kill switch. Limits hold across IPs and instances. |
| 24 | Account recovery | `Server/Endpoints/AuthEndpoints.cs` | Resend confirmation, forgot password and reset password: same form protections, identical answers whether or not the account exists, single-use reset links, lockout cleared after a successful reset. |
| 25 | Two-factor authentication | `Server/Endpoints/MfaEndpoints.cs` | TOTP (any authenticator app) with 10 single-use recovery codes; every change needs a current code; wrong codes count towards lockout; `__Host-` 2FA cookies; sessions revalidated every 5 minutes. |
| 26 | Database | `Infrastructure/Data/DatabaseSetup.cs` | SQL Server / Azure SQL with EF Core migrations and transient-fault retries. Migrations run from the pipeline in production. |
| 27 | Shared key ring | `Infrastructure/DependencyInjection.cs` | Data Protection keys stored in SQL (all instances share them) and encrypted with an Azure Key Vault key. |
| 28 | Shared anti-bot state | `Infrastructure/Distributed/DistributedSetup.cs` | With Redis: rate limits, blocklist and form-token nonces are enforced across instances. |
| 29 | Health | `/health`, `/health/ready` | Liveness, and readiness (SQL + Redis) for the load balancer / App Service health check. |
| 30 | Telemetry | `Server/Program.cs` | Application Insights (OpenTelemetry) with the `AntiBotStarter.Security` and `AntiBotStarter.Email` meters when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set. |

### Pipeline order (`Program.cs`)

```text
UseForwardedHeaders → ExceptionHandler/HSTS → SecurityHeaders → OriginLock → CrawlerTrap/Blocklist
→ BotFilter → HttpsRedirection → DefaultFiles → Routing → CORS → Authentication → RateLimiter
→ StaticFiles → Authorization → Endpoints (+ filters Antiforgery → Honeypot/Token → CAPTCHA)
```

The cheapest rejection runs first. The CAPTCHA, which makes an HTTP call, is evaluated last.

## 2. How to run it

**Requirements:** .NET SDK 10, Node.js 22.22.3+ or 24.15+ (required by Angular 22), SQL Server LocalDB (installed with
Visual Studio; the database is created and migrated on the first run in Development), and Visual Studio 2022 17.14+ /
2026 or the CLI.

### 2.1 Visual Studio

Open `AntiBotStarter.slnx`, set **AntiBotStarter.Server** as the startup project and press F5.
SpaProxy runs `npm start` (Angular on `https://localhost:63421`) and the server listens on `https://localhost:7180`.

### 2.2 CLI

```cmd
cd src\antibotstarter.client && npm install && cd ..\..
dotnet run --project src\AntiBotStarter.Server --launch-profile https
```

### 2.3 Try the flow

1. Open `https://localhost:63421/register` and register.
   In development, Turnstile uses Cloudflare's **test keys**, which always pass.
2. The confirmation email is queued and, a few seconds later, the dispatcher prints it in the **server console** (`[DEV EMAIL]`).
3. Sign in and open **Products**. Under **Security** you can turn on two-factor authentication (QR code + recovery codes).
4. *Forgot your password?* and *Resend the confirmation email* are on the sign-in page.

## 3. Project structure

```text
AntiBotStarter.slnx                              ← solution (.slnx format)
├─ src/
│  ├─ AntiBotStarter.Domain/                     ← entities: email message, products (no framework dependencies)
│  ├─ AntiBotStarter.Application/                ← options, ports and application services
│  │  ├─ Configuration/AntiBotOptions.cs         ← all the "AntiBot" settings
│  │  ├─ Email/                                  ← email options, templates, disposable domains, ports (IEmailOutbox, IEmailTransport)
│  │  ├─ Forms/                                  ← form token service, ICaptchaVerifier
│  │  ├─ Security/                               ← robots.txt builder, search engine verification, IBlocklist, IReplayGuard
│  │  └─ DependencyInjection.cs                  ← AddApplication
│  ├─ AntiBotStarter.Infrastructure/             ← implementations of the ports
│  │  ├─ Data/                                   ← AppDbContext, database setup, EF Core migrations
│  │  ├─ Distributed/                            ← Redis shared state and health check
│  │  ├─ Email/                                  ← encrypted outbox, dispatcher, SMTP / ACS transports
│  │  ├─ Forms/                                  ← Cloudflare Turnstile verifier
│  │  ├─ Identity/                               ← Identity, lockout, Have I Been Pwned validator
│  │  ├─ Security/                               ← memory/Redis blocklist and replay guard, DNS resolver
│  │  └─ DependencyInjection.cs                  ← AddInfrastructure (database, Data Protection, Redis, Identity, email)
│  ├─ AntiBotStarter.Server/                     ← ASP.NET Core (Minimal APIs) + SpaProxy
│  │  ├─ Endpoints/                              ← auth + recovery, MFA, products, robots/config/antiforgery
│  │  ├─ Middleware/                             ← security headers, origin lock, crawler trap, User-Agent filter
│  │  ├─ Security/                               ← proxy/CORS, rate limiting, antiforgery, client IP, security events
│  │  ├─ Forms/FormProtectionFilter.cs           ← honeypot + form token + CAPTCHA endpoint filter
│  │  ├─ AntiBotServiceCollectionExtensions.cs   ← AddAntiBotProtection, Kestrel limits, edge pipeline
│  │  ├─ StartupValidation.cs                    ← in Production it refuses to start with an insecure config
│  │  └─ Program.cs                              ← order of the security pipeline
│  ├─ antibotstarter.client/                     ← Angular 22 (.esproj, aspnetcore-https.js, proxy.conf.js)
│  │  └─ src/app/{core,shared,features}
│  └─ tests/
│     ├─ AntiBotStarter.Application.UnitTests/   ← form tokens
│     ├─ AntiBotStarter.Infrastructure.UnitTests/← Turnstile and Have I Been Pwned with mocked HTTP
│     └─ AntiBotStarter.Server.IntegrationTests/ ← the whole server in memory, plus SQL Server, Redis and Mailpit containers
├─ deploy/azure/                                 ← main.bicep (Front Door+WAF, App Service, SQL, Redis, Key Vault, ACS, App Insights) + guide
├─ deploy/github/deploy.yml                      ← deployment workflow template (OIDC)
├─ deploy/iis, deploy/cloudflare                 ← IIS web.config sample, Cloudflare rules
├─ docs/                                         ← Word and PDF copies of this README
└─ scripts/                                      ← build-and-test.cmd (every test on Windows), smoke-test.ps1
```

## 4. Anti-crawler: how crawling is prevented

| Crawler type | What stops it |
|-----------------|-------------------------------------------|
| Search engines (Google, Bing…) | `robots.txt` `Disallow: /`, `X-Robots-Tag: noindex`, `<meta name="robots">` and UA blocking when `AllowSearchEngines=false`. |
| AI crawlers (GPTBot, ClaudeBot, CCBot, Bytespider, PerplexityBot…) | robots.txt (including `Google-Extended` and `Applebot-Extended`) + 403 by UA. |
| Bots pretending to be Googlebot | Reverse + forward DNS verification (the method Google and Bing document). |
| Crawlers that ignore robots.txt | **Trap**: an invisible link to a `Disallow` path. Whoever follows it gets blocked. |
| Plain HTTP scrapers (requests, curl, Scrapy) | UA filter, empty UA, antiforgery and form token (without JS they never get the tokens). |
| Headless browsers (Puppeteer/Playwright) | `HeadlessChrome` UA, Turnstile and, above all, the WAF's detection (TLS/JS fingerprinting). |
| Mass data scraping | Mandatory session with confirmed email, per-user rate limit, bounded paging, GUIDs and no totals. |
| IP rotation | Per-user rate limit (not only per IP), IPv6 /64 grouping, and reputation/ASN rules at the WAF. |

If you **do** want SEO, set `AntiBot:AllowSearchEngines = true` and remove the `<meta name="robots">` from `index.html`.
Then only DNS-verified search engines get in; AI crawlers stay blocked.

## 5. Email abuse protection

Endpoints that send email (register, resend confirmation, forgot password) are a favorite target: a bot can use them to
**send thousands of emails to third parties** from your domain, which ruins its reputation and costs money. The
per-IP limit does not stop it (the attacker rotates IPs), so the limits are applied to the **messages themselves**,
before they are queued, and they are stored in the shared database:

| Layer | What it stops |
|------------------------------|------------------------------|
| Turnstile + form token + honeypot + antiforgery + `auth` rate limit | Automated submissions in the first place. |
| Per-recipient cooldown (15 min) and daily cap (3) | Bombing one address, whatever IP or instance the requests come from. |
| Site-wide hourly (200) and daily (1000) caps, warning at 80 % | Mass sending to many different addresses; the alert tells you an attack is going on. |
| Disposable domains rejected at registration | Throwaway accounts. |
| Same answer for existing / unknown / unconfirmed accounts | Using the email endpoints to discover accounts. |
| Fixed templates, link from `App:PublicBaseUrl` only | Using your emails to deliver spam text or links to another domain. |
| Encrypted outbox, erased after sending, purged after 7 days | Live confirmation/reset tokens sitting in the database. |
| `Email:Paused` kill switch and `antibot.email.*` metrics | Stopping all sending during an incident and alerting on it. |
| SPF, DKIM, DMARC on your sending domain (deploy guide) | Spoofing and spam-folder delivery. |

## 6. External services and configuration

None of this can be done from the code, and it is what stops the most bots.

### 6.1 WAF / CDN with bot management (pick one)

| Service | What to enable |
|-------------------|-----------------------------------------|
| **Cloudflare** | Bot Fight Mode / Super Bot Fight Mode or Bot Management (Enterprise), *Block AI bots*, *AI Labyrinth*, WAF managed rules, rate limiting rules, Managed Challenge on `/api/auth/*`, Turnstile. See `deploy/cloudflare/rules.md`. |
| **Azure Front Door Premium** | WAF with `Microsoft_DefaultRuleSet` + `Microsoft_BotManagerRuleSet`, rate limit rules, JavaScript challenge, geo-filtering. See `deploy/azure/frontdoor-waf.bicep`. |
| **Azure Application Gateway WAF v2** | OWASP CRS 3.2 / DRS 2.1 + Bot Protection, rate limiting (custom rules). |
| **AWS CloudFront + AWS WAF** | *Bot Control* managed rule group (Common/Targeted), rate-based rules, CAPTCHA/Challenge actions. |
| **Akamai Bot Manager / Imperva / DataDome / HUMAN (PerimeterX) / Kasada / F5 Distributed Cloud Bot Defense** | Advanced detection (browser fingerprinting and behavior) when the bot is targeted and high-value (credential stuffing, scalping). |

### 6.2 Lock the origin (mandatory when using a WAF)

- **Azure App Service:** Networking > Access restrictions → allow only the `AzureFrontDoor.Backend` service tag with the `X-Azure-FDID` header = your ID. Also set `OriginLock:Enabled=true`.
- **Cloudflare:** Cloudflare Tunnel (`cloudflared`), or a firewall limited to the cloudflare.com/ips ranges plus a secret header (Transform Rule).
- **Private Link** (Front Door Premium → App Service), so the origin is not exposed to the Internet.

### 6.3 Hosting

- **IIS:** the *IP and Domain Restrictions* feature + `dynamicIpSecurity`, `requestFiltering`, `removeServerHeader`. See `deploy/iis/web.config.sample`.
- **Azure App Service:** *HTTPS Only*, minimum TLS 1.2, FTP disabled, *Client affinity* off when scaling out, Managed Identity for Key Vault.
- **Behind a proxy:** configure `ForwardedHeaders`. On App Service Linux you can use `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, although this starter already calls `UseForwardedHeaders`.

### 6.4 Secrets and keys (implemented)

- **Azure Key Vault**: `deploy/azure/main.bicep` stores the Turnstile secret, the Redis and ACS connection strings as
  secrets and wires them as Key Vault references; the app identity only has *Secrets User* and *Crypto User*.
- **Data Protection**: the key ring is in SQL (shared by every instance) and encrypted with a Key Vault RSA key
  (`DataProtection:KeyVaultKeyId`, required in Production).

### 6.5 Distributed state (implemented)

- With `Distributed:RedisConnectionString`, rate limits (global sliding window, auth fixed window, API token bucket),
  the blocklist and the form-token nonces are enforced in Redis across instances (container tests prove it).
- The WAF's own rate limiting still comes first (6.1).

### 6.6 CAPTCHA (alternatives)

| Provider | Notes |
|------------------|------------------------------------------|
| **Cloudflare Turnstile** (default) | Free, frictionless, private. Keys in the Cloudflare dashboard > Turnstile. |
| **Google reCAPTCHA v3 / Enterprise** | Returns a score (0–1); validate `score` and `action`. Implement `ICaptchaVerifier`. |
| **hCaptcha** | Endpoint `https://api.hcaptcha.com/siteverify`. Adjust the CSP. |
| **Friendly Captcha / ALTCHA** | Proof-of-work, cookieless; ALTCHA is self-hostable. |

### 6.7 Email (implemented)

Transports: **Azure Communication Services** (managed identity or connection string) and **SMTP** (MailKit: SendGrid,
Amazon SES, Mailgun, Postmark, Microsoft 365…). Protections in [section 5](#5-email-abuse-protection). On your sending domain configure **SPF, DKIM
and DMARC**; the Azure-managed ACS domain in the template is for testing only (low limits).

### 6.8 Stronger authentication

- **MFA (implemented):** TOTP with recovery codes, setup page with QR code, two-step sign-in. Optional next step:
  **passkeys/WebAuthn** (supported by Identity since .NET 10).
- External provider: **Microsoft Entra ID / Entra External ID**, Auth0, Okta. That delegates login protection, including bot detection.

### 6.9 Monitoring and alerts

- **Application Insights (implemented)** through OpenTelemetry when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set,
  exporting the `AntiBotStarter.Security` and `AntiBotStarter.Email` meters.
- Alerts on: spikes of `antibot.rejections` per `reason`, 429, `login_failed`, `login_locked_out`, `2fa_failed`,
  `crawler_trap`, `captcha_failed`, `antibot.email.throttled{reason=global_*}` and `antibot.email.failed`.
- WAF logs (Front Door/Cloudflare) in Log Analytics or a SIEM (Microsoft Sentinel).
- Incident runbook: tighten the WAF, enable *Under Attack Mode* (Cloudflare) and lower the limits through configuration.

### 6.10 Other recommended measures

- **HSTS preload** (hstspreload.org) once the whole domain is https.
- **DNSSEC** and **CAA** records.
- **security.txt** (`/.well-known/security.txt`) for vulnerability reports.
- **Load testing** (k6, JMeter, Azure Load Testing) to tune the limits without blocking real users.
- **Pentest / OWASP ZAP** baseline scan in CI.
- **Dependabot / `dotnet list package --vulnerable` / `npm audit`**.
- **Legal protection:** terms of use that forbid scraping, and text-and-data-mining (TDM) rights-reservation metadata.

## 7. Configuration reference

Everything lives in the `AntiBot` section of `appsettings.json`. The lists (User-Agents, search engines) are there, not in code.

| Key | Default | Notes |
|--------------------|----------|------------------------------|
| `AllowSearchEngines` | `false` | `true` allows verified search engines and changes robots.txt. |
| `ExemptPaths` | `/health`, `/robots.txt` | Skip the UA filter and the blocklist. |
| `ContentSecurityPolicy` | allows `challenges.cloudflare.com` | If you switch CAPTCHA, adjust `script-src` and `frame-src`. |
| `AllowedCorsOrigins` | `[]` | Only if the front end lives on another domain. |
| `RateLimits:GlobalPermitPerMinute` | 120 | Per IP. Tune it with real data. |
| `RateLimits:AuthPermitPerMinute` | 5 | Per IP, for login, registration and confirmation. |
| `RateLimits:Api*` | 30 / 10 every 10 s | Token bucket per user or IP. |
| `BotFilter:*` | see the file | `BlockedUserAgentFragments`, `AiCrawlerUserAgents`, `RobotsOnlyAiTokens`, `SearchEngines`. |
| `CrawlerTrap:Path` | `/_internal/archive` | **Must match** `index.html` and `security.constants.ts`. |
| `CrawlerTrap:BlockMinutes` | 1440 | Block duration. |
| `Forms:MinSecondsToSubmit` | 3 | Faster submissions are treated as bots. |
| `Forms:MaxMinutesValid` | 30 | Form token expiry. |
| `Captcha:SiteKey` / `SecretKey` | empty (test keys in Development) | The **secret** goes in Key Vault or environment variables, never in the repo. |
| `Captcha:ExpectedHostnames` | `[]` | Set your domains in production. |
| `OriginLock:*` | disabled | `HeaderName` `X-Azure-FDID` (Front Door) or your secret header (Cloudflare). |
| `ForwardedHeaders:KnownProxies` / `KnownNetworks` | `[]` | IPs or CIDRs of your load balancer or CDN. |
| `ForwardedHeaders:ForwardedForHeaderName` | `X-Forwarded-For` | `CF-Connecting-IP` behind Cloudflare. |
| `Scraping:RequireAuthForData` | `true` | |
| `Scraping:MaxPageSize` / `MaxPage` | 50 / 20 | |
| `PwnedPasswords:Enabled` / `FailClosed` | `true` / `false` | |
| `App:PublicBaseUrl` | (Development: `https://localhost:63421`) | Required and https in Production. |
| `AllowedHosts` | `localhost` | In Production, your domains separated by `;`. |

Other sections:

| Key | Default | Notes |
|------------------------|-----------|-------------------------|
| `ConnectionStrings:Default` | empty (Development: LocalDB) | Azure SQL with managed identity in production. |
| `Database:Provider` / `MigrateOnStartup` | `SqlServer` / `false` (Development: `true`) | `InMemory` is for tests only. |
| `DataProtection:KeyVaultKeyId` | empty | Key Vault key that encrypts the key ring. Required in Production. |
| `Distributed:RedisConnectionString` | empty | Enables the Redis-shared state. |
| `Distributed:SingleInstance` | `false` (Development: `true`) | Production without Redis is only allowed when you state that exactly one instance runs. |
| `Distributed:KeyPrefix` | `abs:` | Prefix of every Redis key. |
| `Email:Transport` | `Log` | `Smtp` or `AzureCommunicationServices` in production. |
| `Email:FromAddress` / `FromName` | empty / `AntiBotStarter` | |
| `Email:Paused` | `false` | Kill switch: queue but do not send. |
| `Email:BlockDisposableDomains` / `ExtraBlockedDomains` | `true` / `[]` | |
| `Email:Throttle:PerRecipientMinMinutes` / `PerRecipientPerDay` | 15 / 3 | Per destination address. |
| `Email:Throttle:GlobalPerHour` / `GlobalPerDay` / `GlobalWarningPercent` | 200 / 1000 / 80 | Whole site. |
| `Email:Dispatcher:*` | 5 s poll, batch 20, 5 attempts, 2 min lock | |
| `Email:RetentionDays` | 7 | Sent/failed rows (already without body or recipient) are deleted after this. |
| `Email:Smtp:*` | port 587, `StartTls` | `SslOnConnect` for 465; `None` only for a local test server. |
| `Email:AzureCommunicationServices:Endpoint` / `ConnectionString` | empty | Endpoint uses the app's managed identity. |

Environment variables (Azure App Service, containers): `AntiBot__Captcha__SecretKey`, `AntiBot__OriginLock__ExpectedValues__0`, etc.

## 8. API

| Method and path | Protection | Description |
|---|---|---|
| `GET /robots.txt` | — | Generated from `AntiBot` settings ([section 4](#4-anti-crawler-how-crawling-is-prevented)) |
| `GET /health`, `GET /health/ready` | — | Liveness; readiness of SQL Server and Redis |
| `GET /api/config` | — | `{ captchaEnabled, turnstileSiteKey }` for the Angular client |
| `GET /api/antiforgery` | — | Issues the antiforgery cookie and `XSRF-TOKEN` |
| `GET /api/forms/token?purpose=` | `api` rate limit | Signed, single-use form token for `login`, `register`, `resend`, `forgot`, `reset` |
| `POST /api/auth/register`, `/login`, `/resend-confirmation`, `/forgot-password`, `/reset-password` | antiforgery + honeypot + form token + CAPTCHA + `auth` rate limit | Account forms; identical answers whether or not the account exists |
| `POST /api/auth/login-2fa`, `/login-recovery`, `/confirm-email` | antiforgery + `auth` rate limit | Second sign-in step, recovery code, email confirmation |
| `POST /api/auth/logout`, `GET /api/auth/me` | session (+ antiforgery on POST) | Sign out; the signed-in user |
| `GET /api/account/mfa`, `POST /setup`, `/enable`, `/disable`, `/recovery-codes` | session + antiforgery + `auth` rate limit | Two-factor authentication |
| `GET /api/products?page=&pageSize=`, `GET /api/products/{id}` | session (configurable) + `api` token bucket | Sample data API with anti-scraping limits |

Any other `/api/...` path answers 404.

## 9. Tests

The tests also need Docker (for the containers) and Chrome (for the Angular tests).

`scripts\build-and-test.cmd` runs everything on Windows: the build (warnings as errors), the .NET tests (with Docker
containers for SQL Server, Redis and Mailpit), the Angular tests, the Angular production build and the real-server smoke test.

| Project | Command | Covers |
|----------|----------|----------------------------------------|
| `AntiBotStarter.Application.UnitTests` (11) | `dotnet test AntiBotStarter.slnx` | Form token: reuse, purpose, too fast, expired, tampered. |
| `AntiBotStarter.Infrastructure.UnitTests` (14) | same | Turnstile and Have I Been Pwned with mocked HTTP. |
| `AntiBotStarter.Server.IntegrationTests` (134) | same | The whole server in memory: UA filter and AI crawlers, real/fake/look-alike Googlebot, trap + IPv6 /64, robots.txt, rate limits (global, auth, api, spoofed XFF and known proxy), headers, origin lock, antiforgery, honeypot, token (reuse, purpose, too fast, expired, tampered), CAPTCHA, Identity (full flow, lockout, anti-enumeration, cookies), anti-scraping, email outbox (encryption, every abuse limit, disposable domains, kill switch, retries, purge); account recovery; MFA (setup, two-step sign-in, lockout, recovery codes); production validation. |
| ↳ containers | same (Docker) | **SQL Server 2022**: migrations, full account flow, two instances dispatching the outbox concurrently send each email exactly once, shared throttling. **Redis 8**: blocklist, auth and global limits, and form-token replay shared by two instances. **Mailpit**: real SMTP delivery and retry when the server is down. Without Docker they are skipped, unless `ABS_REQUIRE_CONTAINERS=1`. |
| `antibotstarter.client` (48) | `npm run test:ci` | 429/403 interceptor, XSRF, secure default config, form token, login and registration (CAPTCHA required, reset after error, honeypot), two-step sign-in and recovery codes, forgot/reset/resend pages, MFA setup with QR, Turnstile, loader, guard, trap and paging. |
| Real server (32 checks) | `scripts\smoke-test.ps1` | Publishes the app; Production refuses an insecure configuration; then Kestrel over HTTPS with LocalDB: headers, readiness, robots.txt, scraper/AI/fake-Googlebot blocking, auth on data, a full registration (antiforgery + form token + real Turnstile verification + SQL outbox + dispatcher), disposable domain, trap + blocklist, security-event logging. |

## 10. Production and deployment

### 10.1 Checklist

Most items are created by `deploy/azure/main.bicep` and enforced by `StartupValidation`; the rest are decisions or external steps.

- [ ] Infrastructure deployed with `deploy/azure/main.bicep` (Front Door + WAF, locked origin, Key Vault, SQL, Redis, ACS, App Insights)
- [ ] SQL user created for the app identity and migrations applied from the pipeline (deploy guide steps 3–4)
- [ ] Real Turnstile keys for your domain (secret only in Key Vault)
- [ ] Custom domain on Front Door, then `customHostName` redeployed (AllowedHosts, PublicBaseUrl, Turnstile hostname)
- [ ] Your own email domain verified in ACS with SPF, DKIM and DMARC
- [ ] Alerts on `antibot.rejections`, `antibot.email.throttled` (global) and `antibot.email.failed`
- [ ] Load tests to tune the rate limits and email caps
- [ ] `AllowSearchEngines` decided and `<meta name="robots">` adjusted

### 10.2 Deploying to Azure

See **[deploy/azure/README.md](../deploy/azure/README.md)**: `az deployment group create` with `main.bicep`, SQL user for the
app identity, migration bundle, `az webapp deploy`, verification and custom domain. A GitHub Actions template with
OIDC is in `deploy/github/deploy.yml` (copy it to `.github/workflows/`).

### 10.3 Publish

```cmd
dotnet publish src\AntiBotStarter.Server -c Release -o publish
```

The `.esproj` runs `npm run build` and copies `dist/antibotstarter.client/browser` into `wwwroot`.
