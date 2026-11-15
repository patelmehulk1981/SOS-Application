// Linux App Service running the API and the Hangfire worker (same process; Jobs:RunServer=true).
// Prod has a "staging" slot: deploy there, smoke-test, then swap (no downtime). The slot never runs jobs.

param location string
param suffix string
param tags object
param sku string
param enableSlot bool
param identityId string
param identityClientId string
param sqlServerFqdn string
param sqlDatabaseName string
param blobServiceUri string
param keyVaultName string
param appInsightsConnectionString string
param workspaceId string
param firebaseProjectId string
param inviteLinkBaseUrl string
param grievanceContactEmail string

var kvRef = 'https://${keyVaultName}${environment().suffixes.keyvaultDns}/secrets'

var settings = [
  { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
  { name: 'WEBSITE_RUN_FROM_PACKAGE', value: '1' }
  { name: 'AZURE_CLIENT_ID', value: identityClientId } // DefaultAzureCredential (Blob) picks the user-assigned identity
  { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
  {
    name: 'ConnectionStrings__Sql'
    value: 'Server=tcp:${sqlServerFqdn},1433;Database=${sqlDatabaseName};Authentication=Active Directory Managed Identity;User Id=${identityClientId};Encrypt=True;Connect Timeout=30;'
  }
  { name: 'BlobStorage__ServiceUri', value: blobServiceUri }
  { name: 'Jwt__SigningKeyPem', value: '@Microsoft.KeyVault(SecretUri=${kvRef}/jwt-signing-key)' }
  { name: 'Firebase__ProjectId', value: firebaseProjectId }
  { name: 'Firebase__ServiceAccountJson', value: '@Microsoft.KeyVault(SecretUri=${kvRef}/firebase-service-account)' }
  { name: 'Firebase__UseDevelopmentVerifier', value: 'false' }
  { name: 'GoogleMaps__ApiKey', value: '@Microsoft.KeyVault(SecretUri=${kvRef}/google-maps-server-key)' }
  { name: 'App__InviteLinkBaseUrl', value: inviteLinkBaseUrl }
  { name: 'App__GrievanceContactEmail', value: grievanceContactEmail }
  { name: 'Jobs__Storage', value: 'SqlServer' }
  { name: 'Jobs__PrepareSchema', value: 'false' } // installed by the deploy step; the app has no DDL rights
  { name: 'ForwardedHeaders__TrustAllProxies', value: 'true' } // only reachable through the App Service front end
  { name: 'OpenApi__EnableUi', value: 'false' }
]

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: 'asp-${suffix}'
  location: location
  tags: tags
  kind: 'linux'
  sku: { name: sku }
  properties: {
    reserved: true
  }
}

var siteConfig = {
  linuxFxVersion: 'DOTNETCORE|10.0'
  alwaysOn: true // Hangfire timers (escalation after 3 min) must keep running
  http20Enabled: true
  minTlsVersion: '1.2'
  scmMinTlsVersion: '1.2'
  ftpsState: 'Disabled'
  healthCheckPath: '/health/live'
  use32BitWorkerProcess: false
}

resource site 'Microsoft.Web/sites@2023-12-01' = {
  name: 'app-${suffix}'
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identityId}': {} }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: false
    keyVaultReferenceIdentity: identityId
    siteConfig: union(siteConfig, {
      appSettings: concat(settings, [{ name: 'Jobs__RunServer', value: 'true' }])
    })
  }
}

// Basic auth for SCM/FTP off: deployments use Entra ID (GitHub OIDC).
resource scmBasicAuth 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2023-12-01' = {
  parent: site
  name: 'scm'
  properties: { allow: false }
}

resource ftpBasicAuth 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2023-12-01' = {
  parent: site
  name: 'ftp'
  properties: { allow: false }
}

resource slot 'Microsoft.Web/sites/slots@2023-12-01' = if (enableSlot) {
  parent: site
  name: 'staging'
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identityId}': {} }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: false
    keyVaultReferenceIdentity: identityId
    siteConfig: union(siteConfig, {
      // A warming slot must not run jobs against production data (Jobs__RunServer is slot-sticky below).
      appSettings: concat(settings, [{ name: 'Jobs__RunServer', value: 'false' }])
    })
  }
}

resource stickySettings 'Microsoft.Web/sites/config@2023-12-01' = if (enableSlot) {
  parent: site
  name: 'slotConfigNames'
  properties: {
    appSettingNames: ['Jobs__RunServer']
  }
}

resource diagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  scope: site
  name: 'to-log-analytics'
  properties: {
    workspaceId: workspaceId
    logs: [
      { category: 'AppServiceHTTPLogs', enabled: true }
      { category: 'AppServiceConsoleLogs', enabled: true }
      { category: 'AppServicePlatformLogs', enabled: true }
    ]
    metrics: [{ category: 'AllMetrics', enabled: true }]
  }
}

output siteId string = site.id
output siteName string = site.name
output defaultHostName string = site.properties.defaultHostName
