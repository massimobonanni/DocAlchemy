@description('Azure region for the Foundry account.')
param location string

@description('Globally unique Foundry account name.')
@minLength(2)
@maxLength(64)
param accountName string

@description('Foundry multi-service account SKU.')
param skuName string

@description('GPT-5.2 deployment name.')
@minLength(1)
@maxLength(64)
param completionDeploymentName string

@description('GPT-5.2 model version.')
param completionModelVersion string

@description('GPT-5.2 deployment SKU.')
param completionDeploymentSkuName string

@description('GPT-5.2 deployment capacity.')
@minValue(1)
param completionDeploymentCapacity int

@description('Text Embedding 3 Large deployment name.')
@minLength(1)
@maxLength(64)
param embeddingDeploymentName string

@description('Text Embedding 3 Large model version.')
param embeddingModelVersion string

@description('Text Embedding 3 Large deployment SKU.')
param embeddingDeploymentSkuName string

@description('Text Embedding 3 Large deployment capacity.')
@minValue(1)
param embeddingDeploymentCapacity int

@description('Tags applied to the Foundry account.')
param tags object

resource foundryAccount 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  name: accountName
  location: location
  kind: 'AIServices'
  sku: {
    name: skuName
  }
  tags: tags
  properties: {
    customSubDomainName: accountName
    disableLocalAuth: false
    publicNetworkAccess: 'Enabled'
  }
}

resource completionDeployment 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = {
  parent: foundryAccount
  name: completionDeploymentName
  sku: {
    name: completionDeploymentSkuName
    capacity: completionDeploymentCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-5.2'
      version: completionModelVersion
    }
    versionUpgradeOption: 'NoAutoUpgrade'
  }
}

resource embeddingDeployment 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = {
  parent: foundryAccount
  name: embeddingDeploymentName
  sku: {
    name: embeddingDeploymentSkuName
    capacity: embeddingDeploymentCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'text-embedding-3-large'
      version: embeddingModelVersion
    }
    versionUpgradeOption: 'NoAutoUpgrade'
  }
}

output accountId string = foundryAccount.id
output accountName string = foundryAccount.name
output endpoint string = foundryAccount.properties.endpoint
output completionModelName string = 'gpt-5.2'
output completionModelVersion string = completionModelVersion
output completionDeploymentName string = completionDeployment.name
output completionDeploymentCapacity int = completionDeploymentCapacity
output embeddingModelName string = 'text-embedding-3-large'
output embeddingModelVersion string = embeddingModelVersion
output embeddingDeploymentName string = embeddingDeployment.name
output embeddingDeploymentCapacity int = embeddingDeploymentCapacity
