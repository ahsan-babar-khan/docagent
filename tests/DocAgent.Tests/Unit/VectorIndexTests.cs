using DocAgent.Core.Search;
using FluentAssertions;

namespace DocAgent.Tests.Unit;

public class VectorIndexTests
{
    [Fact]
    public void Cosine_of_identical_orthogonal_and_opposite_vectors()
    {
        VectorMath.Cosine([1, 2, 3], [1, 2, 3]).Should().BeApproximately(1, 1e-9);
        VectorMath.Cosine([1, 0], [0, 1]).Should().BeApproximately(0, 1e-9);
        VectorMath.Cosine([1, 0], [-1, 0]).Should().BeApproximately(-1, 1e-9);
    }

    [Fact]
    public void Cosine_of_zero_vector_is_zero_not_NaN() =>
        VectorMath.Cosine([0, 0], [1, 1]).Should().Be(0);

    [Fact]
    public void Cosine_index_returns_top_k_in_order()
    {
        var index = new CosineIndex();
        index.Add(1, [1, 0]);
        index.Add(2, [0.9f, 0.1f]);
        index.Add(3, [0, 1]);

        var hits = index.Search([1, 0], k: 2);

        hits.Select(h => h.ChunkId).Should().Equal(1, 2);
    }

    [Theory]
    [InlineData(4, 8)]
    [InlineData(8, 12)]
    [InlineData(16, 16)]
    public void Lsh_always_finds_an_exact_duplicate(int tables, int bits)
    {
        var data = SyntheticVectors.Clustered(count: 2000, dims: 64, seed: 1);
        var lsh = new LshIndex(64, tables, bits);
        for (int i = 0; i < data.Length; i++) lsh.Add(i, data[i]);

        // An identical vector hashes to the same bucket in every table, so it is always found.
        foreach (var i in new[] { 0, 500, 1999 })
            lsh.Search(data[i], 1).Single().ChunkId.Should().Be(i);
    }

    [Fact]
    public void Lsh_keeps_high_recall_on_clustered_data()
    {
        const int dims = 64;
        var data = SyntheticVectors.Clustered(count: 3000, dims, seed: 7);
        var queries = SyntheticVectors.NoisyCopies(data, count: 50, noise: 0.05, seed: 8);
        var exact = new CosineIndex();
        var lsh = new LshIndex(dims, tables: 8, bits: 8);
        for (int i = 0; i < data.Length; i++) { exact.Add(i, data[i]); lsh.Add(i, data[i]); }

        var recall = SyntheticVectors.RecallAtK(exact, lsh, queries, k: 5);

        recall.Should().BeGreaterThan(0.8);
    }

    [Fact]
    public void Lsh_rejects_wrong_dimensions() =>
        FluentActions.Invoking(() => new LshIndex(dims: 4).Add(1, [1, 2, 3]))
            .Should().Throw<ArgumentException>();
}
