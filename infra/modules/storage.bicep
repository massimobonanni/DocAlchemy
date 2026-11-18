@description('Azure region for the results storage account.')
param location string

@description('Globally unique lowercase alphanumeric storage account name.')
@minLength(3)
@maxLength(24)
param storageAccountName string

@description('Private Blob container name.')
@minLength(3)
@maxLength(63)
param containerName string

@description('Tags applied to the storage account.')
param tags object

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
}

resource container 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: containerName
  properties: {
    publicAccess: 'None'
  }
}

output storageAccountId string = storageAccount.id
output storageAccountName string = storageAccount.name
output blobServiceEndpoint string = storageAccount.properties.primaryEndpoints.blob
output containerId string = container.id
output containerName string = container.name
