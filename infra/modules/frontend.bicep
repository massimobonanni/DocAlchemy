@description('Azure region for the App Service plan and Web App.')
param location string

@description('Linux App Service plan name.')
param appServicePlanName string

@description('Linux App Service plan SKU.')
@allowed([
  'B1'
  'B2'
  'B3'
  'P0v3'
  'P1v3'
  'P2v3'
])
param appServicePlanSkuName string

@description('Globally unique Web App name.')
param webAppName string

@description('Tags applied to the App Service plan and Web App.')
param tags object

@description('Application Insights connection string.')
@secure()
param applicationInsightsConnectionString string

@description('Content Understanding account endpoint.')
param contentUnderstandingEndpoint string

@description('Content Understanding authentication mode.')
@allowed([
  'ManagedIdentity'
  'ApiKey'
])
param contentUnderstandingAuthenticationMode string

@description('Blob service endpoint for analysis results.')
param storageBlobServiceEndpoint string

@description('Private Blob container for analysis results.')
param resultsContainerName string

@description('Versionless Key Vault secret URI for the Foundry API key.')
param keyVaultApiKeySecretUri string

@description('Confidence warning threshold.')
@minLength(1)
@maxLength(16)
param confidenceThreshold string

@description('Maximum upload size in bytes.')
@minValue(1)
@maxValue(200000000)
param maxDocumentBytes int

var isBasicSku = contains([
  'B1'
  'B2'
  'B3'
], appServicePlanSkuName)

resource appServicePlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: appServicePlanName
  location: location
  tags: tags
  kind: 'linux'
  sku: {
    name: appServicePlanSkuName
    tier: isBasicSku ? 'Basic' : 'PremiumV3'
    size: appServicePlanSkuName
    family: isBasicSku ? 'B' : 'Pv3'
    capacity: 1
  }
  properties: {
    reserved: true
  }
}

resource webApp 'Microsoft.Web/sites@2024-04-01' = {
  name: webAppName
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    clientAffinityEnabled: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      webSocketsEnabled: true
      http20Enabled: true
      minTlsVersion: '1.2'
      scmMinTlsVersion: '1.2'
      ftpsState: 'Disabled'
      use32BitWorkerProcess: false
    }
  }
}

resource appSettings 'Microsoft.Web/sites/config@2024-04-01' = {
  parent: webApp
  name: 'appsettings'
  properties: union({
    APPLICATIONINSIGHTS_CONNECTION_STRING: applicationInsightsConnectionString
    ContentUnderstanding__Endpoint: contentUnderstandingEndpoint
    ContentUnderstanding__ApiVersion: '2025-11-01'
    ContentUnderstanding__AuthenticationMode: contentUnderstandingAuthenticationMode
    Storage__BlobServiceEndpoint: storageBlobServiceEndpoint
    Storage__ContainerName: resultsContainerName
    Storage__AuthenticationMode: 'ManagedIdentity'
    Analysis__ConfidenceThreshold: confidenceThreshold
    Analysis__OperationTimeout: '00:10:00'
    Analysis__PollInterval: '00:00:02'
    Uploads__MaxDocumentBytes: string(maxDocumentBytes)
  }, contentUnderstandingAuthenticationMode == 'ApiKey'
    ? {
        ContentUnderstanding__ApiKey: '@Microsoft.KeyVault(SecretUri=${keyVaultApiKeySecretUri})'
      }
    : {})
}

output webAppId string = webApp.id
output webAppName string = webApp.name
output webAppUrl string = 'https://${webApp.properties.defaultHostName}'
output principalId string = webApp.identity.principalId
