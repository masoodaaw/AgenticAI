using System.Diagnostics;
using AgenticAI.Backend.Models;

namespace AgenticAI.Backend.Services;

public sealed class SupervisorOrchestrator(
    IEnumerable<IWorkerAgent> workers,
    IStateStore stateStore,
    IShortTermMemoryStore shortTermMemory,
    ILongTermMemoryStore longTermMemory,
    IApprovalService approvals,
    IToolRegistry tools,
    IMetricsTracker metrics,
    IGuardrailPolicy guardrails) : IAgentOrchestrator
{
    private static readonly ActivitySource ActivitySource = new("AgenticAI.Orchestrator");

    public async Task<AgentResult> ExecuteAsync(AgentRequest request, CancellationToken cancellationToken)
    {
        using var requestScope = metrics.TrackRequest();
        using var activity = ActivitySource.StartActivity("orchestrator.execute");
        activity?.SetTag("user.id", request.UserId);

        guardrails.ValidatePrompt(request.Message);

        var session = new ConversationState
        {
            SessionId = Guid.NewGuid(),
            UserId = request.UserId,
            Status = ConversationStatus.Running,
            Messages = [request.Message],
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        await stateStore.SaveAsync(session, cancellationToken);
        await shortTermMemory.AddMessageAsync(session.SessionId, request.Message, cancellationToken);

        var relevantMemory = await longTermMemory.QueryRelevantAsync(request.UserId, request.Message, cancellationToken);
        var ttft = Stopwatch.StartNew();

        var contextWorker = workers.First(x => x.Name == "context");
        var toolWorker = workers.First(x => x.Name == "tool");
        var contextResult = await contextWorker.ExecuteAsync(session, request, cancellationToken);

        metrics.TrackTtft(ttft.Elapsed);

        if (request.HighRisk && !string.IsNullOrWhiteSpace(request.RequestedTool))
        {
            session.Status = ConversationStatus.Interrupted;
            await stateStore.SaveAsync(session, cancellationToken);

            Guid approvalId = Guid.Empty;
            approvalId = await approvals.CreatePendingAsync(session, request, async ct =>
            {
                var invocation = await tools.InvokeAsync(request.RequestedTool!, "admin", new ToolInvocationRequest(request.UserId, request.Message, true), ct);
                var output = guardrails.ValidateCompletion(invocation.Output);
                session.Status = ConversationStatus.Completed;
                session.Messages.Add(output);
                await stateStore.SaveAsync(session, ct);
                await longTermMemory.SaveDecisionAsync(request.UserId, output, DateTimeOffset.UtcNow, ct);
                metrics.TrackTokenUsage(request.Message, output);
                return new AgentResult(session.SessionId, output, "Completed", approvalId, Activity.Current?.TraceId.ToString());
            }, cancellationToken);

            return new AgentResult(session.SessionId, "Execution paused for human approval.", "Interrupted", approvalId, Activity.Current?.TraceId.ToString());
        }

        var workerRequest = request with { Message = $"{request.Message}\nMemory: {string.Join("; ", relevantMemory)}" };
        var workerOutput = await toolWorker.ExecuteAsync(session, workerRequest, cancellationToken);
        var safeOutput = guardrails.ValidateCompletion(workerOutput);

        session.Status = ConversationStatus.Completed;
        session.Messages.Add(safeOutput);
        session.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await stateStore.SaveAsync(session, cancellationToken);

        await longTermMemory.SaveDecisionAsync(request.UserId, safeOutput, DateTimeOffset.UtcNow, cancellationToken);
        metrics.TrackTokenUsage(request.Message, safeOutput);

        return new AgentResult(session.SessionId, safeOutput, "Completed", null, Activity.Current?.TraceId.ToString());
    }
}

public sealed class ContextWorkerAgent : IWorkerAgent
{
    public string Name => "context";

    public Task<string> ExecuteAsync(ConversationState state, AgentRequest request, CancellationToken cancellationToken)
    {
        var output = $"Context worker expanded message for session {state.SessionId}";
        state.Messages.Add(output);
        return Task.FromResult(output);
    }
}

public sealed class ToolWorkerAgent(IToolRegistry tools) : IWorkerAgent
{
    public string Name => "tool";

    public async Task<string> ExecuteAsync(ConversationState state, AgentRequest request, CancellationToken cancellationToken)
    {
        var toolName = string.IsNullOrWhiteSpace(request.RequestedTool) ? "status.ping" : request.RequestedTool;
        var result = await tools.InvokeAsync(toolName, "admin", new ToolInvocationRequest(request.UserId, request.Message, request.HighRisk), cancellationToken);
        return result.Output;
    }
}
