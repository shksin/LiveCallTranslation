@description('Location for all resources.')
param location string = resourceGroup().location

param acsDataLocation string = 'Australia'

@description('A prefix to add to the start of all resource names. Note: A "unique" suffix will also be added')
param prefix string = 'demo'

@description('Tags to apply to all deployed resources')
param tags object = {}

@description('The URL to the deployment artifact to deploy, if left empty deployment will be skipped and you will need to deploy after the template is done')
param deploymentArtifactUrl string = ''

var databaseName = '${prefix}db'

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

module hosting 'modules/hosting.bicep' = {
  name: '${deployment().name}-hosting'
  params: {
    location: location
    uniqueNameFormat: uniqueNameFormat
    tags: tags

    databaseName: databaseName
    deploymentArtifactUrl: deploymentArtifactUrl

    acsName: core.outputs.acsName
    acsSystemTopicName: core.outputs.acsSystemTopicName
    aiSpeechName: core.outputs.aiSpeechName
    logAnalyticsName: core.outputs.logAnalyticsName
  }
}

module rbac 'modules/rbac.bicep' = {
  name: '${deployment().name}-rbac'
  params: {
    acsName: core.outputs.acsName
    acsSystemTopicName: core.outputs.acsSystemTopicName
    aiSpeechName: core.outputs.aiSpeechName
    principalId: hosting.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}
