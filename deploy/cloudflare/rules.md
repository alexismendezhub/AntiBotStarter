# Cloudflare: recommended configuration for AntiBotStarter

> Sample expressions for *Security > WAF > Custom rules* and *Rate limiting rules*.
> They are not applied automatically: create them in the dashboard or with Terraform (`cloudflare_ruleset`).

## 1. Dashboard switches

| Where | Option | Value |
|-------------|-------------------|----------------------------|
| Security > Bots | Bot Fight Mode (Free) / Super Bot Fight Mode (Pro/Business) | On; "Definitely automated" → Block, "Likely automated" → Managed Challenge |
| Security > Bots | Block AI bots / AI Scrapers and Crawlers | On |
| Security > Bots | AI Labyrinth | On (serves decoy pages to crawlers that ignore robots.txt) |
| Security > Bots | Verified bots | Allow only if `AllowSearchEngines = true` |
| Security > Settings | Security level | Medium or High |
| Security > Settings | Browser Integrity Check | On |
| SSL/TLS | Mode | Full (strict) |
| SSL/TLS > Edge | Min TLS | 1.2 |
| Turnstile | Widget | Create one for your domains → site key/secret in `AntiBot:Captcha` |

## 2. Custom rules (WAF)

```text
# Managed Challenge on authentication when the bot score is low (requires Bot Management)
(http.request.uri.path wildcard "/api/auth/*" and cf.bot_management.score lt 30)
→ Managed Challenge

# Block empty UA
(len(http.user_agent) eq 0)
→ Block

# Block verified bots if you do not want search engines
(cf.client.bot)
→ Block            # remove it if AllowSearchEngines = true

# Challenge traffic from hosting/cloud ASNs (adjust the list)
(ip.geoip.asnum in {14061 16509 14618 24940 16276 63949} and not cf.client.bot)
→ Managed Challenge
```

## 3. Rate limiting rules

```text
Expression: (http.request.uri.path wildcard "/api/auth/*")
Characteristics: IP   Period: 1 min   Requests: 20   Action: Block 10 min

Expression: (http.request.uri.path wildcard "/api/*")
Characteristics: IP   Period: 1 min   Requests: 300  Action: Managed Challenge
```

## 4. Lock the origin (mandatory)

Option A, **Cloudflare Tunnel** (`cloudflared`): the origin has no public port. This is the best option.

Option B, **secret header**: with *Rules > Transform Rules > Modify Request Header*, add
`X-Origin-Secret: <long-random-value>` and in appsettings set:

```json
"OriginLock": { "Enabled": true, "HeaderName": "X-Origin-Secret", "ExpectedValues": [ "<value>" ] }
```

Also restrict the origin firewall or Access Restrictions to the
[Cloudflare IP ranges](https://www.cloudflare.com/ips/).

## 5. Real client IP

Behind Cloudflare:

```json
"ForwardedHeaders": {
  "ForwardedForHeaderName": "CF-Connecting-IP",
  "KnownNetworks": [ "173.245.48.0/20", "103.21.244.0/22", "...all the ranges from cloudflare.com/ips..." ]
}
```
