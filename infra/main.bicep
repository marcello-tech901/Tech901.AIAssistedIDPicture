@description('Environment name (dev, staging, prod)')
param env string = 'dev'

@description('Azure region for all resources')
param location string = 'eastus'

@description('SKU for Speech Service (F0 = free, S0 = standard)')
param speechSku string = 'F0'

@description('SKU for Face API (F0 = free, S0 = standard)')
param faceSku string = 'F0'

resource speechService 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: 'speech-tech901-idphoto-${env}'
  location: location
  kind: 'SpeechServices'
  sku: {
    name: speechSku
  }
  properties: {
    customSubDomainName: 'speech-tech901-idphoto-${env}'
    publicNetworkAccess: 'Enabled'
  }
}

resource faceApi 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: 'face-tech901-idphoto-${env}'
  location: location
  kind: 'Face'
  sku: {
    name: faceSku
  }
  properties: {
    customSubDomainName: 'face-tech901-idphoto-${env}'
    publicNetworkAccess: 'Enabled'
  }
}

output speechKey string = speechService.listKeys().key1
output speechRegion string = speechService.location
output faceKey string = faceApi.listKeys().key1
output faceEndpoint string = faceApi.properties.endpoint
