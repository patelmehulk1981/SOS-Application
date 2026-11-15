// Community SOS: one environment (staging or prod) in one resource group. See docs/07-testing-deployment.md.
//
//   az group create -n rg-communitysos-prod -l centralindia
//   az deployment group create -g rg-communitysos-prod -f infra/bicep/main.bicep -p infra/bicep/main.prod.bicepparam
//
// Personal data (SQL, Blob, logs) stays in India: everything is deployed to `location` (Central India) except
// the static invite landing page, which holds no personal data (Static Web Apps is not offered in India).

targetScope = 'resourceGroup'

@allowed(['staging', 'prod'])
param environmentName string

@description('Region for every resource holding personal data.')
param location string = 'centralindia'

@description('Short name used in resource names (lowercase letters and digits).')
@minLength(3)
@maxLength(12)
param appName string = 'communitysos'

@description('Entra ID group that administers Azure SQL (Entra-only authentication). Put the deploy identity (GitHub OIDC) in it.')
param sqlAdminGroupName string

@description('Object ID of that Entra ID group.')
param sqlAdminGroupObjectId string

@description('App Service plan SKU. P0v3 or above for deployment slots and zone redundancy.')
param appServiceSku string = environmentName == 'prod' ? 'P0v3' : 'B1'

@description('Azure SQL database SKU. Provisioned (never serverless auto-pause: an SOS cannot wait for a cold start).')
param sqlSku object = environmentName == 'prod'
  ? { name: 'GP_Gen5_2', tier: 'GeneralPurpose' }
  : { name: 'S1', tier: 'Standard' }

@description('Firebase project id (phone auth + FCM).')
param firebaseProjectId string

@description('https://host that serves /i/{code} invite links and /.well-known files (must match the app build).')
param inviteLinkBaseUrl string

@description('Shown in the app (DPDP grievance officer).')
param grievanceContactEmail string

@description('Who receives operational alerts.')
param alertEmail string

@description('Region for the invite landing page (Static Web Apps regions only).')
param staticSiteLocation string = 'eastasia'

param tags object = {
  app: appName
  environment: environmentName
}

var suffix = '${appName}-${environmentName}'
var compact = toLower('${appName}${environmentName}${take(uniqueString(resourceGroup().id), 5)}')

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${suffix}'
  location: location
  tags: tags
}

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    location: location
    suffix: suffix
    tags: tags
    alertEmail: alertEmail
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    location: location
    name: take(compact, 24)
    tags: tags
    identityPrincipalId: identity.properties.principalId
    workspaceId: monitoring.outputs.workspaceId
  }
}

module sql 'modules/sql.bicep' = {
  name: 'sql'
  params: {
    location: location
    suffix: suffix
    tags: tags
    adminGroupName: sqlAdminGroupName
    adminGroupObjectId: sqlAdminGroupObjectId
    sku: sqlSku
    zoneRedundant: environmentName == 'prod'
    workspaceId: monitoring.outputs.workspaceId
  }
}

module keyVault 'modules/keyvault.bicep' = {
  name: 'keyvault'
  params: {
    location: location
    name: take('kv-${compact}', 24)
    tags: tags
    identityPrincipalId: identity.properties.principalId
    workspaceId: monitoring.outputs.workspaceId
  }
}

module app 'modules/app.bicep' = {
  name: 'app'
  params: {
    location: location
    suffix: suffix
    tags: tags
    sku: appServiceSku
    enableSlot: environmentName == 'prod'
    identityId: identity.id
    identityClientId: identity.properties.clientId
    sqlServerFqdn: sql.outputs.serverFqdn
    sqlDatabaseName: sql.outputs.databaseName
    blobServiceUri: storage.outputs.blobEndpoint
    keyVaultName: keyVault.outputs.name
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    workspaceId: monitoring.outputs.workspaceId
    firebaseProjectId: firebaseProjectId
    inviteLinkBaseUrl: inviteLinkBaseUrl
    grievanceContactEmail: grievanceContactEmail
  }
}

module alerts 'modules/alerts.bicep' = {
  name: 'alerts'
  params: {
    location: location
    suffix: suffix
    tags: tags
    siteId: app.outputs.siteId
    appInsightsId: monitoring.outputs.appInsightsId
    actionGroupId: monitoring.outputs.actionGroupId
  }
}

module inviteSite 'modules/invite-site.bicep' = {
  name: 'invite-site'
  params: {
    location: staticSiteLocation
    name: 'stapp-${suffix}'
    tags: tags
  }
}

output apiHostName string = app.outputs.defaultHostName
output apiSiteName string = app.outputs.siteName
output sqlServerFqdn string = sql.outputs.serverFqdn
output sqlServerName string = sql.outputs.serverName
output sqlDatabaseName string = sql.outputs.databaseName
output keyVaultName string = keyVault.outputs.name
output storageAccountName string = storage.outputs.name
output appIdentityName string = identity.name
output appIdentityPrincipalId string = identity.properties.principalId
output inviteSiteName string = inviteSite.outputs.name
output inviteSiteHostName string = inviteSite.outputs.defaultHostName
