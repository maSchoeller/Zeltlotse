targetScope = 'subscription'

@description('Immer Germany West Central, wie in foundation.md festgelegt.')
param location string = 'germanywestcentral'

@description('Name der Resource Group.')
param resourceGroupName string = 'zeltlotse-produktion'

@description('Registrierbare Domain, unter der app./api. liegen (z.B. zeltlotse.de).')
param domainName string

@description('Objekt-ID des Azure-AD-Kontos oder der Gruppe, die als SQL-Administrator verwaltet (AAD-only-Auth, kein SQL-Passwort).')
param sqlAadAdminObjectId string

@description('Anzeigename des SQL-Administrators (Login-Name in Azure AD).')
param sqlAadAdminLogin string

@description('Vollqualifizierter Image-Tag für den Server, z.B. ghcr.io/maschoeller/zeltlotse-server:<sha>.')
param serverImage string

@description('Benutzername für den ghcr.io-Registry-Zugriff.')
param ghcrUsername string

@secure()
@description('Lesezugriffs-Token für ghcr.io (Container-Apps-Secret, kein Klartext im Bicep-State).')
param ghcrToken string

@secure()
@description('JWT-Signierschlüssel, mindestens 32 Zeichen. Wird einmalig manuell gesetzt, siehe design.md.')
param jwtSigningKey string

@description('Erst bei true werden die Custom-Domain-Bindungen angelegt — beim allerersten Deploy noch false, siehe design.md Sequenzierung.')
param bindCustomDomain bool = false

resource resourceGroup 'Microsoft.Resources/resourceGroups@2024-11-01' = {
  name: resourceGroupName
  location: location
}

module logAnalytics 'modules/log-analytics.bicep' = {
  scope: resourceGroup
  name: 'log-analytics'
  params: {
    location: location
  }
}

module containerAppsEnv 'modules/container-apps-env.bicep' = {
  scope: resourceGroup
  name: 'container-apps-env'
  params: {
    location: location
    logAnalyticsCustomerId: logAnalytics.outputs.customerId
    logAnalyticsSharedKey: logAnalytics.outputs.sharedKey
  }
}

module sql 'modules/sql.bicep' = {
  scope: resourceGroup
  name: 'sql'
  params: {
    location: location
    sqlAadAdminObjectId: sqlAadAdminObjectId
    sqlAadAdminLogin: sqlAadAdminLogin
  }
}

module identity 'modules/identity.bicep' = {
  scope: resourceGroup
  name: 'identity'
  params: {
    location: location
  }
}

module keyVault 'modules/key-vault.bicep' = {
  scope: resourceGroup
  name: 'key-vault'
  params: {
    location: location
    serverIdentityPrincipalId: identity.outputs.principalId
    ghcrToken: ghcrToken
    jwtSigningKey: jwtSigningKey
  }
}

module server 'modules/container-app-server.bicep' = {
  scope: resourceGroup
  name: 'container-app-server'
  params: {
    location: location
    containerAppsEnvId: containerAppsEnv.outputs.id
    identityId: identity.outputs.id
    identityClientId: identity.outputs.clientId
    serverImage: serverImage
    ghcrUsername: ghcrUsername
    ghcrTokenSecretUri: keyVault.outputs.ghcrTokenSecretUri
    jwtSigningKeySecretUri: keyVault.outputs.jwtSigningKeySecretUri
    sqlServerFqdn: sql.outputs.fullyQualifiedDomainName
    clientOrigin: 'https://app.${domainName}'
    domainName: domainName
    bindCustomDomain: bindCustomDomain
  }
}

// Azure Static Web Apps ist in Germany West Central nicht verfügbar
// (Stand dieses Laufs: nur centralus, eastus2, westus2, westeurope,
// eastasia) — West Europe liegt geografisch am nächsten.
module staticWebApp 'modules/static-web-app.bicep' = {
  scope: resourceGroup
  name: 'static-web-app'
  params: {
    location: 'westeurope'
    domainName: domainName
    bindCustomDomain: bindCustomDomain
  }
}

output resourceGroupName string = resourceGroup.name
output serverDefaultHostname string = server.outputs.defaultHostname
output staticWebAppDefaultHostname string = staticWebApp.outputs.defaultHostname
output staticWebAppName string = staticWebApp.outputs.name
