@description('Name of the container app.')
param name string

@description('Azure region for the container app.')
param location string

@description('Tags applied to the container app.')
param tags object

@description('Resource id of the Container Apps managed environment.')
param managedEnvironmentId string

@description('Container image to run, including registry and tag.')
param containerImage string

@description('Login server of the existing registry to pull from. Empty when running a public image.')
param registryLoginServer string = ''

@description('Username for the registry to pull from.')
param registryUsername string = ''

@description('Password for the registry to pull from.')
@secure()
param registryPassword string = ''

@description('Highest number of replicas HTTP scaling may create.')
@minValue(1)
@maxValue(30)
param maxReplicas int = 3

@description('Concurrent requests per replica before another replica is added.')
@minValue(1)
param concurrentRequestsPerReplica int = 50

@description('CPU cores per replica.')
param cpu string = '0.5'

@description('Memory per replica.')
param memory string = '1Gi'

@description('GitHub account owning the content repository.')
param documentsOwner string

@description('Content repository name.')
param documentsRepository string

@description('Branch, tag, or commit to serve documents from.')
param documentsRef string

@description('How often the content set is refreshed from GitHub.')
param documentsCatalogCacheLifetime string

@description('Custom domain to bind to ingress. Empty binds none.')
param customDomainName string = ''

@description('''
Resource id of the certificate to secure the custom domain with. Empty leaves the domain
unbound: a hostname may only be bound with a certificate, never without one.
''')
param customDomainCertificateId string = ''

@description('Port the container listens on.')
param containerPort int = 8080

// Only set a registry block when a private registry was supplied. Without one the app runs
// a public placeholder image, and naming a registry it has no credentials for would fail
// the revision.
var usesRegistry = !empty(registryLoginServer)

// The platform will not take a registry password inline: it has to be a named secret that
// the registry block references.
var registryPasswordSecretName = 'registry-password'


// The hostname is bound as soon as a domain is given, because that is what makes Azure
// validate ownership and is the prerequisite for issuing a certificate for it. It is only
// served over HTTPS once a certificate id arrives, at which point the binding becomes SNI.
var usesCustomDomain = !empty(customDomainName)
var securesCustomDomain = usesCustomDomain && !empty(customDomainCertificateId)

resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    managedEnvironmentId: managedEnvironmentId
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: containerPort
        transport: 'auto'
        // TLS is terminated here. The app itself does no HTTPS redirection, so refusing
        // insecure traffic at the edge is what actually enforces HTTPS.
        allowInsecure: false
        customDomains: !usesCustomDomain ? [] : securesCustomDomain ? [
          {
            name: customDomainName
            bindingType: 'SniEnabled'
            certificateId: customDomainCertificateId
          }
        ] : [
          {
            name: customDomainName
            bindingType: 'Disabled'
          }
        ]
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
      }
      secrets: usesRegistry ? [
        {
          name: registryPasswordSecretName
          value: registryPassword
        }
      ] : []
      registries: usesRegistry ? [
        {
          server: registryLoginServer
          username: registryUsername
          passwordSecretRef: registryPasswordSecretName
        }
      ] : []
    }
    template: {
      containers: [
        {
          name: 'mcp'
          image: containerImage
          resources: {
            cpu: json(cpu)
            memory: memory
          }
          env: [
            {
              name: 'Documents__Owner'
              value: documentsOwner
            }
            {
              name: 'Documents__Repository'
              value: documentsRepository
            }
            {
              name: 'Documents__Ref'
              value: documentsRef
            }
            {
              name: 'Documents__CatalogCacheLifetime'
              value: documentsCatalogCacheLifetime
            }
          ]
          probes: [
            {
              type: 'Readiness'
              httpGet: {
                path: '/health'
                port: containerPort
              }
              initialDelaySeconds: 5
              periodSeconds: 10
              failureThreshold: 6
            }
            {
              type: 'Liveness'
              httpGet: {
                path: '/health'
                port: containerPort
              }
              // Generous: a GitHub outage must not restart a replica that is happily
              // serving cached content, and /health only fails when nothing has loaded.
              initialDelaySeconds: 30
              periodSeconds: 60
              failureThreshold: 10
            }
          ]
        }
      ]
      scale: {
        // Scale to zero when idle. The cost is a cold start - process start plus one
        // catalog download - on the first request after an idle period.
        minReplicas: 0
        maxReplicas: maxReplicas
        rules: [
          {
            name: 'http-concurrency'
            http: {
              metadata: {
                concurrentRequests: '${concurrentRequestsPerReplica}'
              }
            }
          }
        ]
      }
    }
  }
}

output name string = containerApp.name
output fqdn string = containerApp.properties.configuration.ingress.fqdn
output url string = 'https://${containerApp.properties.configuration.ingress.fqdn}'

@description('Public HTTPS endpoint on the custom domain, or empty when none is bound.')
output customDomainUrl string = securesCustomDomain ? 'https://${customDomainName}' : ''

@description('''
Value the asuid TXT record has to carry for a custom domain to validate. Constant per
subscription, so it is the same for every domain and every deployment.
''')
output customDomainVerificationId string = containerApp.properties.customDomainVerificationId
