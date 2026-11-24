# Deploying AntiBotStarter to Azure

`main.bicep` creates the whole production topology:

```text
Internet → Azure Front Door Premium + WAF (OWASP DRS 2.1, Bot Manager, rate limits, empty-UA block)
         → App Service Linux (.NET 10) — accepts ONLY this Front Door profile (service tag + X-Azure-FDID)
         → Azure SQL (Entra-only auth, managed identity)   → Azure Managed Redis (shared anti-bot state)
         → Key Vault (secrets + RSA key for Data Protection) → Azure Communication Services Email
         → Application Insights (OpenTelemetry: requests + antibot.* metrics)
```

> **Status:** validated with `az bicep build` and `az bicep lint` (no warnings). It has **not been deployed** by the
> starter's tests because deploying creates billable resources. Run `az deployment group what-if` first.

## 1. Prerequisites

- A resource group: `az group create -n rg-antibot -l eastus2`
- A Microsoft Entra **group** that administers Azure SQL (its name and object ID go in the parameters).
- A **Cloudflare Turnstile** widget for your domain (site key + secret key).
- Pass the Turnstile secret securely: either a Key Vault reference in `main.parameters.json` (bootstrap vault)
  or on the command line: `-p turnstileSecretKey=$TURNSTILE_SECRET` (never commit it).

## 2. Deploy the infrastructure

```bash
az deployment group what-if  -g rg-antibot -f deploy/azure/main.bicep -p deploy/azure/main.parameters.json
az deployment group create   -g rg-antibot -f deploy/azure/main.bicep -p deploy/azure/main.parameters.json
```

Outputs: `siteName`, `frontDoorEndpoint`, `frontDoorId`, `sqlServer`, `sqlDatabase`, `emailFrom`.

## 3. Give the app access to the database (once)

Connect to the database as a member of the SQL admin group (Azure Data Studio / `sqlcmd -G`) and run:

```sql
CREATE USER [app-antibot-<suffix>] FROM EXTERNAL PROVIDER;   -- the App Service name (its managed identity)
ALTER ROLE db_datareader ADD MEMBER [app-antibot-<suffix>];
ALTER ROLE db_datawriter ADD MEMBER [app-antibot-<suffix>];
```

The app never needs DDL rights: migrations run from the pipeline (step 4).

## 4. Apply the migrations (every release, before the new code goes live)

```bash
dotnet tool restore
dotnet ef migrations bundle -p src/AntiBotStarter.Infrastructure -s src/AntiBotStarter.Server -o efbundle --self-contained -r linux-x64
# From a machine allowed by the SQL firewall, signed in (az login) as a member of the SQL admin group:
./efbundle --connection "Server=tcp:<sqlServer>,1433;Database=<sqlDatabase>;Authentication=Active Directory Default;Encrypt=True;"
```

`Database:MigrateOnStartup` is false in production on purpose: several instances must never race to migrate.
Alternative: `dotnet ef migrations script --idempotent` and review the SQL before running it.

## 5. Deploy the app

```bash
dotnet publish src/AntiBotStarter.Server -c Release -o publish
cd publish && zip -r ../app.zip . && cd ..
az webapp deploy -g rg-antibot -n <siteName> --src-path app.zip --type zip
```

Basic (FTP/SCM) publishing credentials are disabled by the template: `az webapp deploy` uses your Entra sign-in.
A GitHub Actions template with OIDC is in `deploy/github/deploy.yml` (copy it to `.github/workflows/`).

## 6. Verify

```bash
curl -I https://<frontDoorEndpoint>/health          # 200 through Front Door
curl -I https://<siteName>.azurewebsites.net/health # 403: the origin is locked to Front Door
curl -s -A "python-requests/2.32" https://<frontDoorEndpoint>/api/config -o /dev/null -w "%{http_code}\n"   # 403
```

Also confirm in the portal (App Service → Health check) that instances report healthy (`/health/ready`).

At startup the app runs `StartupValidation`: if anything required is missing it refuses to start and the
App Service log says exactly what.

## 7. Custom domain and email reputation

- **Domain:** add it to the Front Door endpoint (managed certificate), create the CNAME/TXT records, then redeploy
  with `customHostName=www.example.com` so `AllowedHosts`, `App:PublicBaseUrl` and Turnstile's expected
  hostname follow it.
- **Email:** the template links an *Azure-managed* domain (`DoNotReply@xxxx.azurecomm.net`), which has very low
  sending limits and is only meant for testing. For production add your own domain to the Email Communication
  Service, verify it (SPF, DKIM, DMARC records) and change `Email__FromAddress`. The app-level throttling
  (`Email:Throttle`) keeps abuse from burning your domain's reputation.

## Design notes

- **Client IP:** Front Door sends the client address in `X-Azure-ClientIP`. Because the access restriction only lets
  this Front Door profile in, the app trusts that header from any proxy hop (`KnownNetworks` 0.0.0.0/0 and ::/0).
  Never copy that setting to an origin that is reachable directly.
- **App Service health check** (`/health/ready`) checks SQL and Redis; Front Door probes `/health` (liveness).
- **Secrets** are Key Vault references; the app identity has *Key Vault Secrets User* and *Key Vault Crypto User*.
- **Redis and SQL** accept public connections over TLS with Entra/keys. For network isolation add private endpoints
  (Premium plan VNet integration) and set `publicNetworkAccess: 'Disabled'`.
