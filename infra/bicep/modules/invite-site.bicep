// Static landing page for invite links (web/invite-site): /i/{code} plus /.well-known/assetlinks.json and
// apple-app-site-association. Holds no personal data. Content is deployed by the pipeline.

param location string
param name string
param tags object

resource site 'Microsoft.Web/staticSites@2023-12-01' = {
  name: name
  location: location
  tags: tags
  sku: { name: 'Free', tier: 'Free' }
  properties: {
    allowConfigFileUpdates: true
    stagingEnvironmentPolicy: 'Disabled'
  }
}

output name string = site.name
output defaultHostName string = site.properties.defaultHostname
