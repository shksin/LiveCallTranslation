param acsName string
param acsSystemTopicName string
param aiSpeechName string
param principalId string
param principalType ('User' | 'ServicePrincipal')

// ACS

resource acs 'Microsoft.Communication/communicationServices@2023-06-01-preview' existing = {
  name: acsName
}

resource acsOwnerRole 'Microsoft.Authorization/roleDefinitions@2022-05-01-preview' existing = {
  scope: subscription()
  // Communication and Email Service Owner
  name: '09976791-48a7-449e-bb21-39d1a415f350'
}

resource acsOwnerRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acs.id, acsOwnerRole.id, principalId)
  scope: acs
  properties: {
    roleDefinitionId: acsOwnerRole.id
    principalId: principalId
    principalType: principalType
  }
}

// Event Grid

resource eventGridSystemTopic 'Microsoft.EventGrid/systemTopics@2024-06-01-preview' existing = {
  name: acsSystemTopicName
}

resource eventGridContributorRole 'Microsoft.Authorization/roleDefinitions@2022-05-01-preview' existing = {
  scope: subscription()
  // EventGrid Contributor
  name: '1e241071-0855-49ea-94dc-649edcd759de'
}

resource eventGridContributorRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(eventGridSystemTopic.id, eventGridContributorRole.id, principalId)
  scope: eventGridSystemTopic
  properties: {
    roleDefinitionId: eventGridContributorRole.id
    principalId: principalId
    principalType: principalType
  }
}

// AI Speech

resource aiSpeech 'Microsoft.CognitiveServices/accounts@2024-10-01' existing = {
  name: aiSpeechName
}

resource aiSpeechUserRole 'Microsoft.Authorization/roleDefinitions@2022-05-01-preview' existing = {
  scope: subscription()
  // Cognitive Services Speech User
  name: 'f2dc8367-1007-4938-bd23-fe263f013447'
}

resource aiSpeechRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(aiSpeech.id, aiSpeechUserRole.id, principalId)
  scope: aiSpeech
  properties: {
    roleDefinitionId: aiSpeechUserRole.id
    principalId: principalId
    principalType: principalType
  }
}
