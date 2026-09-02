param location string
param domainName string
param bindCustomDomain bool

// Kein repositoryUrl: die Auslieferung läuft über die GitHub Action mit dem
// Deployment-Token, nicht über die von Azure verwaltete Repo-Anbindung.
// Siehe design.md, Abschnitt "Secrets".
resource staticWebApp 'Microsoft.Web/staticSites@2023-12-01' = {
  name: 'zeltlotse-client'
  location: location
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {}
}

resource customDomain 'Microsoft.Web/staticSites/customDomains@2023-12-01' = if (bindCustomDomain) {
  parent: staticWebApp
  name: 'app.${domainName}'
}

output defaultHostname string = staticWebApp.properties.defaultHostname
output name string = staticWebApp.name
