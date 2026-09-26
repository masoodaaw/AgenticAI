using System.Diagnostics;
using System.Diagnostics.Metrics;
using AgenticAI.Backend.Models;

namespace AgenticAI.Backend.Services;

public sealed class MetricsTracker : IMetricsTracker
{
    private static readonly Meter Meter = new("AgenticAI.Metrics");
    private static readonly Counter<long> RequestCounter = Meter.CreateCounter<long>("agent.requests.total");
    private static readonly Histogram<double> TtftHistogram = Meter.CreateHistogram<double>("agent.ttft.ms");
    private static readonly Counter<long> TokenCounter = Meter.CreateCounter<long>("agent.tokens.estimated");

    private long _totalRequests;
    private long _totalTokens;
    private double _totalTtftMs;
    private readonly object _sync = new();

    public IDisposable TrackRequest()
    {
        RequestCounter.Add(1);
        Interlocked.Increment(ref _totalRequests);
        return Activity.Current is null ? new Activity("agent.request").Start() : NullDisposable.Instance;
    }

    public void TrackTtft(TimeSpan elapsed)
    {
        TtftHistogram.Record(elapsed.TotalMilliseconds);
        lock (_sync)
        {
            _totalTtftMs += elapsed.TotalMilliseconds;
        }
    }

    public void TrackTokenUsage(string input, string output)
    {
        var estimate = EstimateTokens(input) + EstimateTokens(output);
        TokenCounter.Add(estimate);
        Interlocked.Add(ref _totalTokens, estimate);
    }

    public DashboardSnapshot GetSnapshot()
    {
        var requests = Interlocked.Read(ref _totalRequests);
        var totalTtft = 0d;
        lock (_sync)
        {
            totalTtft = _totalTtftMs;
        }

        var averageTtft = requests == 0 ? 0 : totalTtft / requests;
        return new DashboardSnapshot
        {
            TotalRequests = requests,
            AverageTtftMs = Math.Round(averageTtft, 2),
            TotalEstimatedTokens = Interlocked.Read(ref _totalTokens),
            CapturedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static int EstimateTokens(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return 0;
        }

        return Math.Max(1, content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();
        public void Dispose() { }
    }
}
