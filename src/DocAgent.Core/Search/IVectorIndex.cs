namespace DocAgent.Core.Search;

public record Hit(int ChunkId, double Score);

public interface IVectorIndex
{
    void Add(int chunkId, float[] vector);
    IReadOnlyList<Hit> Search(float[] query, int k);
}

public static class VectorMath
{
    public static double Cosine(float[] a, float[] b)
    {
        if (a.Length != b.Length) throw new ArgumentException($"Vector length mismatch: {a.Length} vs {b.Length}.");
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb) + 1e-12);
    }
}
