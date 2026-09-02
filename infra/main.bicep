targetScope = 'subscription'

metadata description = '''
Infrastructure for the HexMaster coding standards MCP server: a resource group holding a
Container Apps environment and a container app that scales to zero.

The deployment is subscription scoped so it creates its own resource group, which makes a
release a single deployment into a bare subscription. The container registry is not
deployed here. An existing registry is used, and the pipeline passes its login server and
credentials in, so a release is: build and push the image to that registry, then deploy
this template once pinned to the pushed image.
'''

@description('Short application name used to derive resource names.')
@minLength(3)
@maxLength(32)
param applicationName string = 'hexmaster-codingstandards'

@description('Environment name, used in resource names and tags.')
@allowed(['dev', 'test', 'prod'])
param environmentName string

@description('Resource group this deployment creates and deploys into.')
@minLength(1)
@maxLength(90)
param resourceGroupName string = 'rg-${applicationName}-${environmentName}'

@description('Azure region for the resource group and every resource in it.')
param location string = deployment().location

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

@description('''
Custom domain to serve the MCP server on. Empty deploys no certificate and binds no
hostname, which is what a deployment into a subscription whose DNS is not set up yet needs.

Setting it puts two prerequisites on the DNS zone, both of which have to resolve *before*
the deployment runs and have to keep resolving afterwards, because certificate renewal
re-validates them:

  CNAME  <subdomain>         -> the app's default FQDN (the `fqdn` output)
  TXT    asuid.<subdomain>   -> the `customDomainVerificationId` output

That ordering is the awkward part of a first deployment: the CNAME target only exists once
the app does. Deploy once with this empty, take the two outputs, create the records, then set
the domain with bindCustomDomainCertificate false, and once more with it true.
''')
param customDomainName string = ''

@description('''
Whether to issue the managed certificate and secure the custom domain with it. Ignored when
no custom domain is given.

This exists because Azure wants the hostname added to the app *before* a certificate is
issued for it, and one ARM deployment cannot do both to the same resource. False adds the
hostname over HTTP only; true issues the certificate and switches the binding to SNI. Prod
runs with true - false is only for the one deployment that first introduces a domain.
''')
param bindCustomDomainCertificate bool = true

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

// Not named `resourceGroup`, which would shadow the function of the same name.
resource group 'Microsoft.Resources/resourceGroups@2024-07-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

module environment 'modules/environment.bicep' = {
  scope: group
  name: 'environment'
  params: {
    name: '${applicationName}-${environmentName}-env'
    workspaceName: '${applicationName}-${environmentName}-logs'
    // Empty until the certificate is wanted, so the environment issues none while the
    // hostname is still being introduced to the app.
    customDomainName: bindCustomDomainCertificate ? customDomainName : ''
    location: location
    tags: tags
  }
}

module containerApp 'modules/containerApp.bicep' = {
  scope: group
  name: 'containerApp'
  params: {
    name: '${applicationName}-${environmentName}'
    location: location
    tags: tags
    managedEnvironmentId: environment.outputs.id
    customDomainName: customDomainName
    customDomainCertificateId: environment.outputs.managedCertificateId
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

@description('Resource group everything was deployed into.')
output resourceGroupName string = group.name

@description('Public HTTPS endpoint of the MCP server.')
output url string = containerApp.outputs.url

@description('Ingress hostname of the MCP server.')
output fqdn string = containerApp.outputs.fqdn

@description('Container app name, for deployment and diagnostics.')
output containerAppName string = containerApp.outputs.name

@description('Public HTTPS endpoint on the custom domain, or empty when none is bound.')
output customDomainUrl string = containerApp.outputs.customDomainUrl

@description('Value the asuid TXT record has to carry for the custom domain to validate.')
output customDomainVerificationId string = containerApp.outputs.customDomainVerificationId
