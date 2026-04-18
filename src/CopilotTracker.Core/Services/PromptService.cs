namespace CopilotTracker.Core.Services;

using CopilotTracker.Core.Interfaces;
using CopilotTracker.Core.Models;
using Microsoft.Extensions.Logging;

public class PromptService
{
    private readonly IPromptRepository _repository;
    private readonly ILogger<PromptService> _logger;

    public PromptService(IPromptRepository repository, ILogger<PromptService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public virtual async Task<Prompt> CreatePromptAsync(
        string sessionId, string promptText, string? cwd, long hookTimestamp,
        string userId, string createdBy, string? title = null)
    {
        // Close any active MISSED START prompt before creating a real one
        var activePrompt = await _repository.GetActiveBySessionAsync(sessionId);
        if (activePrompt != null && activePrompt.PromptText == "MISSED START")
        {
            activePrompt.Status = "done";
            activePrompt.CompletedAt = DateTime.UtcNow;
            activePrompt.UpdatedAt = DateTime.UtcNow;
            await _repository.UpdateAsync(activePrompt);
        }

        var prompt = new Prompt
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = sessionId,
            PromptText = promptText,
            Title = title,
            Cwd = cwd,
            Status = "started",
            HookTimestamp = hookTimestamp,
            UserId = userId,
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _repository.CreateAsync(prompt);
        return prompt;
    }

    public virtual async Task<Prompt?> CompleteActivePromptAsync(string sessionId, long hookTimestamp)
    {
        var activePrompt = await _repository.GetActiveBySessionAsync(sessionId);
        if (activePrompt == null) return null;

        activePrompt.Status = "done";
        activePrompt.CompletedAt = DateTime.UtcNow;
        activePrompt.UpdatedAt = DateTime.UtcNow;
        await _repository.UpdateAsync(activePrompt);
        return activePrompt;
    }

    public virtual async Task<Prompt> GetOrCreateActivePromptAsync(
        string sessionId, string userId, string createdBy)
    {
        var activePrompt = await _repository.GetActiveBySessionAsync(sessionId);
        if (activePrompt != null) return activePrompt;

        // Create a MISSED START prompt for events that arrive without an active prompt
        var missedPrompt = new Prompt
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = sessionId,
            PromptText = "MISSED START",
            Status = "started",
            HookTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            UserId = userId,
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _repository.CreateAsync(missedPrompt);
        return missedPrompt;
    }

    /// <summary>
    /// Returns the active prompt if one exists, otherwise the most recent prompt.
    /// Never creates a new prompt. Returns null only if the session has no prompts at all.
    /// Used for subagent and notification events that should attach to the parent prompt.
    /// </summary>
    public virtual async Task<Prompt?> GetActiveOrLatestPromptAsync(string sessionId)
    {
        var activePrompt = await _repository.GetActiveBySessionAsync(sessionId);
        if (activePrompt != null) return activePrompt;

        return await _repository.GetLatestBySessionAsync(sessionId);
    }

    public virtual async Task<Prompt?> GetAsync(string sessionId, string id)
    {
        return await _repository.GetAsync(sessionId, id);
    }

    public virtual async Task<PagedResult<Prompt>> ListAsync(
        string? sessionId, string? status, DateTime? since,
        string? continuationToken, int pageSize)
    {
        return await _repository.ListAsync(sessionId, status, since, continuationToken, pageSize);
    }

    public virtual async Task<IReadOnlyList<Prompt>> GetBySessionAsync(string sessionId)
    {
        return await _repository.GetBySessionAsync(sessionId);
    }
}
