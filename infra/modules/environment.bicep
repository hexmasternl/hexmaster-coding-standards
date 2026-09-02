@description('Name of the Container Apps managed environment.')
param name string

@description('Name of the Log Analytics workspace backing the environment.')
param workspaceName string

@description('Azure region for both resources.')
param location string

@description('Tags applied to both resources.')
param tags object

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

output id string = managedEnvironment.id
output name string = managedEnvironment.name
output workspaceId string = workspace.id
output workspaceCustomerId string = workspace.properties.customerId
