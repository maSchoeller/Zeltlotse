param location string
param serverIdentityPrincipalId string
@secure()
param ghcrToken string
@secure()
param jwtSigningKey string

@description('Muss weltweit eindeutig sein — Key-Vault-Namen teilen sich einen globalen Namensraum und sind auf 24 Zeichen begrenzt.')
param vaultName string = 'zl-kv-${uniqueString(resourceGroup().id)}'

var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: vaultName
  location: location
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
  }
}

resource ghcrTokenSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: vault
  name: 'ghcr-token'
  properties: {
    value: ghcrToken
  }
}

resource jwtSigningKeySecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: vault
  name: 'jwt-signing-key'
  properties: {
    value: jwtSigningKey
  }
}

// Erlaubt der Server-Identität, die beiden Secrets zur Laufzeit zu lesen —
// ohne diese Rolle scheitert die Key-Vault-Referenz der Container App.
resource secretsUserRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, serverIdentityPrincipalId, keyVaultSecretsUserRoleId)
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: serverIdentityPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output ghcrTokenSecretUri string = ghcrTokenSecret.properties.secretUri
output jwtSigningKeySecretUri string = jwtSigningKeySecret.properties.secretUri
