targetScope = 'resourceGroup'

metadata description = '''
Infrastructure for the HexMaster coding standards MCP server: a Container Apps environment,
a container registry, and a container app that scales to zero.

This template is deployed twice per release. The first deployment creates the registry so
there is somewhere to push to; the image is then built and pushed; the second deployment
pins the app to that image. On a fresh resource group the first deployment runs a public
placeholder image, which is what makes the stack self-bootstrapping.
'''

@description('Short application name used to derive resource names.')
@minLength(3)
@maxLength(32)
param applicationName string = 'hexmaster-codingstandards'

@description('Environment name, used in resource names and tags.')
@allowed(['dev', 'test', 'prod'])
param environmentName string

@description('Azure region for every resource.')
param location string = resourceGroup().location

@description('''
Container image to run. Defaults to a publicly pullable placeholder so a first deployment
into an empty resource group succeeds before any application image exists. Deployments
should pass an image tagged with a commit SHA, never a moving tag.
''')
param containerImage string = 'mcr.microsoft.com/k8se/quickstart:latest'

@description('Highest number of replicas HTTP scaling may create.')
@minValue(1)
@maxValue(30)
param maxReplicas int = 3

@description('GitHub account owning the content repository.')
param documentsOwner string = 'hexmasternl'

@description('Content repository name.')
param documentsRepository string = 'hexmaster-coding-standards'

@description('Branch, tag, or commit to serve documents from.')
param documentsRef string = 'main'

@description('How long a loaded catalog is served before the next request re-fetches it.')
param documentsCatalogCacheLifetime string = '00:30:00'

// A registry name allows letters and digits only, so hyphens are stripped and a short hash
// of the resource group id keeps it globally unique.
var registryName = take('${replace(applicationName, '-', '')}${environmentName}${uniqueString(resourceGroup().id)}', 50)

var tags = {
  application: applicationName
  environment: environmentName
  'managed-by': 'bicep'
}

// Only reference the registry once an application image is actually being deployed. While
// the placeholder is running there is nothing in the registry to pull, and pointing the app
// at an empty registry would fail its first revision.
var usesRegistry = !startsWith(containerImage, 'mcr.microsoft.com/')

// Declared before the registry so its principal id can be granted AcrPull as part of the
// registry deployment, rather than in a later, separately ordered assignment.
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${applicationName}-${environmentName}-id'
  location: location
  tags: tags
}

module registry 'modules/registry.bicep' = {
  name: 'registry'
  params: {
    name: registryName
    location: location
    tags: tags
    acrPullPrincipalId: identity.properties.principalId
  }
}

module environment 'modules/environment.bicep' = {
  name: 'environment'
  params: {
    name: '${applicationName}-${environmentName}-env'
    workspaceName: '${applicationName}-${environmentName}-logs'
    location: location
    tags: tags
  }
}

module containerApp 'modules/containerApp.bicep' = {
  name: 'containerApp'
  params: {
    name: '${applicationName}-${environmentName}'
    location: location
    tags: tags
    managedEnvironmentId: environment.outputs.id
    containerImage: containerImage
    registryLoginServer: usesRegistry ? registry.outputs.loginServer : ''
    userAssignedIdentityId: identity.id
    maxReplicas: maxReplicas
    documentsOwner: documentsOwner
    documentsRepository: documentsRepository
    documentsRef: documentsRef
    documentsCatalogCacheLifetime: documentsCatalogCacheLifetime
  }
}

@description('Registry to push application images to.')
output registryLoginServer string = registry.outputs.loginServer

@description('Registry name, for az acr login.')
output registryName string = registry.outputs.name

@description('Public HTTPS endpoint of the MCP server.')
output url string = containerApp.outputs.url

@description('Ingress hostname of the MCP server.')
output fqdn string = containerApp.outputs.fqdn

@description('Container app name, for deployment and diagnostics.')
output containerAppName string = containerApp.outputs.name
