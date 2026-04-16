namespace CopilotTracker.Server.Models.Requests;

public class InitializeSessionRequest
{
    public required string MachineId { get; set; }
    public string? Repository { get; set; }
    public string? Branch { get; set; }
}
