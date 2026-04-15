namespace CopilotTracker.Cosmos;

using System.Net;
using Microsoft.Azure.Cosmos;
using CopilotTracker.Core.Interfaces;
using CopilotTracker.Core.Models;

public class CosmosTaskRepository : ITaskRepository
{
    private readonly Container _container;

    public CosmosTaskRepository(Database database)
    {
        _container = database.GetContainer("tasks");
    }

    public async Task<TrackerTask> CreateAsync(TrackerTask task)
    {
        var response = await _container.CreateItemAsync(task, new PartitionKey(task.QueueName));
        return response.Resource;
    }

    public async Task<TrackerTask?> GetAsync(string id, string queueName)
    {
        try
        {
            var response = await _container.ReadItemAsync<TrackerTask>(id, new PartitionKey(queueName));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<TrackerTask> UpdateAsync(TrackerTask task)
    {
        task.UpdatedAt = DateTime.UtcNow;
        var response = await _container.ReplaceItemAsync(task, task.Id, new PartitionKey(task.QueueName));
        return response.Resource;
    }

    public async Task<PagedResult<TrackerTask>> GetBySessionAsync(
        string sessionId,
        string? continuationToken = null,
        int pageSize = 50)
    {
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.sessionId = @sessionId ORDER BY c.createdAt DESC")
            .WithParameter("@sessionId", sessionId);

        // Cross-partition query since sessionId is not the partition key
        var options = new QueryRequestOptions { MaxItemCount = pageSize };

        return await CosmosSessionRepository.ExecutePagedQueryAsync<TrackerTask>(
            _container, query, options, continuationToken);
    }

    public async Task<PagedResult<TrackerTask>> GetByQueueAsync(
        string queueName,
        string? status = null,
        string? continuationToken = null,
        int pageSize = 50)
    {
        var clauses = new List<string> { "c.queueName = @queueName" };

        if (status is not null)
            clauses.Add("c.status = @status");

        var sql = $"SELECT * FROM c WHERE {string.Join(" AND ", clauses)} ORDER BY c.createdAt DESC";
        var queryDef = new QueryDefinition(sql)
            .WithParameter("@queueName", queueName);
        if (status is not null) queryDef = queryDef.WithParameter("@status", status);

        var options = new QueryRequestOptions
        {
            MaxItemCount = pageSize,
            PartitionKey = new PartitionKey(queueName)
        };

        return await CosmosSessionRepository.ExecutePagedQueryAsync<TrackerTask>(
            _container, queryDef, options, continuationToken);
    }

    public async Task<PagedResult<TrackerTask>> ListAsync(
        string? queueName = null,
        string? status = null,
        string? continuationToken = null,
        int pageSize = 50)
    {
        var clauses = new List<string>();
        var parameters = new Dictionary<string, object>();

        if (queueName is not null)
        {
            clauses.Add("c.queueName = @queueName");
            parameters["@queueName"] = queueName;
        }

        if (status is not null)
        {
            clauses.Add("c.status = @status");
            parameters["@status"] = status;
        }

        var where = clauses.Count > 0 ? " WHERE " + string.Join(" AND ", clauses) : string.Empty;
        var sql = $"SELECT * FROM c{where} ORDER BY c.createdAt DESC";
        var queryDef = new QueryDefinition(sql);
        foreach (var param in parameters)
            queryDef = queryDef.WithParameter(param.Key, param.Value);

        var options = new QueryRequestOptions { MaxItemCount = pageSize };
        if (queueName is not null)
            options.PartitionKey = new PartitionKey(queueName);

        return await CosmosSessionRepository.ExecutePagedQueryAsync<TrackerTask>(
            _container, queryDef, options, continuationToken);
    }
}
