@description('Name of the container registry. Registry names allow letters and digits only.')
@minLength(5)
@maxLength(50)
param name string

@description('Azure region for the registry.')
param location string

@description('Tags applied to the registry.')
param tags object

@description('Principal id granted AcrPull on this registry. Empty to grant nothing.')
param acrPullPrincipalId string = ''

resource registry 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: 'Basic'
  }
  properties: {
    // The container app pulls with a managed identity, so there is no reason for admin
    // credentials to exist - and every reason for them not to.
    adminUserEnabled: false
    anonymousPullEnabled: false
    publicNetworkAccess: 'Enabled'
  }
}

// Granted here rather than in main.bicep because the assignment name must be computable at
// the start of the deployment, and the registry id is only known inside this module.
resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(acrPullPrincipalId)) {
  name: guid(registry.id, acrPullPrincipalId, 'AcrPull')
  scope: registry
  properties: {
    principalId: acrPullPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      '7f951dda-4ed3-4680-a7ca-43fe172d538d' // AcrPull
    )
  }
}

output id string = registry.id
output name string = registry.name
output loginServer string = registry.properties.loginServer
