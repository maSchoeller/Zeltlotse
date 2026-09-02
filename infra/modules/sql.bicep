param location string
param sqlAadAdminObjectId string
param sqlAadAdminLogin string

@description('Muss weltweit eindeutig sein — Azure SQL Server-Namen teilen sich einen globalen Namensraum.')
param serverName string = 'zeltlotse-sql-${uniqueString(subscription().id)}'

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: serverName
  location: location
  properties: {
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'User'
      login: sqlAadAdminLogin
      sid: sqlAadAdminObjectId
      azureADOnlyAuthentication: true
    }
    minimalTlsVersion: '1.2'
  }
}

// Container Apps hat ohne NAT Gateway keine feste ausgehende IP; AAD-only-Auth
// mitigiert das Risiko einer offenen Firewall (kein SQL-Passwort zu erraten).
// Siehe debt.md dieses Laufs.
resource firewallAllowAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: 'zeltlotse'
  location: location
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 2
  }
  properties: {
    autoPauseDelay: 60
    minCapacity: json('0.5')
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause'
  }
}

output fullyQualifiedDomainName string = sqlServer.properties.fullyQualifiedDomainName
output serverName string = sqlServer.name
