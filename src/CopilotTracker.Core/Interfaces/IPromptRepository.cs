namespace CopilotTracker.Core.Interfaces;

using CopilotTracker.Core.Models;

public interface IPromptRepository
{
    Task<Prompt> CreateAsync(Prompt prompt);
    Task<Prompt?> GetAsync(string sessionId, string id);
    Task<Prompt> UpdateAsync(Prompt prompt);
    Task<Prompt?> GetActiveBySessionAsync(string sessionId);
    Task<IReadOnlyList<Prompt>> GetBySessionAsync(string sessionId);
    Task<PagedResult<Prompt>> ListAsync(string? sessionId = null, string? status = null, DateTime? since = null, string? continuationToken = null, int pageSize = 50);
    Task<int> CountByStatusAsync(string status);
}
