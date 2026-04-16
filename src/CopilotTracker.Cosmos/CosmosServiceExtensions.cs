namespace CopilotTracker.Cosmos;

using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using CopilotTracker.Core.Interfaces;

public static class CosmosServiceExtensions
{
    public static IServiceCollection AddCosmosRepositories(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(sp =>
        {
            var endpoint = configuration["Cosmos:Endpoint"]
                ?? throw new InvalidOperationException("Cosmos:Endpoint not configured");
            var databaseName = configuration["Cosmos:Database"] ?? "CopilotTracker";

            var clientOptions = new CosmosClientOptions
            {
                SerializerOptions = new CosmosSerializationOptions
                {
                    PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
                }
            };

            var managedIdentityClientId = configuration["Cosmos:ManagedIdentityClientId"];
            var credential = string.IsNullOrEmpty(managedIdentityClientId)
                ? new DefaultAzureCredential()
                : new DefaultAzureCredential(new DefaultAzureCredentialOptions
                {
                    ManagedIdentityClientId = managedIdentityClientId
                });

            var client = new CosmosClient(endpoint, credential, clientOptions);
            return client.GetDatabase(databaseName);
        });

        services.AddSingleton<ISessionRepository, CosmosSessionRepository>();
        services.AddSingleton<ITaskRepository, CosmosTaskRepository>();
        services.AddSingleton<ITaskLogRepository, CosmosTaskLogRepository>();
        services.AddSingleton<IPromptRepository, CosmosPromptRepository>();
        services.AddSingleton<IPromptLogRepository, CosmosPromptLogRepository>();

        return services;
    }
}
