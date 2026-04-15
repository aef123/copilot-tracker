namespace CopilotTracker.Cosmos;

using Microsoft.Azure.Cosmos;
using CopilotTracker.Core.Interfaces;
using CopilotTracker.Core.Models;

public class CosmosTaskLogRepository : ITaskLogRepository
{
    private readonly Container _container;

    public CosmosTaskLogRepository(Database database)
    {
        _container = database.GetContainer("taskLogs");
    }

    public async Task<TaskLog> CreateAsync(TaskLog log)
    {
        var response = await _container.CreateItemAsync(log, new PartitionKey(log.TaskId));
        return response.Resource;
    }

    public async Task<IReadOnlyList<TaskLog>> GetByTaskAsync(string taskId)
    {
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.taskId = @taskId ORDER BY c.timestamp ASC")
            .WithParameter("@taskId", taskId);

        var options = new QueryRequestOptions { PartitionKey = new PartitionKey(taskId) };
        var results = new List<TaskLog>();

        using var iterator = _container.GetItemQueryIterator<TaskLog>(query, requestOptions: options);
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }

        return results;
    }

    public async Task<PagedResult<TaskLog>> GetByTaskPagedAsync(
        string taskId,
        string? continuationToken = null,
        int pageSize = 100)
    {
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.taskId = @taskId ORDER BY c.timestamp ASC")
            .WithParameter("@taskId", taskId);

        var options = new QueryRequestOptions
        {
            MaxItemCount = pageSize,
            PartitionKey = new PartitionKey(taskId)
        };

        return await CosmosSessionRepository.ExecutePagedQueryAsync<TaskLog>(
            _container, query, options, continuationToken);
    }
}
