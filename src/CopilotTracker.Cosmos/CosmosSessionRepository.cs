namespace CopilotTracker.Cosmos;

using System.Net;
using Microsoft.Azure.Cosmos;
using CopilotTracker.Core.Interfaces;
using CopilotTracker.Core.Models;

public class CosmosSessionRepository : ISessionRepository
{
    private readonly Container _container;

    public CosmosSessionRepository(Database database)
    {
        _container = database.GetContainer("sessions");
    }

    public async Task<Session> CreateAsync(Session session)
    {
        var response = await _container.CreateItemAsync(session, new PartitionKey(session.MachineId));
        return response.Resource;
    }

    public async Task<Session?> GetAsync(string id, string machineId)
    {
        try
        {
            var response = await _container.ReadItemAsync<Session>(id, new PartitionKey(machineId));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<Session> UpdateAsync(Session session)
    {
        session.UpdatedAt = DateTime.UtcNow;
        var response = await _container.ReplaceItemAsync(session, session.Id, new PartitionKey(session.MachineId));
        return response.Resource;
    }

    public async Task<IReadOnlyList<Session>> GetActiveByMachineAsync(string machineId)
    {
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.machineId = @machineId AND c.status = @status ORDER BY c.updatedAt DESC")
            .WithParameter("@machineId", machineId)
            .WithParameter("@status", SessionStatus.Active);

        var options = new QueryRequestOptions { PartitionKey = new PartitionKey(machineId) };
        var results = new List<Session>();

        using var iterator = _container.GetItemQueryIterator<Session>(query, requestOptions: options);
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }

        return results;
    }

    public async Task<PagedResult<Session>> ListAsync(
        string? machineId = null,
        string? status = null,
        string? tool = null,
        DateTime? since = null,
        string? continuationToken = null,
        int pageSize = 50)
    {
        var clauses = new List<string>();

        if (machineId is not null)
            clauses.Add("c.machineId = @machineId");

        if (status is not null)
            clauses.Add("c.status = @status");

        if (tool is not null)
        {
            // Old documents won't have a tool field; treat them as "copilot"
            if (tool.Equals("copilot", StringComparison.OrdinalIgnoreCase))
                clauses.Add("(c.tool = @tool OR NOT IS_DEFINED(c.tool))");
            else
                clauses.Add("c.tool = @tool");
        }

        if (since is not null)
            clauses.Add("c.createdAt >= @since");

        var where = clauses.Count > 0 ? " WHERE " + string.Join(" AND ", clauses) : string.Empty;
        var sql = $"SELECT * FROM c{where} ORDER BY c.updatedAt DESC";
        var queryDef = new QueryDefinition(sql);

        if (machineId is not null) queryDef = queryDef.WithParameter("@machineId", machineId);
        if (status is not null) queryDef = queryDef.WithParameter("@status", status);
        if (tool is not null) queryDef = queryDef.WithParameter("@tool", tool);
        if (since is not null) queryDef = queryDef.WithParameter("@since", since.Value);

        var options = new QueryRequestOptions { MaxItemCount = pageSize };
        if (machineId is not null)
            options.PartitionKey = new PartitionKey(machineId);

        return await ExecutePagedQueryAsync<Session>(_container, queryDef, options, continuationToken);
    }

    public async Task<PagedResult<Session>> GetStaleSessionsAsync(
        DateTime heartbeatBefore,
        string? continuationToken = null,
        int pageSize = 50)
    {
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.status = @status AND c.lastHeartbeat < @heartbeatBefore ORDER BY c.updatedAt DESC")
            .WithParameter("@status", SessionStatus.Active)
            .WithParameter("@heartbeatBefore", heartbeatBefore);

        var options = new QueryRequestOptions { MaxItemCount = pageSize };

        return await ExecutePagedQueryAsync<Session>(_container, query, options, continuationToken);
    }

    internal static async Task<PagedResult<T>> ExecutePagedQueryAsync<T>(
        Container container,
        QueryDefinition queryDefinition,
        QueryRequestOptions options,
        string? continuationToken)
    {
        var items = new List<T>();

        using var iterator = container.GetItemQueryIterator<T>(
            queryDefinition,
            continuationToken: continuationToken,
            requestOptions: options);

        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            items.AddRange(response);
            return new PagedResult<T>
            {
                Items = items,
                ContinuationToken = response.ContinuationToken
            };
        }

        return new PagedResult<T> { Items = items };
    }
}
