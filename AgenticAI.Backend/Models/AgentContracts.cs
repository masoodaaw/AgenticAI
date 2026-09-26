namespace AgenticAI.Backend.Models;

public sealed record AgentRequest(string UserId, string Message, bool HighRisk = false, string? RequestedTool = null);

public sealed record AgentResult(Guid SessionId, string Response, string Status, Guid? ApprovalId = null, string? TraceId = null);

public sealed record ToolInvocationRequest(string UserId, string Payload, bool HighRisk = false);

public sealed record ToolInvocationResult(bool Allowed, string Output, string ToolName, string Role);

public sealed record ToolDefinition(string Name, bool HighRisk, string[] AllowedRoles);

public sealed record ApprovalAction(string Reviewer, bool Approved, string? Notes = null);

public sealed record GatewayRouteRequest(string UserId, string Role, string Target, string Operation, string Payload, bool HighRisk = false);

public enum ConversationStatus
{
    Running,
    Interrupted,
    Completed,
    Rejected
}

public sealed class ConversationState
{
    public Guid SessionId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public List<string> Messages { get; set; } = [];
    public ConversationStatus Status { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DashboardSnapshot
{
    public long TotalRequests { get; set; }
    public double AverageTtftMs { get; set; }
    public long TotalEstimatedTokens { get; set; }
    public DateTimeOffset CapturedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
