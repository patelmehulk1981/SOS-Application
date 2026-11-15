// Private Blob storage for SOS and profile photos. No account keys (Entra ID only; the API signs read URLs
// with user-delegation SAS through its managed identity), no public access, TLS 1.2+.

param location string
param name string
param tags object
param identityPrincipalId string
param workspaceId string

var storageBlobDataContributor = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'

resource account 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: name
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: { name: 'Standard_ZRS' }
  properties: {
    accessTier: 'Hot'
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    defaultToOAuthAuthentication: true
    publicNetworkAccess: 'Enabled'
    encryption: {
      keySource: 'Microsoft.Storage'
      requireInfrastructureEncryption: true
      services: {
        blob: { enabled: true, keyType: 'Account' }
      }
    }
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: account
  name: 'default'
  properties: {
    // Short soft delete: recovers from mistakes without defeating the 90-day purge.
    deleteRetentionPolicy: { enabled: true, days: 7 }
    containerDeleteRetentionPolicy: { enabled: true, days: 7 }
  }
}

resource media 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'sos-media'
  properties: { publicAccess: 'None' }
}

// Backstop for the RetentionPurgeJob: an SOS closes within ~48 h of being raised (expiry), so any SOS photo
// older than 120 days is past the 90-days-after-closure retention even if the job never ran.
resource lifecycle 'Microsoft.Storage/storageAccounts/managementPolicies@2023-05-01' = {
  parent: account
  name: 'default'
  properties: {
    policy: {
      rules: [
        {
          name: 'sos-photos-backstop'
          enabled: true
          type: 'Lifecycle'
          definition: {
            filters: {
              blobTypes: ['blockBlob']
              prefixMatch: ['sos-media/sos/']
            }
            actions: {
              baseBlob: { delete: { daysAfterCreationGreaterThan: 120 } }
            }
          }
        }
      ]
    }
  }
}

resource apiCanUseBlobs 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: account
  name: guid(account.id, identityPrincipalId, storageBlobDataContributor)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContributor)
    principalId: identityPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource diagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  scope: blobService
  name: 'to-log-analytics'
  properties: {
    workspaceId: workspaceId
    logs: [
      { category: 'StorageDelete', enabled: true }
      { category: 'StorageWrite', enabled: true }
    ]
  }
}

output name string = account.name
output blobEndpoint string = account.properties.primaryEndpoints.blob
