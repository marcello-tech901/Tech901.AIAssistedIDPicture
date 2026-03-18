// ---------------------------------------------------------------------------
// Tech901 AI-Assisted ID Photo — Azure Cognitive Services Infrastructure
// ---------------------------------------------------------------------------
// Deploys two Microsoft.CognitiveServices/accounts resources:
//   1. Speech Service  (kind: SpeechServices) — text-to-speech prompts and
//      speech-to-text name capture during the kiosk flow.
//   2. Face API         (kind: Face)           — face landmark detection for
//      auto-cropping ID photos around the participant's face.
//
// Both resources use the CognitiveServices/accounts resource type. The `kind`
// property selects which Cognitive Service is provisioned. Common kind values
// include SpeechServices, Face, TextAnalytics, ComputerVision, and OpenAI.
//
// Usage:
//   az deployment group create \
//     --resource-group rg-tech901-idphoto-dev \
//     --template-file infra/main.bicep \
//     --parameters env=dev location=eastus
//
// After deployment, deploy.ps1 reads the outputs below and stores them in
// .NET User Secrets for local development.
// ---------------------------------------------------------------------------

@description('Environment name (dev, staging, prod)')
param env string = 'dev'

@description('Azure region for all resources')
param location string = 'eastus'

// SKU tiers for Cognitive Services:
//   F0 — Free tier. Suitable for development and testing. Rate-limited.
//   S0 — Standard (paid) tier. Higher throughput and SLA-backed.
// Choose F0 for dev/test environments and S0 for production kiosks.
@description('SKU for Speech Service (F0 = free, S0 = standard)')
param speechSku string = 'F0'

@description('SKU for Face API (F0 = free, S0 = standard)')
param faceSku string = 'F0'

// TODO DEMO-01: AI-102 — Azure resource provisioning. Both Speech and Face use Microsoft.CognitiveServices/accounts; the 'kind' property selects which service. F0 = free tier, S0 = paid.

// Speech Service — provides TTS prompts ("Please look at the camera") and
// STT name capture during the NameCapture kiosk state.
// The resource type is Microsoft.CognitiveServices/accounts with kind set
// to 'SpeechServices'. The API version 2024-10-01 is the latest stable.
resource speechService 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: 'speech-tech901-idphoto-${env}'
  location: location
  kind: 'SpeechServices'
  sku: {
    name: speechSku
  }
  properties: {
    // customSubDomainName is required for token-based auth and private
    // endpoints. It must be globally unique across all Azure tenants.
    // We include the env suffix to avoid collisions between environments.
    customSubDomainName: 'speech-tech901-idphoto-${env}'
    publicNetworkAccess: 'Enabled'
  }
}

// Face API — detects face landmarks (eye positions, nose tip, etc.) so the
// ImageProcessingService can crop the photo centered on the participant's face.
// Uses kind 'Face'. The Azure.AI.Vision.Face SDK (beta) connects to this.
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

// ---------------------------------------------------------------------------
// Log Analytics Workspace — centralized logging and metrics for all services.
// Receives diagnostic logs (request/response, audit, traces) and platform
// metrics (latency, request counts, error rates, throttling) from both
// Speech and Face services.
// ---------------------------------------------------------------------------
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-tech901-idphoto-${env}'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

// ---------------------------------------------------------------------------
// Diagnostic Settings — route logs and metrics to Log Analytics
// ---------------------------------------------------------------------------

// Speech Service diagnostics: allLogs + Audit categories, plus AllMetrics.
resource speechDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'speech-diagnostics'
  scope: speechService
  properties: {
    workspaceId: logAnalytics.id
    logs: [
      { categoryGroup: 'allLogs', enabled: true }
      { categoryGroup: 'Audit', enabled: true }
    ]
    metrics: [
      { category: 'AllMetrics', enabled: true }
    ]
  }
}

// Face API diagnostics: allLogs + Audit categories, plus AllMetrics.
resource faceDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'face-diagnostics'
  scope: faceApi
  properties: {
    workspaceId: logAnalytics.id
    logs: [
      { categoryGroup: 'allLogs', enabled: true }
      { categoryGroup: 'Audit', enabled: true }
    ]
    metrics: [
      { category: 'AllMetrics', enabled: true }
    ]
  }
}

// TODO DEMO-02: AI-102 — listKeys() retrieves subscription keys at deploy time. In production, prefer Managed Identity + RBAC over key-based auth.

// ---------------------------------------------------------------------------
// Outputs — consumed by infra/deploy.ps1 to populate .NET User Secrets
// (dev) or environment variables (kiosk). The downstream automation reads
// these with `az deployment group show --query properties.outputs`.
//
// listKeys() retrieves the subscription key at deployment time. In production
// you may prefer Managed Identity + RBAC instead of key-based auth.
// ---------------------------------------------------------------------------
output speechKey string = speechService.listKeys().key1
output speechRegion string = speechService.location
output faceKey string = faceApi.listKeys().key1
output faceEndpoint string = faceApi.properties.endpoint
output logAnalyticsWorkspaceId string = logAnalytics.id
