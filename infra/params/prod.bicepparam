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

// containerImage is deliberately not set here. CD passes the commit-SHA-tagged image; with
// no value the template's public placeholder runs, which is what lets a first deployment
// into an empty resource group succeed.
