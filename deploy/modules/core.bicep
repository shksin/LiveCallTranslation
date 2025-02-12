param uniqueNameFormat string
param uniqueShortNameFormat string
param location string
param acsDataLocation string
param tags object

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: format(uniqueNameFormat, 'logs')
  location: location
  tags: tags
  properties: {
    retentionInDays: 30
    sku: {
      name: 'PerGB2018'
    }
  }
}

resource acs 'Microsoft.Communication/communicationServices@2023-06-01-preview' = {
  name: format(uniqueNameFormat, 'acs')
  location: 'global'
  tags: tags
  properties: {
    dataLocation: acsDataLocation
  }
}

resource acsLogs 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'allLogsToLogAnalytics'
  scope: acs
  properties: {
    workspaceId: logAnalytics.id
    logs: [
      {
        categoryGroup: 'allLogs'
        enabled: true
      }
    ]
  }
}

resource acsSystemTopic 'Microsoft.EventGrid/systemTopics@2024-06-01-preview' = {
  name: format(uniqueNameFormat, 'eg-acs')
  location: 'global'
  tags: tags
  properties: {
    source: acs.id
    topicType: 'Microsoft.Communication.CommunicationServices'
  }
  identity: {
    type: 'SystemAssigned'
  }
}

resource acsSystemTopicLogs 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'allLogsToLogAnalytics'
  scope: acsSystemTopic
  properties: {
    workspaceId: logAnalytics.id
    logs: [
      {
        categoryGroup: 'allLogs'
        enabled: true
      }
    ]
  }
}

resource aiSpeech 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: format(uniqueNameFormat, 'aispeech')
  location: location
  tags: tags
  kind: 'SpeechServices'
  sku: {
    name: 'S0'
  }
  properties: {
    customSubDomainName: format(uniqueShortNameFormat, 'aispeech')
    disableLocalAuth: true
  }
}

resource aiSpeechLogs 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'allLogsToLogAnalytics'
  scope: aiSpeech
  properties: {
    workspaceId: logAnalytics.id
    logs: [
      {
        categoryGroup: 'allLogs'
        enabled: true
      }
    ]
  }
}

output acsName string = acs.name
output acsSystemTopicName string = acsSystemTopic.name
output aiSpeechName string = aiSpeech.name
output logAnalyticsName string = logAnalytics.name

output aiSpeechId string = aiSpeech.id
output aiSpeechLocation string = aiSpeech.location
output acsEndpoint string = 'https://${acs.properties.hostName}/'
output acsSystemTopicId string = acsSystemTopic.id
