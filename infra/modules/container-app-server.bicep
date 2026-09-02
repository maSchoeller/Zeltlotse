param location string
param containerAppsEnvId string
param identityId string
param identityClientId string
param serverImage string
param ghcrUsername string
param ghcrTokenSecretUri string
param jwtSigningKeySecretUri string
param sqlServerFqdn string
param clientOrigin string
param domainName string
param bindCustomDomain bool

var sqlConnectionString = 'Server=tcp:${sqlServerFqdn},1433;Database=zeltlotse;Authentication=Active Directory Managed Identity;User Id=${identityClientId};Encrypt=True;TrustServerCertificate=False;'

var registries = [
  {
    server: 'ghcr.io'
    username: ghcrUsername
    passwordSecretRef: 'ghcr-token'
  }
]

// Beide Werte kommen aus Key Vault, nicht als Klartext im Bicep-State — die
// Server-Identität braucht dafür die Rolle "Key Vault Secrets User" auf dem
// Vault, vergeben in modules/key-vault.bicep.
var secrets = [
  {
    name: 'ghcr-token'
    keyVaultUrl: ghcrTokenSecretUri
    identity: identityId
  }
  {
    name: 'jwt-signing-key'
    keyVaultUrl: jwtSigningKeySecretUri
    identity: identityId
  }
]

var appEnv = [
  {
    name: 'ConnectionStrings__zeltlotse'
    value: sqlConnectionString
  }
  {
    name: 'Zeltlotse__Token__Schluessel'
    secretRef: 'jwt-signing-key'
  }
  {
    name: 'Zeltlotse__ClientUrsprung'
    value: clientOrigin
  }
  {
    name: 'ASPNETCORE_ENVIRONMENT'
    value: 'Production'
  }
]

resource serverApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'zeltlotse-server'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identityId}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerAppsEnvId
    configuration: {
      registries: registries
      secrets: secrets
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        customDomains: bindCustomDomain ? [
          {
            name: 'api.${domainName}'
            certificateId: managedCertificate.id
            bindingType: 'SniEnabled'
          }
        ] : []
      }
    }
    template: {
      containers: [
        {
          name: 'server'
          image: serverImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: appEnv
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/alive'
                port: 8080
              }
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health'
                port: 8080
              }
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 3
      }
    }
  }
}

// Migrations laufen als eigener, manuell ausgelöster Job vor jedem Rollout —
// nicht mehr beim App-Start. Siehe design.md, Abschnitt
// "Migrations-Ausführung beim Deploy".
resource migrationJob 'Microsoft.App/jobs@2024-03-01' = {
  name: 'zeltlotse-migration'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identityId}': {}
    }
  }
  properties: {
    environmentId: containerAppsEnvId
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 600
      manualTriggerConfig: {
        parallelism: 1
        replicaCompletionCount: 1
      }
      registries: registries
      secrets: secrets
    }
    template: {
      containers: [
        {
          name: 'migrate'
          image: serverImage
          args: [
            '--migrate-only'
          ]
          env: appEnv
        }
      ]
    }
  }
}

resource managedCertificate 'Microsoft.App/managedEnvironments/managedCertificates@2024-03-01' = if (bindCustomDomain) {
  name: '${last(split(containerAppsEnvId, '/'))}/api-${replace(domainName, '.', '-')}'
  location: location
  properties: {
    subjectName: 'api.${domainName}'
    domainControlValidation: 'CNAME'
  }
}

output defaultHostname string = serverApp.properties.configuration.ingress.fqdn
