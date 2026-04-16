namespace CopilotTracker.Core.Interfaces;

using CopilotTracker.Core.Models;

public interface IPromptLogRepository
{
    Task<PromptLog> CreateAsync(PromptLog log);
    Task<IReadOnlyList<PromptLog>> GetByPromptAsync(string promptId);
    Task<PagedResult<PromptLog>> GetByPromptPagedAsync(string promptId, string? continuationToken = null, int pageSize = 100);
}
