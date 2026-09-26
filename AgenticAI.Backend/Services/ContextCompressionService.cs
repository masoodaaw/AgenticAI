namespace AgenticAI.Backend.Services;

public sealed class ContextCompressionService(IServiceProvider services, ILogger<ContextCompressionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = services.CreateScope();
            var memory = scope.ServiceProvider.GetRequiredService<ILongTermMemoryStore>();
            var compressed = await memory.CompressOlderThanAsync(DateTimeOffset.UtcNow.AddDays(-30), stoppingToken);
            if (compressed > 0)
            {
                logger.LogInformation("Compressed {Count} long-term memory items older than 30 days.", compressed);
            }

            await Task.Delay(TimeSpan.FromHours(12), stoppingToken);
        }
    }
}
