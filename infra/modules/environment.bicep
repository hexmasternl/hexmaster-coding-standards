@description('Name of the Container Apps managed environment.')
param name string

@description('Name of the Log Analytics workspace backing the environment.')
param workspaceName string

@description('Azure region for both resources.')
param location string

@description('Tags applied to both resources.')
param tags object

@description('''
Custom domain to issue a free managed certificate for. Empty issues none. Issuance performs
domain control validation immediately, so the DNS records have to exist before a deployment
that sets this - see the notes in main.bicep.
''')
param customDomainName string = ''

@description('How many days to retain logs. 30 is the free-tier floor.')
@minValue(30)
@maxValue(730)
param logRetentionInDays int = 30

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: workspaceName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: logRetentionInDays
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
  }
}

resource managedEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: workspace.properties.customerId
        sharedKey: workspace.listKeys().primarySharedKey
      }
    }
    // Consumption only: the app scales to zero, so there is no always-on profile to pay for.
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
    zoneRedundant: false
  }
}


// Free, auto-renewing certificate for the custom domain, issued by DigiCert against the
// environment. It lives on the environment rather than the app because that is where
// Container Apps keeps certificates; the app references it by id.
//
// Validation happens during this resource's own deployment, so it fails - and takes the
// deployment with it - unless the CNAME and asuid TXT records already resolve. Renewal
// re-validates, so those records have to stay in place for good.
resource managedCertificate 'Microsoft.App/managedEnvironments/managedCertificates@2024-03-01' = if (!empty(customDomainName)) {
  parent: managedEnvironment
  // Dots are legal in the name but make for awkward resource ids, and the name only has to
  // be unique within the environment.
  name: replace(customDomainName, '.', '-')
  location: location
  tags: tags
  properties: {
    subjectName: customDomainName
    // A subdomain, so it is validated through the CNAME that has to point at the app
    // anyway. Apex domains would need TXT instead.
    domainControlValidation: 'CNAME'
  }
}

output id string = managedEnvironment.id
output name string = managedEnvironment.name
output workspaceId string = workspace.id
output workspaceCustomerId string = workspace.properties.customerId

@description('Resource id of the managed certificate, or empty when no custom domain was given.')
output managedCertificateId string = empty(customDomainName) ? '' : managedCertificate.id
