using System.Collections.Concurrent;
using AgenticAI.Backend.Models;

namespace AgenticAI.Backend.Services;

public sealed class ToolRegistry : IToolRegistry
{
    private static readonly IReadOnlyDictionary<string, ToolDefinition> Definitions = new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase)
    {
        ["db.query"] = new("db.query", true, ["admin", "analyst"]),
        ["api.trigger"] = new("api.trigger", true, ["admin", "operator"]),
        ["status.ping"] = new("status.ping", false, ["admin", "analyst", "viewer", "operator"])
    };

    public IReadOnlyList<ToolDefinition> GetToolDefinitions() => Definitions.Values.ToList();

    public Task<ToolInvocationResult> InvokeAsync(string toolName, string role, ToolInvocationRequest request, CancellationToken cancellationToken)
    {
        if (!Definitions.TryGetValue(toolName, out var definition))
        {
            return Task.FromResult(new ToolInvocationResult(false, "Tool not found", toolName, role));
        }

        if (!definition.AllowedRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
        {
            return Task.FromResult(new ToolInvocationResult(false, "RBAC policy denied", toolName, role));
        }

        var result = toolName switch
        {
            "db.query" => $"Simulated scoped DB query for user {request.UserId}: {request.Payload}",
            "api.trigger" => $"Simulated downstream API trigger: {request.Payload}",
            _ => "ok"
        };

        return Task.FromResult(new ToolInvocationResult(true, result, toolName, role));
    }
}

public sealed class ApprovalService : IApprovalService
{
    private readonly ConcurrentDictionary<Guid, PendingApproval> _pending = new();

    public Task<Guid> CreatePendingAsync(ConversationState state, AgentRequest request, Func<CancellationToken, Task<AgentResult>> onApproved, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        _pending[id] = new PendingApproval(state, request, onApproved);
        return Task.FromResult(id);
    }

    public async Task<AgentResult?> ResolveAsync(Guid approvalId, ApprovalAction action, CancellationToken cancellationToken)
    {
        if (!_pending.TryRemove(approvalId, out var pending))
        {
            return null;
        }

        if (!action.Approved)
        {
            pending.State.Status = ConversationStatus.Rejected;
            return new AgentResult(pending.State.SessionId, $"Request rejected by {action.Reviewer}.", "Rejected", approvalId);
        }

        return await pending.OnApproved(cancellationToken);
    }

    private sealed record PendingApproval(ConversationState State, AgentRequest Request, Func<CancellationToken, Task<AgentResult>> OnApproved);
}
