using AgenticAI.Backend.Models;

namespace AgenticAI.Backend.Services;

public interface IGuardrailPolicy
{
    void ValidatePrompt(string prompt);
    string ValidateCompletion(string output);
}

public interface IStateStore
{
    Task SaveAsync(ConversationState state, CancellationToken cancellationToken);
    Task<ConversationState?> GetAsync(Guid sessionId, CancellationToken cancellationToken);
}

public interface IShortTermMemoryStore
{
    Task AddMessageAsync(Guid sessionId, string message, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> GetWindowAsync(Guid sessionId, int maxMessages, CancellationToken cancellationToken);
}

public interface ILongTermMemoryStore
{
    Task SaveDecisionAsync(string userId, string decision, DateTimeOffset createdAtUtc, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> QueryRelevantAsync(string userId, string message, CancellationToken cancellationToken);
    Task<int> CompressOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken);
}

public interface IWorkerAgent
{
    string Name { get; }
    Task<string> ExecuteAsync(ConversationState state, AgentRequest request, CancellationToken cancellationToken);
}

public interface IToolRegistry
{
    IReadOnlyList<ToolDefinition> GetToolDefinitions();
    Task<ToolInvocationResult> InvokeAsync(string toolName, string role, ToolInvocationRequest request, CancellationToken cancellationToken);
}

public interface IApprovalService
{
    Task<Guid> CreatePendingAsync(ConversationState state, AgentRequest request, Func<CancellationToken, Task<AgentResult>> onApproved, CancellationToken cancellationToken);
    Task<AgentResult?> ResolveAsync(Guid approvalId, ApprovalAction action, CancellationToken cancellationToken);
}

public interface IAgentOrchestrator
{
    Task<AgentResult> ExecuteAsync(AgentRequest request, CancellationToken cancellationToken);
}

public interface IMetricsTracker
{
    IDisposable TrackRequest();
    void TrackTtft(TimeSpan elapsed);
    void TrackTokenUsage(string input, string output);
    DashboardSnapshot GetSnapshot();
}
