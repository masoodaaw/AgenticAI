using System.Collections.Concurrent;
using System.Text.Json;
using AgenticAI.Backend.Models;
using Microsoft.Extensions.Caching.Distributed;

namespace AgenticAI.Backend.Services;

public sealed class HybridConversationStateStore(IDistributedCache cache) : IStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task SaveAsync(ConversationState state, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(state, JsonOptions);
        return cache.SetStringAsync(StateKey(state.SessionId), payload, new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromHours(24)
        }, cancellationToken);
    }

    public async Task<ConversationState?> GetAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var payload = await cache.GetStringAsync(StateKey(sessionId), cancellationToken);
        return payload is null ? null : JsonSerializer.Deserialize<ConversationState>(payload, JsonOptions);
    }

    private static string StateKey(Guid sessionId) => $"conversation:{sessionId}";
}

public sealed class RedisShortTermMemoryStore(IDistributedCache cache) : IShortTermMemoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task AddMessageAsync(Guid sessionId, string message, CancellationToken cancellationToken)
    {
        var key = WindowKey(sessionId);
        var existing = await cache.GetStringAsync(key, cancellationToken);
        var list = existing is null ? [] : JsonSerializer.Deserialize<List<string>>(existing, JsonOptions) ?? [];
        list.Add(message);
        if (list.Count > 20)
        {
            list = list[^20..];
        }

        await cache.SetStringAsync(key, JsonSerializer.Serialize(list, JsonOptions), new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromHours(4)
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetWindowAsync(Guid sessionId, int maxMessages, CancellationToken cancellationToken)
    {
        var existing = await cache.GetStringAsync(WindowKey(sessionId), cancellationToken);
        var list = existing is null ? [] : JsonSerializer.Deserialize<List<string>>(existing, JsonOptions) ?? [];
        return list.TakeLast(maxMessages).ToList();
    }

    private static string WindowKey(Guid sessionId) => $"memory:window:{sessionId}";
}

public sealed class PgVectorLongTermMemoryStore : ILongTermMemoryStore
{
    private readonly ConcurrentDictionary<string, List<(DateTimeOffset timestamp, string value)>> _memory = new();

    public Task SaveDecisionAsync(string userId, string decision, DateTimeOffset createdAtUtc, CancellationToken cancellationToken)
    {
        var bucket = _memory.GetOrAdd(userId, _ => []);
        lock (bucket)
        {
            bucket.Add((createdAtUtc, decision));
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> QueryRelevantAsync(string userId, string message, CancellationToken cancellationToken)
    {
        if (!_memory.TryGetValue(userId, out var values))
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        var terms = message.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var relevant = values
            .OrderByDescending(v => Score(v.value, terms))
            .Take(5)
            .Select(v => v.value)
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(relevant);
    }

    public Task<int> CompressOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken)
    {
        var compressed = 0;
        foreach (var (user, items) in _memory)
        {
            lock (items)
            {
                var old = items.Where(x => x.timestamp < cutoffUtc).ToList();
                if (old.Count == 0)
                {
                    continue;
                }

                compressed += old.Count;
                items.RemoveAll(x => x.timestamp < cutoffUtc);
                var summary = $"Summary({old.Count}): {string.Join(" | ", old.Take(3).Select(x => x.value))}";
                items.Add((DateTimeOffset.UtcNow, summary));
            }
        }

        return Task.FromResult(compressed);
    }

    private static int Score(string candidate, IEnumerable<string> terms)
    {
        return terms.Sum(term => candidate.Contains(term, StringComparison.OrdinalIgnoreCase) ? 1 : 0);
    }
}
