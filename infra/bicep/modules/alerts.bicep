// Operational alerts for a life-safety service: errors, instance health, SOS waiting too long for review,
// and FCM delivery failures.

param location string
param suffix string
param tags object
param siteId string
param appInsightsId string
param actionGroupId string

resource serverErrors 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-${suffix}-http5xx'
  location: 'global'
  tags: tags
  properties: {
    description: 'API returned more than 10 server errors in 5 minutes.'
    severity: 1
    enabled: true
    scopes: [siteId]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'http5xx'
          criterionType: 'StaticThresholdCriterion'
          metricName: 'Http5xx'
          metricNamespace: 'Microsoft.Web/sites'
          operator: 'GreaterThan'
          threshold: 10
          timeAggregation: 'Total'
        }
      ]
    }
    actions: [{ actionGroupId: actionGroupId }]
  }
}

resource unhealthy 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-${suffix}-health'
  location: 'global'
  tags: tags
  properties: {
    description: 'API health check below 100% (an instance is failing /health/live).'
    severity: 1
    enabled: true
    scopes: [siteId]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'health'
          criterionType: 'StaticThresholdCriterion'
          metricName: 'HealthCheckStatus'
          metricNamespace: 'Microsoft.Web/sites'
          operator: 'LessThan'
          threshold: 100
          timeAggregation: 'Average'
        }
      ]
    }
    actions: [{ actionGroupId: actionGroupId }]
  }
}

// EscalateSosJob logs "SOS {SosId} escalated: not reviewed by {AdminId} in time".
resource escalations 'Microsoft.Insights/scheduledQueryRules@2023-12-01' = {
  name: 'alert-${suffix}-escalations'
  location: location
  tags: tags
  properties: {
    displayName: 'SOS escalated (not reviewed within 3 minutes)'
    description: 'Sub-Admins are not reviewing SOS in time. Check admin coverage.'
    severity: 2
    enabled: true
    scopes: [appInsightsId]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      allOf: [
        {
          query: 'traces | where message has "escalated: not reviewed"'
          timeAggregation: 'Count'
          operator: 'GreaterThanOrEqual'
          threshold: 3
          failingPeriods: { numberOfEvaluationPeriods: 1, minFailingPeriodsToAlert: 1 }
        }
      ]
    }
    actions: { actionGroups: [actionGroupId] }
  }
}

// Push failures (FcmPushSender / alert jobs log failed deliveries as warnings with "push" in the category).
resource pushFailures 'Microsoft.Insights/scheduledQueryRules@2023-12-01' = {
  name: 'alert-${suffix}-push-failures'
  location: location
  tags: tags
  properties: {
    displayName: 'Push delivery failures'
    description: 'Many FCM sends failed: check the Firebase service account, APNs key and FCM status.'
    severity: 2
    enabled: true
    scopes: [appInsightsId]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      allOf: [
        {
          query: 'exceptions | where type has "PushDeliveryException"'
          timeAggregation: 'Count'
          operator: 'GreaterThanOrEqual'
          threshold: 5
          failingPeriods: { numberOfEvaluationPeriods: 1, minFailingPeriodsToAlert: 1 }
        }
      ]
    }
    actions: { actionGroups: [actionGroupId] }
  }
}
