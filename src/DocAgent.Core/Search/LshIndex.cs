using System.Collections.Concurrent;

namespace DocAgent.Core.Search;

/// <summary>
/// Approximate search with locality-sensitive hashing (random hyperplanes).
/// Each table draws <c>bits</c> random hyperplanes; a vector's key records which side of each plane it is on.
/// Vectors at a small angle usually share a key, so a query is only compared against its matching buckets.
/// More tables raise recall; more bits make buckets smaller (faster, lower recall).
/// </summary>
public class LshIndex : IVectorIndex
{
    private readonly int _tables, _bits, _dims;
    private readonly float[][][] _planes;                    // [table][bit][dim]
    private readonly Dictionary<int, List<int>>[] _buckets;  // [table] key -> chunk ids
    private readonly ConcurrentDictionary<int, float[]> _vectors = new();
    private readonly object _lock = new();

    public LshIndex(int dims, int tables = 8, int bits = 12, int seed = 42)
    {
        if (bits is < 1 or > 30) throw new ArgumentOutOfRangeException(nameof(bits));
        if (tables < 1) throw new ArgumentOutOfRangeException(nameof(tables));
        (_dims, _tables, _bits) = (dims, tables, bits);
        var rng = new Random(seed);                          // fixed seed = repeatable tests
        _planes = new float[tables][][];
        _buckets = new Dictionary<int, List<int>>[tables];
        for (int t = 0; t < tables; t++)
        {
            _buckets[t] = new();
            _planes[t] = new float[bits][];
            for (int b = 0; b < bits; b++)
                _planes[t][b] = Enumerable.Range(0, dims).Select(_ => (float)Gaussian(rng)).ToArray();
        }
    }

    public int Count => _vectors.Count;

    private int Key(int table, float[] v)
    {
        if (v.Length != _dims) throw new ArgumentException($"Expected {_dims} dimensions, got {v.Length}.");
        int key = 0;
        for (int b = 0; b < _bits; b++)
        {
            double dot = 0;
            var p = _planes[table][b];
            for (int i = 0; i < _dims; i++) dot += p[i] * v[i];
            if (dot >= 0) key |= 1 << b;
        }
        return key;
    }

    public void Add(int chunkId, float[] vector)
    {
        var keys = new int[_tables];
        for (int t = 0; t < _tables; t++) keys[t] = Key(t, vector); // hash outside the lock
        _vectors[chunkId] = vector;
        lock (_lock)
            for (int t = 0; t < _tables; t++)
            {
                if (!_buckets[t].TryGetValue(keys[t], out var list)) _buckets[t][keys[t]] = list = new();
                list.Add(chunkId);
            }
    }

    public IReadOnlyList<Hit> Search(float[] query, int k)
    {
        var keys = new int[_tables];
        for (int t = 0; t < _tables; t++) keys[t] = Key(t, query);
        var candidates = new HashSet<int>();
        lock (_lock)
            for (int t = 0; t < _tables; t++)
                if (_buckets[t].TryGetValue(keys[t], out var list)) candidates.UnionWith(list);

        return candidates.Select(id => new Hit(id, VectorMath.Cosine(query, _vectors[id])))
                         .OrderByDescending(h => h.Score).Take(k).ToList();
    }

    private static double Gaussian(Random r) =>          // Box-Muller
        Math.Sqrt(-2 * Math.Log(1 - r.NextDouble())) * Math.Cos(2 * Math.PI * r.NextDouble());
}
