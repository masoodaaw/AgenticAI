using AgenticAI.Backend.Services;

namespace AgenticAI.Backend.Middleware;

public sealed class GuardrailMiddleware(RequestDelegate next)
{
    public async Task Invoke(HttpContext context, IGuardrailPolicy policy)
    {
        try
        {
            if (context.Request.Path.StartsWithSegments("/api/agent/execute") && context.Request.HasJsonContentType())
            {
                context.Request.EnableBuffering();
                using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
                var body = await reader.ReadToEndAsync();
                context.Request.Body.Position = 0;
                policy.ValidatePrompt(body);
            }
        }
        catch (InvalidOperationException ex)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = ex.Message });
            return;
        }

        await next(context);
    }
}
