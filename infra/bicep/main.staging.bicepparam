using 'main.bicep'

// Staging mirrors prod at a smaller size (B1 App Service, S1 database, no deployment slot).
param environmentName = 'staging'
param sqlAdminGroupName = 'sg-communitysos-sql-admins'
param sqlAdminGroupObjectId = '00000000-0000-0000-0000-000000000000' // REPLACE_ME: Entra group object id
param firebaseProjectId = 'REPLACE_ME' // a separate Firebase project for staging is recommended
param inviteLinkBaseUrl = 'https://staging.sos.example.com' // REPLACE_ME: must equal INVITE_LINK_HOST of the preview build
param grievanceContactEmail = 'grievance@example.com' // REPLACE_ME
param alertEmail = 'ops@example.com' // REPLACE_ME
