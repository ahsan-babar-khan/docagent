using System.Text.RegularExpressions;
using DocAgent.Core.Ai;

namespace DocAgent.Tests.Fakes;

/// <summary>
/// Bag-of-words embedding: each word is hashed into one of 256 slots, so texts that share words really are similar.
/// Uses FNV-1a instead of string.GetHashCode(), which .NET randomises per process; this keeps results identical across runs.
/// </summary>
public class FakeEmbeddingService : IEmbeddingService
{
    public const int Dims = 256;
    public int Calls { get; private set; }

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        Calls++;
        var v = new float[Dims];
        foreach (Match w in Regex.Matches(text.ToLowerInvariant(), "[a-z0-9]+"))
            v[Fnv1a(w.Value) % Dims] += 1;
        return Task.FromResult(v);
    }

    private static uint Fnv1a(string s)
    {
        uint h = 2166136261;
        foreach (var ch in s) { h ^= ch; h *= 16777619; }
        return h;
    }
}
