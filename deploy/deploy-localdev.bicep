@description('Location for all resources.')
param location string = resourceGroup().location

param acsDataLocation string = 'Australia'

@description('A prefix to add to the start of all resource names. Note: A "unique" suffix will also be added')
param prefix string = 'demo'

@description('Tags to apply to all deployed resources')
param tags object = {}

@description('The object ID of the admin user, you can find this by going to Entra in the Azure Portal, selecting your user, and copying the Object ID from the Overview tab.')
param adminObjectId string

var uniqueNameFormat = '${prefix}-{0}-${uniqueString(resourceGroup().id, prefix)}'
var uniqueShortNameFormat = toLower('${prefix}{0}${uniqueString(resourceGroup().id, prefix)}')

module core 'modules/core.bicep' = {
  name: '${deployment().name}-core'
  params: {
    location: location
    acsDataLocation: acsDataLocation
    uniqueNameFormat: uniqueNameFormat
    uniqueShortNameFormat: uniqueShortNameFormat
    tags: tags
  }
}

module rbac 'modules/rbac.bicep' = if (!empty(trim(adminObjectId))) {
  name: '${deployment().name}-rbac'
  params: {
    acsName: core.outputs.acsName
    acsSystemTopicName: core.outputs.acsSystemTopicName
    aiSpeechName: core.outputs.aiSpeechName
    principalId: adminObjectId
    principalType: 'User'
  }
}

output appsettings object = {
  ConnectionStrings: {
    SQLDB: ''
  }
  AzureTenantId: subscription().tenantId
  Translator: 'AzureAISpeech'
  AzureAISpeech: {
    '//Comment': 'Only required when \'Translator\' is set to \'AzureAISpeech\''
    ResourceID: core.outputs.aiSpeechId
    Region: core.outputs.aiSpeechLocation
  }
  AzureOpenAI: {
    '//Comment': 'Only required when \'Translator\' is set to \'AzureOpenAI\''
    Endpoint: ''
    Deployment: ''
  }
  Inbound: {
    Hostname: '<fill this in from ngrok, just the hostname part, no need for the protocol>'
  }
  ACS: {
    Endpoint: core.outputs.acsEndpoint
    InboundNumber: '*'
  }
  EventGrid: {
    '//Comment': 'Only required when you want the application to auto-configure the EventGrid subscription on startup'
    TopicResourceID: core.outputs.acsSystemTopicId
  }
}
