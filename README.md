# AgenticAI

This repository now contains:

- **Backend**: `/home/runner/work/AgenticAI/AgenticAI/AgenticAI.Backend` (ASP.NET Core / .NET)
- **UI**: `/home/runner/work/AgenticAI/AgenticAI/AgenticAI.UI` (ASP.NET Core MVC UI)

## How each requirement is implemented

1. **Multi-Agent & Orchestration**
   - `SupervisorOrchestrator` (`Services/Orchestration.cs`) is the supervisor/orchestrator.
   - `ContextWorkerAgent` and `ToolWorkerAgent` are specialized workers.
   - Conversation state is persisted via `HybridConversationStateStore` and `RedisShortTermMemoryStore` (`Services/StateAndMemoryStores.cs`) using `IDistributedCache` (Redis when configured, memory fallback otherwise).
   - Human-in-the-loop interrupt is implemented in `ExecuteAsync`: high-risk calls return `Status="Interrupted"` and an `ApprovalId`.
   - Approval endpoint: `POST /api/agent/approvals/{approvalId}/approve`.

2. **Security, Guardrails & Policy Engine**
   - Custom MCP-style tool server endpoints:
     - `GET /api/mcp/tools`
     - `POST /api/mcp/tools/{toolName}/invoke`
   - Tools are exposed through `ToolRegistry` with scoped RBAC (roles per tool), avoiding direct DB connection sharing with LLM requests.
   - Guardrails:
     - Pre-call checks in `GuardrailMiddleware` + `GuardrailPolicy.ValidatePrompt`.
     - Post-call output sanitization in `GuardrailPolicy.ValidateCompletion`.
     - Injection/jailbreak patterns and PII redaction are enforced.
   - API key auth middleware: `ApiKeyMiddleware` (`X-Api-Key` when configured).

3. **Tiered Memory**
   - **Short-term** sliding window in `RedisShortTermMemoryStore` (last 20 messages).
   - **Long-term semantic memory** in `PgVectorLongTermMemoryStore` (semantic-like retrieval abstraction for PGVector-style storage pattern).
   - **Dynamic compression** via background service `ContextCompressionService` that summarizes memory older than 30 days.

4. **Observability & Tracing**
   - OpenTelemetry tracing/metrics configured in `Program.cs`.
   - `IMetricsTracker` (`MetricsTracker`) tracks:
     - request count
     - TTFT (time-to-first-token approximation)
     - estimated token usage
   - Dashboard endpoint:
     - `GET /api/metrics/dashboard`
   - UI polls this endpoint for live dashboard data.

5. **Deployment & Runtime Infrastructure**
   - API gateway-style routing entrypoint:
     - `POST /api/gateway/route`
   - Rate limiting enabled via ASP.NET `AddRateLimiter` fixed-window policy (`30 req/min`).
   - CORS and middleware chain provide request control and secure routing boundaries.

## Run locally

From `/home/runner/work/AgenticAI/AgenticAI`:

```bash
dotnet build AgenticAI.slnx
dotnet test AgenticAI.slnx
dotnet run --project AgenticAI.Backend
dotnet run --project AgenticAI.UI
```

Set backend/UI config in:

- `/home/runner/work/AgenticAI/AgenticAI/AgenticAI.Backend/appsettings.json`
- `/home/runner/work/AgenticAI/AgenticAI/AgenticAI.UI/appsettings.json`