namespace CopilotTracker.Cosmos;

using Microsoft.Azure.Cosmos;
using CopilotTracker.Core.Interfaces;
using CopilotTracker.Core.Models;

public class CosmosPromptLogRepository : IPromptLogRepository
{
    private readonly Container _container;

    public CosmosPromptLogRepository(Database database)
    {
        _container = database.GetContainer("promptLogs");
    }

    public async Task<PromptLog> CreateAsync(PromptLog log)
    {
        var response = await _container.CreateItemAsync(log, new PartitionKey(log.PromptId));
        return response.Resource;
    }

    public async Task<IReadOnlyList<PromptLog>> GetByPromptAsync(string promptId)
    {
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.promptId = @promptId ORDER BY c.timestamp ASC")
            .WithParameter("@promptId", promptId);

        var options = new QueryRequestOptions { PartitionKey = new PartitionKey(promptId) };
        var results = new List<PromptLog>();

        using var iterator = _container.GetItemQueryIterator<PromptLog>(query, requestOptions: options);
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }

        return results;
    }

    public async Task<PagedResult<PromptLog>> GetByPromptPagedAsync(
        string promptId,
        string? continuationToken = null,
        int pageSize = 100)
    {
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.promptId = @promptId ORDER BY c.timestamp ASC")
            .WithParameter("@promptId", promptId);

        var options = new QueryRequestOptions
        {
            MaxItemCount = pageSize,
            PartitionKey = new PartitionKey(promptId)
        };

        return await CosmosSessionRepository.ExecutePagedQueryAsync<PromptLog>(
            _container, query, options, continuationToken);
    }
}
