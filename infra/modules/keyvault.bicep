@description('Azure region for Key Vault.')
param location string

@description('Globally unique Key Vault name.')
@minLength(3)
@maxLength(24)
param vaultName string

@description('Name of the Foundry account whose key is stored.')
param foundryAccountName string

@description('Tags applied to Key Vault.')
param tags object

var apiKeySecretName = 'ContentUnderstandingApiKey'

resource foundryAccount 'Microsoft.CognitiveServices/accounts@2025-06-01' existing = {
  name: foundryAccountName
}

resource vault 'Microsoft.KeyVault/vaults@2025-05-01' = {
  name: vaultName
  location: location
  tags: tags
  properties: {
    tenantId: tenant().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true
    publicNetworkAccess: 'Enabled'
  }
}

resource foundryApiKey 'Microsoft.KeyVault/vaults/secrets@2025-05-01' = {
  parent: vault
  name: apiKeySecretName
  tags: tags
  properties: {
    contentType: 'text/plain'
    value: foundryAccount.listKeys().key1
  }
}

output vaultId string = vault.id
output vaultName string = vault.name
output vaultUri string = vault.properties.vaultUri
output apiKeySecretName string = foundryApiKey.name
output apiKeySecretUri string = uri(vault.properties.vaultUri, 'secrets/${foundryApiKey.name}')
