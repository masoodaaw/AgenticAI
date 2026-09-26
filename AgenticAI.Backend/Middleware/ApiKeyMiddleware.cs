namespace AgenticAI.Backend.Middleware;

public sealed class ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration)
{
    public async Task Invoke(HttpContext context)
    {
        var configuredKey = configuration["Security:ApiKey"];
        if (!string.IsNullOrWhiteSpace(configuredKey))
        {
            var incoming = context.Request.Headers["X-Api-Key"].FirstOrDefault();
            if (!string.Equals(configuredKey, incoming, StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
                return;
            }
        }

        await next(context);
    }
}
