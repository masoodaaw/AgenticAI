using System.Diagnostics;
using System.Threading.RateLimiting;
using AgenticAI.Backend.Middleware;
using AgenticAI.Backend.Models;
using AgenticAI.Backend.Services;
using Microsoft.AspNetCore.RateLimiting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddAuthorization();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("api", limiter =>
    {
        limiter.PermitLimit = 30;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiter.QueueLimit = 10;
    });
});

var redisConnection = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redisConnection))
{
    builder.Services.AddStackExchangeRedisCache(options => options.Configuration = redisConnection);
}
else
{
    builder.Services.AddDistributedMemoryCache();
}

builder.Services.AddSingleton<IGuardrailPolicy, GuardrailPolicy>();
builder.Services.AddSingleton<IMetricsTracker, MetricsTracker>();
builder.Services.AddSingleton<IStateStore, HybridConversationStateStore>();
builder.Services.AddSingleton<IShortTermMemoryStore, RedisShortTermMemoryStore>();
builder.Services.AddSingleton<ILongTermMemoryStore, PgVectorLongTermMemoryStore>();
builder.Services.AddSingleton<IToolRegistry, ToolRegistry>();
builder.Services.AddSingleton<IApprovalService, ApprovalService>();
builder.Services.AddSingleton<IWorkerAgent, ContextWorkerAgent>();
builder.Services.AddSingleton<IWorkerAgent, ToolWorkerAgent>();
builder.Services.AddSingleton<IAgentOrchestrator, SupervisorOrchestrator>();
builder.Services.AddHostedService<ContextCompressionService>();

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("AgenticAI.Backend"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource("AgenticAI.Orchestrator")
        .AddConsoleExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddMeter("AgenticAI.Metrics")
        .AddConsoleExporter());

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ApiKeyMiddleware>();
app.UseMiddleware<GuardrailMiddleware>();
app.UseRateLimiter();
app.UseHttpsRedirection();
app.UseCors();
app.UseAuthorization();

var api = app.MapGroup("/api").RequireRateLimiting("api");

api.MapPost("/agent/execute", async (AgentRequest request, IAgentOrchestrator orchestrator, CancellationToken ct) =>
{
    var result = await orchestrator.ExecuteAsync(request, ct);
    return Results.Ok(result);
});

api.MapPost("/agent/approvals/{approvalId:guid}/approve", async (Guid approvalId, ApprovalAction action, IApprovalService approvals, CancellationToken ct) =>
{
    var result = await approvals.ResolveAsync(approvalId, action, ct);
    return result is null ? Results.NotFound() : Results.Ok(result);
});

api.MapGet("/mcp/tools", (IToolRegistry tools) => Results.Ok(tools.GetToolDefinitions()));
api.MapPost("/mcp/tools/{toolName}/invoke", async (string toolName, ToolInvocationRequest request, HttpContext context, IToolRegistry tools, CancellationToken ct) =>
{
    var role = context.Request.Headers["X-Role"].FirstOrDefault() ?? "viewer";
    var result = await tools.InvokeAsync(toolName, role, request, ct);
    return result.Allowed ? Results.Ok(result) : Results.Forbid();
});

api.MapGet("/metrics/dashboard", (IMetricsTracker metrics) => Results.Ok(metrics.GetSnapshot()));

api.MapPost("/gateway/route", async (GatewayRouteRequest request, IToolRegistry tools, CancellationToken ct) =>
{
    if (request.Target.Equals("mcp", StringComparison.OrdinalIgnoreCase))
    {
        var invocation = new ToolInvocationRequest(request.UserId, request.Payload, request.HighRisk);
        var result = await tools.InvokeAsync(request.Operation, request.Role, invocation, ct);
        return result.Allowed ? Results.Ok(result) : Results.Forbid();
    }

    return Results.BadRequest(new { error = "Unknown route target" });
});

app.Run();

public partial class Program;
