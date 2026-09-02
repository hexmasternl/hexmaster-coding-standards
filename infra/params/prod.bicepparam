using '../main.bicep'

param applicationName = 'hexmaster-codingstandards'
param environmentName = 'prod'
param location = 'swedencentral'

// Bounds the cost blast radius on a public, unauthenticated endpoint.
param maxReplicas = 3

param documentsOwner = 'hexmasternl'
param documentsRepository = 'hexmaster-coding-standards'
param documentsRef = 'main'
param documentsCatalogCacheLifetime = '00:30:00'

// containerImage and the registry parameters are deliberately not set here. CD passes the
// commit-SHA-tagged image together with the registry login server and credentials, which
// are repository secrets and so must never appear in a checked-in parameter file. With no
// values the template's public placeholder runs instead.
