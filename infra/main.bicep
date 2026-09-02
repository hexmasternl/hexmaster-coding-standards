targetScope = 'resourceGroup'

metadata description = '''
Infrastructure for the HexMaster coding standards MCP server: a Container Apps environment
and a container app that scales to zero.

The container registry is not deployed here. An existing registry is used, and the pipeline
passes its login server and credentials in, so a release is: build and push the image to
that registry, then deploy this template once pinned to the pushed image.
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
Container image to run. Defaults to a publicly pullable placeholder so a deployment without
registry credentials still produces a running app. Deployments should pass an image tagged
with a commit SHA, never a moving tag.
''')
param containerImage string = 'mcr.microsoft.com/k8se/quickstart:latest'

@description('''
Login server of the existing container registry the application image is pulled from, for
example `myregistry.azurecr.io`. Leave empty when running a public image.
''')
param registryLoginServer string = ''

@description('Username for the existing container registry.')
param registryUsername string = ''

@description('Password for the existing container registry.')
@secure()
param registryPassword string = ''

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

var tags = {
  application: applicationName
  environment: environmentName
  'managed-by': 'bicep'
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
    registryLoginServer: registryLoginServer
    registryUsername: registryUsername
    registryPassword: registryPassword
    maxReplicas: maxReplicas
    documentsOwner: documentsOwner
    documentsRepository: documentsRepository
    documentsRef: documentsRef
    documentsCatalogCacheLifetime: documentsCatalogCacheLifetime
  }
}

@description('Public HTTPS endpoint of the MCP server.')
output url string = containerApp.outputs.url

@description('Ingress hostname of the MCP server.')
output fqdn string = containerApp.outputs.fqdn

@description('Container app name, for deployment and diagnostics.')
output containerAppName string = containerApp.outputs.name
