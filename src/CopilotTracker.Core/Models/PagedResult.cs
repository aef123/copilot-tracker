namespace CopilotTracker.Core.Models;

public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; set; } = [];
    public string? ContinuationToken { get; set; }
    public bool HasMore => ContinuationToken != null;
}
