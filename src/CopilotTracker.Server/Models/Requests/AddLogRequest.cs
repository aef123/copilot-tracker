namespace CopilotTracker.Server.Models.Requests;

public class AddLogRequest
{
    public required string LogType { get; set; }
    public required string Message { get; set; }
}
