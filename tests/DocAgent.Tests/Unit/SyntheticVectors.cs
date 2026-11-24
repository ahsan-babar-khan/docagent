using DocAgent.Core.Search;

namespace DocAgent.Tests.Unit;

/// <summary>
/// Test data for the vector indexes. Purely random high-dimensional vectors are almost all orthogonal, so
/// "nearest neighbour" means little there. Real embeddings form topic clusters, so the data is clustered:
/// each vector = a random cluster centre + Gaussian noise. Queries are noisy copies of stored vectors.
/// </summary>
public static class SyntheticVectors
{
    public static float[][] Clustered(int count, int dims, int seed, int clusters = 100, double spread = 0.35)
    {
        var rng = new Random(seed);
        var centres = Enumerable.Range(0, clusters).Select(_ => Normalise(Gaussian(rng, dims, 1.0))).ToArray();
        return Enumerable.Range(0, count).Select(_ =>
        {
            var c = centres[rng.Next(clusters)];
            var noise = Gaussian(rng, dims, spread / Math.Sqrt(dims));
            return Normalise(c.Select((x, i) => x + noise[i]).ToArray());
        }).ToArray();
    }

    public static float[][] NoisyCopies(float[][] data, int count, double noise, int seed)
    {
        var rng = new Random(seed);
        int dims = data[0].Length;
        return Enumerable.Range(0, count).Select(_ =>
        {
            var v = data[rng.Next(data.Length)];
            var n = Gaussian(rng, dims, noise / Math.Sqrt(dims));
            return Normalise(v.Select((x, i) => x + n[i]).ToArray());
        }).ToArray();
    }

    /// <summary>Average share of the exact top-k that the approximate index also returns.</summary>
    public static double RecallAtK(IVectorIndex exact, IVectorIndex approx, float[][] queries, int k)
    {
        double total = 0;
        foreach (var q in queries)
        {
            var truth = exact.Search(q, k).Select(h => h.ChunkId).ToHashSet();
            total += approx.Search(q, k).Count(h => truth.Contains(h.ChunkId)) / (double)truth.Count;
        }
        return total / queries.Length;
    }

    private static float[] Gaussian(Random r, int dims, double sigma) =>
        Enumerable.Range(0, dims).Select(_ =>
            (float)(sigma * Math.Sqrt(-2 * Math.Log(1 - r.NextDouble())) * Math.Cos(2 * Math.PI * r.NextDouble()))).ToArray();

    private static float[] Normalise(float[] v)
    {
        var norm = (float)Math.Sqrt(v.Sum(x => x * x));
        return v.Select(x => x / norm).ToArray();
    }
}
