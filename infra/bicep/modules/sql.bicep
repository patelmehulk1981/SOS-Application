// Azure SQL with Entra ID-only authentication (no SQL logins or passwords anywhere), TDE (default),
// auditing to Log Analytics, geo-redundant backups (paired region South India: still in India).

param location string
param suffix string
param tags object
param adminGroupName string
param adminGroupObjectId string
param sku object
param zoneRedundant bool
param workspaceId string

resource server 'Microsoft.Sql/servers@2021-11-01' = {
  name: 'sql-${suffix}'
  location: location
  tags: tags
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: adminGroupName
      sid: adminGroupObjectId
      tenantId: subscription().tenantId
      principalType: 'Group'
    }
  }
}

// App Service reaches SQL over the Azure backbone. The deploy pipeline adds (and removes) its own runner IP.
// Hardening option: private endpoint + App Service VNet integration, then publicNetworkAccess: 'Disabled'.
resource allowAzure 'Microsoft.Sql/servers/firewallRules@2021-11-01' = {
  parent: server
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource database 'Microsoft.Sql/servers/databases@2021-11-01' = {
  parent: server
  name: 'sqldb-${suffix}'
  location: location
  tags: tags
  sku: sku
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    zoneRedundant: zoneRedundant
    requestedBackupStorageRedundancy: 'Geo'
  }
}

resource shortTermBackup 'Microsoft.Sql/servers/databases/backupShortTermRetentionPolicies@2021-11-01' = {
  parent: database
  name: 'default'
  properties: { retentionDays: 14 }
}

resource auditing 'Microsoft.Sql/servers/auditingSettings@2021-11-01' = {
  parent: server
  name: 'default'
  properties: {
    state: 'Enabled'
    isAzureMonitorTargetEnabled: true
    auditActionsAndGroups: [
      'SUCCESSFUL_DATABASE_AUTHENTICATION_GROUP'
      'FAILED_DATABASE_AUTHENTICATION_GROUP'
      'DATABASE_PERMISSION_CHANGE_GROUP'
      'DATABASE_PRINCIPAL_CHANGE_GROUP'
      'SCHEMA_OBJECT_CHANGE_GROUP'
    ]
  }
}

// Server audit events flow through the master database's diagnostic setting.
resource master 'Microsoft.Sql/servers/databases@2021-11-01' existing = {
  parent: server
  name: 'master'
}

resource auditDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  scope: master
  name: 'sql-audit-to-log-analytics'
  properties: {
    workspaceId: workspaceId
    logs: [{ category: 'SQLSecurityAuditEvents', enabled: true }]
  }
  dependsOn: [auditing]
}

output serverName string = server.name
output serverFqdn string = server.properties.fullyQualifiedDomainName
output databaseName string = database.name
