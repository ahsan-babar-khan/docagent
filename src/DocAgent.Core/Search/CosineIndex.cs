using System.Collections.Concurrent;

namespace DocAgent.Core.Search;

/// <summary>Exact search: compares the query against every stored vector. The baseline.</summary>
public class CosineIndex : IVectorIndex
{
    private readonly ConcurrentDictionary<int, float[]> _vectors = new();

    public void Add(int chunkId, float[] vector) => _vectors[chunkId] = vector;

    public IReadOnlyList<Hit> Search(float[] query, int k) =>
        _vectors.Select(kv => new Hit(kv.Key, VectorMath.Cosine(query, kv.Value)))
                .OrderByDescending(h => h.Score).Take(k).ToList();
}
