// ---------------------------------------------------------------------------
// Copilot Session Tracker – full-stack infrastructure
// ---------------------------------------------------------------------------

@description('App Service name (also used as the default hostname)')
param appName string = 'copilot-tracker'

@description('Cosmos DB account name')
param cosmosAccountName string = 'cosmos-copilot-tracker'

@description('Azure region')
param location string = resourceGroup().location

@description('App Service Plan SKU')
param appServicePlanSku string = 'B1'

@description('Entra tenant ID for token validation')
param tenantId string

@description('Entra app registration client ID for the API')
param apiClientId string

@description('Cosmos DB database name')
param cosmosDatabaseName string = 'CopilotTracker'

// ---------------------------------------------------------------------------
// 1. User-Assigned Managed Identity
// ---------------------------------------------------------------------------
resource managedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-copilot-tracker'
  location: location
}

// ---------------------------------------------------------------------------
// 2. Cosmos DB Account (serverless, RBAC-only)
// ---------------------------------------------------------------------------
resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' = {
  name: cosmosAccountName
  location: location
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    disableLocalAuth: true
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    capabilities: [
      { name: 'EnableServerless' }
    ]
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
  }
}

// ---------------------------------------------------------------------------
// 3. Cosmos DB SQL Database
// ---------------------------------------------------------------------------
resource cosmosDatabase 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-05-15' = {
  parent: cosmosAccount
  name: cosmosDatabaseName
  properties: {
    resource: {
      id: cosmosDatabaseName
    }
  }
}

// ---------------------------------------------------------------------------
// 4. Cosmos DB Containers
// ---------------------------------------------------------------------------
resource sessionContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: cosmosDatabase
  name: 'sessions'
  properties: {
    resource: {
      id: 'sessions'
      partitionKey: {
        paths: ['/machineId']
        kind: 'Hash'
      }
      indexingPolicy: {
        compositeIndexes: [
          [
            { path: '/status', order: 'ascending' }
            { path: '/lastHeartbeat', order: 'ascending' }
          ]
        ]
      }
    }
  }
}

resource taskContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: cosmosDatabase
  name: 'tasks'
  properties: {
    resource: {
      id: 'tasks'
      partitionKey: {
        paths: ['/queueName']
        kind: 'Hash'
      }
      indexingPolicy: {
        compositeIndexes: [
          [
            { path: '/status', order: 'ascending' }
            { path: '/createdAt', order: 'descending' }
          ]
        ]
      }
    }
  }
}

resource taskLogContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: cosmosDatabase
  name: 'taskLogs'
  properties: {
    resource: {
      id: 'taskLogs'
      partitionKey: {
        paths: ['/taskId']
        kind: 'Hash'
      }
    }
  }
}

// ---------------------------------------------------------------------------
// 5. Cosmos DB RBAC – Data Contributor role for the UAMI
// ---------------------------------------------------------------------------
resource cosmosRoleAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = {
  parent: cosmosAccount
  name: guid(cosmosAccount.id, managedIdentity.id, 'cosmos-data-contributor')
  properties: {
    roleDefinitionId: '${cosmosAccount.id}/sqlRoleDefinitions/00000000-0000-0000-0000-000000000002'
    principalId: managedIdentity.properties.principalId
    scope: cosmosAccount.id
  }
}

// ---------------------------------------------------------------------------
// 6. App Service Plan (Linux)
// ---------------------------------------------------------------------------
resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: 'plan-${appName}'
  location: location
  kind: 'linux'
  properties: {
    reserved: true
  }
  sku: {
    name: appServicePlanSku
  }
}

// ---------------------------------------------------------------------------
// 7. App Service (.NET 10, UAMI, Always On)
// ---------------------------------------------------------------------------
resource appService 'Microsoft.Web/sites@2023-12-01' = {
  name: appName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${managedIdentity.id}': {}
    }
  }
  properties: {
    serverFarmId: appServicePlan.id
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      appSettings: [
        { name: 'Cosmos__Endpoint', value: cosmosAccount.properties.documentEndpoint }
        { name: 'Cosmos__Database', value: cosmosDatabaseName }
        { name: 'Cosmos__ManagedIdentityClientId', value: managedIdentity.properties.clientId }
        { name: 'AzureAd__Instance', value: '${environment().authentication.loginEndpoint}/' }
        { name: 'AzureAd__TenantId', value: tenantId }
        { name: 'AzureAd__ClientId', value: apiClientId }
        { name: 'AzureAd__Audience', value: 'api://${apiClientId}' }
      ]
    }
    httpsOnly: true
  }
}

// ---------------------------------------------------------------------------
// Outputs
// ---------------------------------------------------------------------------
output appServiceName string = appService.name
output appServiceUrl string = 'https://${appService.properties.defaultHostName}'
output cosmosEndpoint string = cosmosAccount.properties.documentEndpoint
output managedIdentityClientId string = managedIdentity.properties.clientId
