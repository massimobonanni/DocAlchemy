targetScope = 'subscription'

@description('AZD environment name used for resource naming and tags.')
@minLength(1)
@maxLength(64)
param environmentName string

@description('Azure region for the resource group, monitoring, and storage.')
@minLength(1)
param location string

@description('Azure region for the Foundry account. Defaults to the primary region.')
@minLength(1)
param foundryLocation string = location

@description('Private Blob container for normalized analysis results.')
@minLength(3)
@maxLength(63)
param resultsContainerName string = 'analysis-results'

@description('SKU for the Foundry multi-service account.')
param foundrySkuName string = 'S0'

@description('GPT-5.2 deployment name used by the defaults bootstrap.')
@minLength(1)
@maxLength(64)
param completionDeploymentName string = 'gpt-52'

@description('GPT-5.2 model version.')
param completionModelVersion string = '2025-12-11'

@description('GPT-5.2 deployment SKU.')
param completionDeploymentSkuName string = 'GlobalStandard'

@description('GPT-5.2 deployment capacity.')
@minValue(1)
param completionDeploymentCapacity int = 1

@description('Text Embedding 3 Large deployment name used by the defaults bootstrap.')
@minLength(1)
@maxLength(64)
param embeddingDeploymentName string = 'text-embedding-3-large'

@description('Text Embedding 3 Large model version.')
param embeddingModelVersion string = '1'

@description('Text Embedding 3 Large deployment SKU.')
param embeddingDeploymentSkuName string = 'GlobalStandard'

@description('Text Embedding 3 Large deployment capacity.')
@minValue(1)
param embeddingDeploymentCapacity int = 1

var abbreviations = loadJsonContent('abbreviations.json')
var lowerEnvironmentName = toLower(environmentName)
var environmentCharacters = map(range(0, length(lowerEnvironmentName)), i => substring(lowerEnvironmentName, i, 1))
var safeEnvironmentName = join(filter(environmentCharacters, character => contains('abcdefghijklmnopqrstuvwxyz0123456789', character)), '')
var uniquenessToken = uniqueString(subscription().id, environmentName)
var namePart = take(safeEnvironmentName, 32)
var storageNamePart = take(safeEnvironmentName, 9)
var keyVaultNamePart = take(safeEnvironmentName, 8)
var tags = {
  'azd-env-name': environmentName
  project: 'DocAlchemy'
}

resource resourceGroup 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: '${abbreviations.resourcesResourceGroups}${namePart}-${uniquenessToken}'
  location: location
  tags: tags
}

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  scope: resourceGroup
  params: {
    location: location
    workspaceName: '${abbreviations.operationalInsightsWorkspaces}${namePart}-${uniquenessToken}'
    applicationInsightsName: '${abbreviations.insightsComponents}${namePart}-${uniquenessToken}'
    tags: tags
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  scope: resourceGroup
  params: {
    location: location
    storageAccountName: '${abbreviations.storageStorageAccounts}${storageNamePart}${uniquenessToken}'
    containerName: resultsContainerName
    tags: tags
  }
}

module foundry 'modules/foundry.bicep' = {
  name: 'foundry'
  scope: resourceGroup
  params: {
    location: foundryLocation
    accountName: '${abbreviations.aiFoundryAccounts}${namePart}${uniquenessToken}'
    skuName: foundrySkuName
    completionDeploymentName: completionDeploymentName
    completionModelVersion: completionModelVersion
    completionDeploymentSkuName: completionDeploymentSkuName
    completionDeploymentCapacity: completionDeploymentCapacity
    embeddingDeploymentName: embeddingDeploymentName
    embeddingModelVersion: embeddingModelVersion
    embeddingDeploymentSkuName: embeddingDeploymentSkuName
    embeddingDeploymentCapacity: embeddingDeploymentCapacity
    tags: tags
  }
}

module keyVault 'modules/keyvault.bicep' = {
  name: 'keyvault'
  scope: resourceGroup
  params: {
    location: location
    vaultName: '${abbreviations.keyVaultVaults}${keyVaultNamePart}${uniquenessToken}'
    foundryAccountName: foundry.outputs.accountName
    tags: tags
  }
}

output AZURE_RESOURCE_GROUP_NAME string = resourceGroup.name
output LOG_ANALYTICS_WORKSPACE_ID string = monitoring.outputs.workspaceId
output APPLICATION_INSIGHTS_ID string = monitoring.outputs.applicationInsightsId
output STORAGE_ACCOUNT_ID string = storage.outputs.storageAccountId
output STORAGE_ACCOUNT_NAME string = storage.outputs.storageAccountName
output STORAGE_BLOB_SERVICE_ENDPOINT string = storage.outputs.blobServiceEndpoint
output STORAGE_RESULTS_CONTAINER_ID string = storage.outputs.containerId
output STORAGE_RESULTS_CONTAINER_NAME string = storage.outputs.containerName
output CONTENT_UNDERSTANDING_ACCOUNT_ID string = foundry.outputs.accountId
output CONTENT_UNDERSTANDING_ACCOUNT_NAME string = foundry.outputs.accountName
output CONTENT_UNDERSTANDING_ENDPOINT string = foundry.outputs.endpoint
output COMPLETION_MODEL_NAME string = foundry.outputs.completionModelName
output COMPLETION_MODEL_VERSION string = foundry.outputs.completionModelVersion
output COMPLETION_DEPLOYMENT_NAME string = foundry.outputs.completionDeploymentName
output COMPLETION_DEPLOYMENT_CAPACITY int = foundry.outputs.completionDeploymentCapacity
output EMBEDDING_MODEL_NAME string = foundry.outputs.embeddingModelName
output EMBEDDING_MODEL_VERSION string = foundry.outputs.embeddingModelVersion
output EMBEDDING_DEPLOYMENT_NAME string = foundry.outputs.embeddingDeploymentName
output EMBEDDING_DEPLOYMENT_CAPACITY int = foundry.outputs.embeddingDeploymentCapacity
output KEY_VAULT_ID string = keyVault.outputs.vaultId
output KEY_VAULT_NAME string = keyVault.outputs.vaultName
output KEY_VAULT_URI string = keyVault.outputs.vaultUri
output CONTENT_UNDERSTANDING_API_KEY_SECRET_NAME string = keyVault.outputs.apiKeySecretName
output CONTENT_UNDERSTANDING_API_KEY_SECRET_URI string = keyVault.outputs.apiKeySecretUri
