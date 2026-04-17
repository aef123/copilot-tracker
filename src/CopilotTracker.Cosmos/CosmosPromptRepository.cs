namespace CopilotTracker.Cosmos;

using System.Net;
using Microsoft.Azure.Cosmos;
using CopilotTracker.Core.Interfaces;
using CopilotTracker.Core.Models;

public class CosmosPromptRepository : IPromptRepository
{
    private readonly Container _container;

    public CosmosPromptRepository(Database database)
    {
        _container = database.GetContainer("prompts");
    }

    public async Task<Prompt> CreateAsync(Prompt prompt)
    {
        var response = await _container.CreateItemAsync(prompt, new PartitionKey(prompt.SessionId));
        return response.Resource;
    }

    public async Task<Prompt?> GetAsync(string sessionId, string id)
    {
        try
        {
            var response = await _container.ReadItemAsync<Prompt>(id, new PartitionKey(sessionId));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<Prompt> UpdateAsync(Prompt prompt)
    {
        prompt.UpdatedAt = DateTime.UtcNow;
        var response = await _container.ReplaceItemAsync(prompt, prompt.Id, new PartitionKey(prompt.SessionId));
        return response.Resource;
    }

    public async Task<Prompt?> GetActiveBySessionAsync(string sessionId)
    {
        var query = new QueryDefinition(
            "SELECT TOP 1 * FROM c WHERE c.sessionId = @sessionId AND c.status = 'started' ORDER BY c.hookTimestamp DESC")
            .WithParameter("@sessionId", sessionId);

        var options = new QueryRequestOptions
        {
            MaxItemCount = 1,
            PartitionKey = new PartitionKey(sessionId)
        };

        using var iterator = _container.GetItemQueryIterator<Prompt>(query, requestOptions: options);
        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            return response.FirstOrDefault();
        }

        return null;
    }

    public async Task<IReadOnlyList<Prompt>> GetBySessionAsync(string sessionId)
    {
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.sessionId = @sessionId ORDER BY c.createdAt DESC")
            .WithParameter("@sessionId", sessionId);

        var options = new QueryRequestOptions { PartitionKey = new PartitionKey(sessionId) };
        var results = new List<Prompt>();

        using var iterator = _container.GetItemQueryIterator<Prompt>(query, requestOptions: options);
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }

        return results;
    }

    public async Task<PagedResult<Prompt>> ListAsync(
        string? sessionId = null,
        string? status = null,
        DateTime? since = null,
        string? continuationToken = null,
        int pageSize = 50)
    {
        var clauses = new List<string>();
        var parameters = new Dictionary<string, object>();

        if (sessionId is not null)
        {
            clauses.Add("c.sessionId = @sessionId");
            parameters["@sessionId"] = sessionId;
        }

        if (status is not null)
        {
            clauses.Add("c.status = @status");
            parameters["@status"] = status;
        }

        if (since is not null)
        {
            clauses.Add("c.createdAt >= @since");
            parameters["@since"] = since.Value;
        }

        var where = clauses.Count > 0 ? " WHERE " + string.Join(" AND ", clauses) : string.Empty;
        var sql = $"SELECT * FROM c{where} ORDER BY c.createdAt DESC";
        var queryDef = new QueryDefinition(sql);
        foreach (var param in parameters)
            queryDef = queryDef.WithParameter(param.Key, param.Value);

        var options = new QueryRequestOptions { MaxItemCount = pageSize };
        if (sessionId is not null)
            options.PartitionKey = new PartitionKey(sessionId);

        return await CosmosSessionRepository.ExecutePagedQueryAsync<Prompt>(
            _container, queryDef, options, continuationToken);
    }

    public async Task<IReadOnlySet<string>> GetSessionIdsWithActivePromptsAsync(IEnumerable<string> sessionIds)
    {
        var ids = sessionIds.ToList();
        if (ids.Count == 0)
            return new HashSet<string>();

        // Build parameterized IN clause
        var paramNames = ids.Select((_, i) => $"@sid{i}").ToList();
        var sql = $"SELECT DISTINCT VALUE c.sessionId FROM c WHERE c.status = 'started' AND c.sessionId IN ({string.Join(", ", paramNames)})";
        var queryDef = new QueryDefinition(sql);
        for (int i = 0; i < ids.Count; i++)
            queryDef = queryDef.WithParameter($"@sid{i}", ids[i]);

        var result = new HashSet<string>();
        using var iterator = _container.GetItemQueryIterator<string>(queryDef);
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            foreach (var sid in response)
                result.Add(sid);
        }

        return result;
    }

    public async Task<int> CountByStatusAsync(string status)
    {
        var query = new QueryDefinition(
            "SELECT VALUE COUNT(1) FROM c WHERE c.status = @status")
            .WithParameter("@status", status);

        var options = new QueryRequestOptions();

        using var iterator = _container.GetItemQueryIterator<int>(query, requestOptions: options);
        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            return response.FirstOrDefault();
        }

        return 0;
    }
}
