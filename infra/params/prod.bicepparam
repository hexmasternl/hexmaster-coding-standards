using '../main.bicep'

param applicationName = 'hexmaster-codingstandards'
param environmentName = 'prod'
param location = 'swedencentral'

// The template is subscription scoped and creates this resource group itself.
param resourceGroupName = 'rg-hexmaster-codingstandards-prod'

// Served on this hostname, secured by a free managed certificate on the environment. The
// zone needs a CNAME to the app FQDN and an asuid TXT record with the deployment's
// customDomainVerificationId output, both before this deploys - see main.bicep.
param customDomainName = 'standards-mcp.hexmaster.nl'

// Bounds the cost blast radius on a public, unauthenticated endpoint.
param maxReplicas = 3

param documentsOwner = 'hexmasternl'
param documentsRepository = 'hexmaster-coding-standards'
param documentsRef = 'main'
param documentsCatalogCacheLifetime = '00:30:00'

// containerImage and the registry parameters are deliberately not set here. CD passes the
// version-tagged image together with the registry login server and credentials, which
// are repository secrets and so must never appear in a checked-in parameter file. With no
// values the template's public placeholder runs instead.
