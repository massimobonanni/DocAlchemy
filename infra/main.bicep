targetScope = 'subscription'

@description('AZD environment name used for resource naming and tags.')
@minLength(1)
@maxLength(64)
param environmentName string

@description('Azure region for the resource group, monitoring, and storage.')
@minLength(1)
param location string

@description('Private Blob container for normalized analysis results.')
@minLength(3)
@maxLength(63)
param resultsContainerName string = 'analysis-results'

var abbreviations = loadJsonContent('abbreviations.json')
var lowerEnvironmentName = toLower(environmentName)
var environmentCharacters = map(range(0, length(lowerEnvironmentName)), i => substring(lowerEnvironmentName, i, 1))
var safeEnvironmentName = join(filter(environmentCharacters, character => contains('abcdefghijklmnopqrstuvwxyz0123456789', character)), '')
var uniquenessToken = uniqueString(subscription().id, environmentName)
var namePart = take(safeEnvironmentName, 32)
var storageNamePart = take(safeEnvironmentName, 9)
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

output AZURE_RESOURCE_GROUP_NAME string = resourceGroup.name
output LOG_ANALYTICS_WORKSPACE_ID string = monitoring.outputs.workspaceId
output APPLICATION_INSIGHTS_ID string = monitoring.outputs.applicationInsightsId
output STORAGE_ACCOUNT_ID string = storage.outputs.storageAccountId
output STORAGE_ACCOUNT_NAME string = storage.outputs.storageAccountName
output STORAGE_BLOB_SERVICE_ENDPOINT string = storage.outputs.blobServiceEndpoint
output STORAGE_RESULTS_CONTAINER_ID string = storage.outputs.containerId
output STORAGE_RESULTS_CONTAINER_NAME string = storage.outputs.containerName
