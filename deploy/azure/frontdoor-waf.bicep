// SAMPLE: Azure Front Door Premium WAF policy
// with OWASP managed rules + Bot Manager and rate limiting on authentication routes.
// Deploy: az deployment group create -g <rg> -f deploy/azure/frontdoor-waf.bicep
// Then associate the policy with the Front Door endpoint (Security policy) and enable OriginLock in appsettings
// with the X-Azure-FDID value of your Front Door profile.

@description('WAF policy name (letters and digits only)')
param policyName string = 'antibotwaf'

resource waf 'Microsoft.Network/FrontDoorWebApplicationFirewallPolicies@2024-02-01' = {
  name: policyName
  location: 'Global'
  sku: {
    name: 'Premium_AzureFrontDoor'
  }
  properties: {
    policySettings: {
      enabledState: 'Enabled'
      mode: 'Prevention'
      requestBodyCheck: 'Enabled'
      customBlockResponseStatusCode: 403
    }
    managedRules: {
      managedRuleSets: [
        {
          ruleSetType: 'Microsoft_DefaultRuleSet'
          ruleSetVersion: '2.1'
          ruleSetAction: 'Block'
        }
        {
          ruleSetType: 'Microsoft_BotManagerRuleSet'
          ruleSetVersion: '1.1'
        }
      ]
    }
    customRules: {
      rules: [
        {
          name: 'RateLimitAuth'
          priority: 10
          enabledState: 'Enabled'
          ruleType: 'RateLimitRule'
          rateLimitDurationInMinutes: 1
          rateLimitThreshold: 20
          matchConditions: [
            {
              matchVariable: 'RequestUri'
              operator: 'Contains'
              matchValue: [ '/api/auth/' ]
              transforms: [ 'Lowercase' ]
            }
          ]
          action: 'Block'
        }
        {
          name: 'RateLimitGlobal'
          priority: 20
          enabledState: 'Enabled'
          ruleType: 'RateLimitRule'
          rateLimitDurationInMinutes: 1
          rateLimitThreshold: 300
          matchConditions: [
            {
              matchVariable: 'RequestUri'
              operator: 'Any'
              matchValue: []
            }
          ]
          action: 'Block'
        }
        {
          name: 'BlockEmptyUserAgent'
          priority: 30
          enabledState: 'Enabled'
          ruleType: 'MatchRule'
          matchConditions: [
            {
              matchVariable: 'RequestHeader'
              selector: 'User-Agent'
              operator: 'Any'
              negateCondition: true
              matchValue: []
            }
          ]
          action: 'Block'
        }
      ]
    }
  }
}

output wafPolicyId string = waf.id
