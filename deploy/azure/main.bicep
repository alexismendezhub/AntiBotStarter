// AntiBotStarter — production infrastructure on Azure (resource-group scope).
//
//   Internet → Azure Front Door Premium (+ WAF: OWASP DRS + Bot Manager + rate limits)
//            → App Service (Linux, .NET 10) — accepts ONLY Front Door with this profile's X-Azure-FDID
//            → Azure SQL (Entra-only auth) · Azure Managed Redis · Key Vault (secrets + Data Protection key)
//            → Azure Communication Services Email · Application Insights
//
// Deploy:   az deployment group create -g <rg> -f deploy/azure/main.bicep -p deploy/azure/main.parameters.json
// After it: run the post-deployment steps in deploy/azure/README.md (SQL user for the app, migrations, DNS).

targetScope = 'resourceGroup'

@description('Short name used in every resource name (lowercase letters and digits).')
@minLength(3)
@maxLength(12)
param appName string

param location string = resourceGroup().location

@description('Microsoft Entra group that administers Azure SQL (Entra-only authentication).')
param sqlAdminGroupName string

@description('Object ID of that Entra group.')
param sqlAdminGroupObjectId string

@description('Cloudflare Turnstile site key (public).')
param turnstileSiteKey string

@description('Cloudflare Turnstile secret key. Stored only in Key Vault.')
@secure()
param turnstileSecretKey string

@description('Public host name of the site when you add a custom domain (e.g. www.example.com). Empty = the Front Door endpoint host.')
param customHostName string = ''

@description('App Service plan SKU (P0v3 or higher for production).')
param appServiceSku string = 'P0v3'

@description('Number of App Service instances. With more than one, Redis keeps the anti-bot state shared.')
@minValue(1)
param instanceCount int = 2

var suffix = uniqueString(resourceGroup().id)
var names = {
  logs: 'log-${appName}-${suffix}'
  insights: 'appi-${appName}-${suffix}'
  vault: take('kv-${appName}-${suffix}', 24)
  sql: 'sql-${appName}-${suffix}'
  db: appName
  redis: 'redis-${appName}-${suffix}'
  acs: 'acs-${appName}-${suffix}'
  email: 'email-${appName}-${suffix}'
  plan: 'asp-${appName}-${suffix}'
  site: 'app-${appName}-${suffix}'
  frontDoor: 'afd-${appName}-${suffix}'
  waf: 'waf${appName}${suffix}'
}

// Built-in role definitions
var keyVaultSecretsUser = '4633458b-17de-408a-b874-0445c86b69e6'
var keyVaultCryptoUser = '12338af0-0e69-4776-bea7-57ae8d297424'

// ---------- Monitoring ----------
resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: names.logs
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: names.insights
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
    DisableLocalAuth: false
  }
}

// ---------- Key Vault: secrets + the key that encrypts the Data Protection key ring ----------
resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: names.vault
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true
    publicNetworkAccess: 'Enabled'
  }
}

resource dataProtectionKey 'Microsoft.KeyVault/vaults/keys@2023-07-01' = {
  parent: vault
  name: 'dataprotection'
  properties: {
    kty: 'RSA'
    keySize: 3072
    keyOps: [ 'wrapKey', 'unwrapKey' ]
  }
}

resource turnstileSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: vault
  name: 'turnstile-secret'
  properties: { value: turnstileSecretKey }
}

// ---------- Azure SQL (Entra-only authentication: no SQL passwords exist) ----------
resource sql 'Microsoft.Sql/servers@2023-08-01' = {
  name: names.sql
  location: location
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: sqlAdminGroupName
      sid: sqlAdminGroupObjectId
      tenantId: subscription().tenantId
      principalType: 'Group'
    }
  }
}

resource sqlAllowAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01' = {
  parent: sql
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource db 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: sql
  name: names.db
  location: location
  sku: { name: 'GP_S_Gen5_1', tier: 'GeneralPurpose' }
  properties: {
    autoPauseDelay: 60
    minCapacity: json('0.5')
    requestedBackupStorageRedundancy: 'Zone'
  }
}

// ---------- Azure Managed Redis: rate limits, blocklist and form-token nonces shared by every instance ----------
resource redis 'Microsoft.Cache/redisEnterprise@2025-07-01' = {
  name: names.redis
  location: location
  sku: { name: 'Balanced_B0' }
  properties: {
    minimumTlsVersion: '1.2'
    // TLS + access key from Key Vault. For stricter isolation use a private endpoint and set this to Disabled.
    publicNetworkAccess: 'Enabled'
  }
}

resource redisDb 'Microsoft.Cache/redisEnterprise/databases@2025-07-01' = {
  parent: redis
  name: 'default'
  properties: {
    clientProtocol: 'Encrypted'
    port: 10000
    clusteringPolicy: 'NoCluster'
    evictionPolicy: 'NoEviction'
    accessKeysAuthentication: 'Enabled'
  }
}

resource redisSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: vault
  name: 'redis-connection'
  properties: {
    value: '${redis.properties.hostName}:10000,password=${redisDb.listKeys().primaryKey},ssl=True,abortConnect=False'
  }
}

// ---------- Email: Azure Communication Services with an Azure-managed domain ----------
resource emailService 'Microsoft.Communication/emailServices@2023-04-01' = {
  name: names.email
  location: 'global'
  properties: { dataLocation: 'United States' }
}

resource emailDomain 'Microsoft.Communication/emailServices/domains@2023-04-01' = {
  parent: emailService
  name: 'AzureManagedDomain'
  location: 'global'
  properties: {
    domainManagement: 'AzureManaged'
    userEngagementTracking: 'Disabled'
  }
}

resource acs 'Microsoft.Communication/communicationServices@2023-04-01' = {
  name: names.acs
  location: 'global'
  properties: {
    dataLocation: 'United States'
    linkedDomains: [ emailDomain.id ]
  }
}

resource acsSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: vault
  name: 'acs-connection'
  properties: { value: acs.listKeys().primaryConnectionString }
}

// ---------- Front Door Premium + WAF ----------
module waf 'frontdoor-waf.bicep' = {
  name: 'waf'
  params: { policyName: names.waf }
}

resource frontDoor 'Microsoft.Cdn/profiles@2024-02-01' = {
  name: names.frontDoor
  location: 'global'
  sku: { name: 'Premium_AzureFrontDoor' }
  properties: {
    originResponseTimeoutSeconds: 60
  }
}

resource endpoint 'Microsoft.Cdn/profiles/afdEndpoints@2024-02-01' = {
  parent: frontDoor
  name: 'ep-${appName}-${suffix}'
  location: 'global'
  properties: { enabledState: 'Enabled' }
}

var publicHost = empty(customHostName) ? endpoint.properties.hostName : customHostName

// ---------- App Service (only reachable through this Front Door profile) ----------
resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: names.plan
  location: location
  kind: 'linux'
  sku: { name: appServiceSku, capacity: instanceCount }
  properties: {
    reserved: true
    zoneRedundant: false
  }
}

resource site 'Microsoft.Web/sites@2023-12-01' = {
  name: names.site
  location: location
  kind: 'app,linux'
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: false
    publicNetworkAccess: 'Enabled'
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      http20Enabled: true
      minTlsVersion: '1.2'
      scmMinTlsVersion: '1.2'
      ftpsState: 'Disabled'
      healthCheckPath: '/health/ready'
      // Origin lock (network level): only Azure Front Door, and only THIS profile (X-Azure-FDID).
      ipSecurityRestrictionsDefaultAction: 'Deny'
      ipSecurityRestrictions: [
        {
          name: 'FrontDoorOnly'
          ipAddress: 'AzureFrontDoor.Backend'
          tag: 'ServiceTag'
          action: 'Allow'
          priority: 100
          headers: { 'x-azure-fdid': [ frontDoor.properties.frontDoorId ] }
        }
      ]
      scmIpSecurityRestrictionsUseMain: false
      appSettings: [
        { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insights.properties.ConnectionString }
        { name: 'AllowedHosts', value: publicHost }
        { name: 'App__PublicBaseUrl', value: 'https://${publicHost}' }
        { name: 'AntiBot__Captcha__SiteKey', value: turnstileSiteKey }
        { name: 'AntiBot__Captcha__SecretKey', value: '@Microsoft.KeyVault(SecretUri=${turnstileSecret.properties.secretUri})' }
        { name: 'AntiBot__Captcha__ExpectedHostnames__0', value: publicHost }
        // Origin lock (application level) — defense in depth on top of the access restriction.
        { name: 'AntiBot__OriginLock__Enabled', value: 'true' }
        { name: 'AntiBot__OriginLock__HeaderName', value: 'X-Azure-FDID' }
        { name: 'AntiBot__OriginLock__ExpectedValues__0', value: frontDoor.properties.frontDoorId }
        // Real client IP: Front Door puts it in X-Azure-ClientIP. The access restriction guarantees every request
        // comes from Front Door, so the App Service front ends in between can be trusted (see deploy/azure/README.md).
        { name: 'AntiBot__ForwardedHeaders__ForwardedForHeaderName', value: 'X-Azure-ClientIP' }
        { name: 'AntiBot__ForwardedHeaders__KnownNetworks__0', value: '0.0.0.0/0' }
        { name: 'AntiBot__ForwardedHeaders__KnownNetworks__1', value: '::/0' }
        { name: 'Database__Provider', value: 'SqlServer' }
        { name: 'Database__MigrateOnStartup', value: 'false' }
        { name: 'DataProtection__KeyVaultKeyId', value: dataProtectionKey.properties.keyUri }
        { name: 'Distributed__RedisConnectionString', value: '@Microsoft.KeyVault(SecretUri=${redisSecret.properties.secretUri})' }
        { name: 'Email__Transport', value: 'AzureCommunicationServices' }
        { name: 'Email__FromAddress', value: 'DoNotReply@${emailDomain.properties.mailFromSenderDomain}' }
        { name: 'Email__AzureCommunicationServices__ConnectionString', value: '@Microsoft.KeyVault(SecretUri=${acsSecret.properties.secretUri})' }
      ]
      connectionStrings: [
        {
          name: 'Default'
          type: 'SQLAzure'
          // Managed identity of the app: no password anywhere.
          connectionString: 'Server=tcp:${sql.properties.fullyQualifiedDomainName},1433;Database=${names.db};Authentication=Active Directory Managed Identity;Encrypt=True;'
        }
      ]
    }
  }
}

resource ftpPolicy 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2023-12-01' = {
  parent: site
  name: 'ftp'
  properties: { allow: false }
}

resource scmPolicy 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2023-12-01' = {
  parent: site
  name: 'scm'
  properties: { allow: false }
}

resource secretsRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, site.id, keyVaultSecretsUser)
  scope: vault
  properties: {
    principalId: site.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUser)
  }
}

resource cryptoRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(dataProtectionKey.id, site.id, keyVaultCryptoUser)
  scope: vault
  properties: {
    principalId: site.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultCryptoUser)
  }
}

// ---------- Front Door routing to the App Service ----------
resource originGroup 'Microsoft.Cdn/profiles/originGroups@2024-02-01' = {
  parent: frontDoor
  name: 'app'
  properties: {
    loadBalancingSettings: { sampleSize: 4, successfulSamplesRequired: 3, additionalLatencyInMilliseconds: 50 }
    healthProbeSettings: { probePath: '/health', probeRequestType: 'GET', probeProtocol: 'Https', probeIntervalInSeconds: 60 }
    sessionAffinityState: 'Disabled'
  }
}

resource origin 'Microsoft.Cdn/profiles/originGroups/origins@2024-02-01' = {
  parent: originGroup
  name: 'appservice'
  properties: {
    hostName: site.properties.defaultHostName
    originHostHeader: site.properties.defaultHostName
    httpPort: 80
    httpsPort: 443
    priority: 1
    weight: 1000
    enabledState: 'Enabled'
    enforceCertificateNameCheck: true
  }
}

resource route 'Microsoft.Cdn/profiles/afdEndpoints/routes@2024-02-01' = {
  parent: endpoint
  name: 'default'
  dependsOn: [ origin ]
  properties: {
    originGroup: { id: originGroup.id }
    supportedProtocols: [ 'Https', 'Http' ]
    httpsRedirect: 'Enabled'
    forwardingProtocol: 'HttpsOnly'
    linkToDefaultDomain: 'Enabled'
    patternsToMatch: [ '/*' ]
    enabledState: 'Enabled'
  }
}

resource securityPolicy 'Microsoft.Cdn/profiles/securityPolicies@2024-02-01' = {
  parent: frontDoor
  name: 'waf'
  properties: {
    parameters: {
      type: 'WebApplicationFirewall'
      wafPolicy: { id: waf.outputs.wafPolicyId }
      associations: [
        {
          domains: [ { id: endpoint.id } ]
          patternsToMatch: [ '/*' ]
        }
      ]
    }
  }
}

output siteName string = site.name
output siteIdentityPrincipalId string = site.identity.principalId
output frontDoorEndpoint string = 'https://${endpoint.properties.hostName}'
output frontDoorId string = frontDoor.properties.frontDoorId
output sqlServer string = sql.properties.fullyQualifiedDomainName
output sqlDatabase string = names.db
output emailFrom string = 'DoNotReply@${emailDomain.properties.mailFromSenderDomain}'
