using 'main.bicep'

// Replace every REPLACE_ME before the first deployment (docs/07-testing-deployment.md, "First deployment").
param environmentName = 'prod'
param sqlAdminGroupName = 'sg-communitysos-sql-admins'
param sqlAdminGroupObjectId = '00000000-0000-0000-0000-000000000000' // REPLACE_ME: Entra group object id
param firebaseProjectId = 'REPLACE_ME'
param inviteLinkBaseUrl = 'https://sos.example.com' // REPLACE_ME: must equal INVITE_LINK_HOST of the store build
param grievanceContactEmail = 'grievance@example.com' // REPLACE_ME
param alertEmail = 'ops@example.com' // REPLACE_ME
