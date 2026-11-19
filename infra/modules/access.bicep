@description('Foundry account name receiving Content Understanding permissions.')
param foundryAccountName string

@description('Storage account containing the results container.')
param storageAccountName string

@description('Private results Blob container name.')
param resultsContainerName string

@description('RBAC-enabled Key Vault name.')
param keyVaultName string

@description('System-assigned Web App identity principal ID.')
param webAppPrincipalId string

@description('Content Understanding authentication mode selected for the Web App.')
@allowed([
  'ManagedIdentity'
  'ApiKey'
])
param contentUnderstandingAuthenticationMode string

@description('Optional Entra principal object ID used by step 15 to configure Content Understanding defaults.')
param bootstrapPrincipalId string = ''

var contentUnderstandingOwnerRoleId = '4b42bd01-da42-4c92-9b07-15ea5bd6a602'
var cognitiveServicesUserRoleId = 'a97b65f3-24c7-4388-baec-2e87135dc908'
var storageBlobDataContributorRoleId = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

resource foundryAccount 'Microsoft.CognitiveServices/accounts@2025-06-01' existing = {
  name: foundryAccountName
}

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageAccountName
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' existing = {
  parent: storageAccount
  name: 'default'
}

resource resultsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' existing = {
  parent: blobService
  name: resultsContainerName
}

resource keyVault 'Microsoft.KeyVault/vaults@2025-05-01' existing = {
  name: keyVaultName
}

resource contentUnderstandingOwner 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (contentUnderstandingAuthenticationMode == 'ManagedIdentity') {
  name: guid(foundryAccount.id, webAppPrincipalId, contentUnderstandingOwnerRoleId)
  scope: foundryAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', contentUnderstandingOwnerRoleId)
    principalId: webAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource blobDataContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resultsContainer.id, webAppPrincipalId, storageBlobDataContributorRoleId)
  scope: resultsContainer
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContributorRoleId)
    principalId: webAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource keyVaultSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (contentUnderstandingAuthenticationMode == 'ApiKey') {
  name: guid(keyVault.id, webAppPrincipalId, keyVaultSecretsUserRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: webAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource bootstrapCognitiveServicesUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(bootstrapPrincipalId)) {
  name: guid(foundryAccount.id, bootstrapPrincipalId, cognitiveServicesUserRoleId)
  scope: foundryAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesUserRoleId)
    principalId: bootstrapPrincipalId
  }
}
