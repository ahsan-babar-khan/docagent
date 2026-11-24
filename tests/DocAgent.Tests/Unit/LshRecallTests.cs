using System.Diagnostics;
using System.Text;
using DocAgent.Core.Search;
using FluentAssertions;
using Xunit.Abstractions;

namespace DocAgent.Tests.Unit;

/// <summary>
/// Step 5.5: the thesis, measured. 20,000 vectors of 256 dimensions, 100 queries, recall@5 and time per query
/// for exact search vs LSH at several (tables, bits) settings. Slow, so it is excluded from CI:
///   dotnet test --filter Category=Benchmark --logger "console;verbosity=detailed"
/// The table is also written to TestResults/lsh-benchmark.md.
/// </summary>
[Trait("Category", "Benchmark")]
public class LshRecallTests(ITestOutputHelper output)
{
    private const int Count = 20_000, Dims = 256, Queries = 100, K = 5;

    [Fact]
    public void Recall_and_speed_of_lsh_against_exact_search()
    {
        var data = SyntheticVectors.Clustered(Count, Dims, seed: 42, clusters: 200);
        var queries = SyntheticVectors.NoisyCopies(data, Queries, noise: 0.05, seed: 43);

        var exact = new CosineIndex();
        for (int i = 0; i < data.Length; i++) exact.Add(i, data[i]);
        exact.Search(queries[0], K); // warm up the JIT
        var exactMs = TimePerQuery(exact, queries);

        var report = new StringBuilder()
            .AppendLine($"Data: {Count:N0} clustered vectors × {Dims} dims, {Queries} queries, k = {K}. Exact search: {exactMs:F2} ms/query.")
            .AppendLine()
            .AppendLine("| Tables | Bits | Recall@5 | ms / query | Speed-up vs exact |")
            .AppendLine("|---:|---:|---:|---:|---:|");

        foreach (var tables in new[] { 4, 8, 16 })
        foreach (var bits in new[] { 8, 12, 16 })
        {
            var lsh = new LshIndex(Dims, tables, bits, seed: 1);
            for (int i = 0; i < data.Length; i++) lsh.Add(i, data[i]);
            lsh.Search(queries[0], K);

            var recall = SyntheticVectors.RecallAtK(exact, lsh, queries, K);
            var ms = TimePerQuery(lsh, queries);
            report.AppendLine($"| {tables} | {bits} | {recall:P0} | {ms:F2} | {exactMs / ms:F1}× |");
            recall.Should().BeInRange(0, 1);
        }

        output.WriteLine(report.ToString());
        Directory.CreateDirectory("TestResults");
        File.WriteAllText(Path.Combine("TestResults", "lsh-benchmark.md"), report.ToString());
    }

    private static double TimePerQuery(IVectorIndex index, float[][] queries)
    {
        var sw = Stopwatch.StartNew();
        foreach (var q in queries) index.Search(q, K);
        return sw.Elapsed.TotalMilliseconds / queries.Length;
    }
}
