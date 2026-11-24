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
    private readonly int _tables, _bits, _seed;
    private int _dims;                                       // 0 until known
    private float[][][]? _planes;                            // [table][bit][dim]
    private readonly Dictionary<int, List<int>>[] _buckets;  // [table] key -> chunk ids
    private readonly ConcurrentDictionary<int, float[]> _vectors = new();
    private readonly object _lock = new();

    /// <param name="dims">Vector size, or 0 to take it from the first vector added
    /// (so switching embedding models, e.g. 1536-dim OpenAI vs 768-dim nomic-embed-text, needs no config change).</param>
    public LshIndex(int dims = 0, int tables = 8, int bits = 12, int seed = 42)
    {
        if (bits is < 1 or > 30) throw new ArgumentOutOfRangeException(nameof(bits));
        if (tables < 1) throw new ArgumentOutOfRangeException(nameof(tables));
        if (dims < 0) throw new ArgumentOutOfRangeException(nameof(dims));
        (_tables, _bits, _seed) = (tables, bits, seed);
        _buckets = Enumerable.Range(0, tables).Select(_ => new Dictionary<int, List<int>>()).ToArray();
        if (dims > 0) Initialise(dims);
    }

    public int Count => _vectors.Count;
    public int Dims => _dims;

    private float[][][] Planes(int dims)
    {
        if (_planes is null)
            lock (_lock)
                if (_planes is null) Initialise(dims);
        if (dims != _dims) throw new ArgumentException($"Expected {_dims} dimensions, got {dims}.");
        return _planes!;
    }

    private void Initialise(int dims)
    {
        var rng = new Random(_seed);                         // fixed seed = repeatable tests
        var planes = new float[_tables][][];
        for (int t = 0; t < _tables; t++)
        {
            planes[t] = new float[_bits][];
            for (int b = 0; b < _bits; b++)
                planes[t][b] = Enumerable.Range(0, dims).Select(_ => (float)Gaussian(rng)).ToArray();
        }
        _dims = dims;
        _planes = planes;
    }

    private int[] Keys(float[] v)
    {
        var planes = Planes(v.Length);
        var keys = new int[_tables];
        for (int t = 0; t < _tables; t++)
        {
            int key = 0;
            for (int b = 0; b < _bits; b++)
            {
                double dot = 0;
                var p = planes[t][b];
                for (int i = 0; i < p.Length; i++) dot += p[i] * v[i];
                if (dot >= 0) key |= 1 << b;
            }
            keys[t] = key;
        }
        return keys;
    }

    public void Add(int chunkId, float[] vector)
    {
        var keys = Keys(vector);                             // hash outside the lock
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
        if (_planes is null) return Array.Empty<Hit>();      // nothing indexed yet
        var keys = Keys(query);
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
