using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DocAgent.Core.Ai;
using DocAgent.Core.Documents;
using DocAgent.Core.Rag;
using DocAgent.Core.Search;
using DocAgent.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace DocAgent.Tests.Eval;

/// <summary>
/// Step 8.5: quality evaluation against the REAL model. Costs a few cents per run, so it is excluded from CI
/// and only does work when an AI provider is set via environment variables:
///   AI_ENDPOINT, AI_KEY, AI_CHAT_MODEL, AI_EMBEDDING_MODEL  (same meaning as the "AI" config section)
///   Free and local with Ollama: AI_ENDPOINT=http://localhost:11434/v1 AI_CHAT_MODEL=qwen2.5:7b AI_EMBEDDING_MODEL=nomic-embed-text
///   dotnet test --filter Category=Eval --logger "console;verbosity=detailed"
/// Reports citation accuracy, refusal accuracy and latency for several MinScore values.
/// The table is also written to TestResults/eval.md. Change the prompt or the model, rerun, compare.
/// </summary>
[Trait("Category", "Eval")]
public class AnswerEvaluationTests(ITestOutputHelper output)
{
    private record EvalCase(string Question, string? ExpectFile);

    [Fact]
    public async Task Evaluate_answers_against_the_question_set()
    {
        var factory = CreateFactory();
        if (factory is null)
        {
            output.WriteLine("Skipped: set AI_ENDPOINT and/or AI_KEY (plus AI_CHAT_MODEL / AI_EMBEDDING_MODEL) to run the evaluation.");
            return;
        }

        var baseDir = Path.Combine(AppContext.BaseDirectory, "Eval");
        var cases = JsonSerializer.Deserialize<List<EvalCase>>(
            await File.ReadAllTextAsync(Path.Combine(baseDir, "questions.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var embeddings = new CachingEmbeddingService(new OpenAiEmbeddingService(factory.Embeddings(), factory.EmbeddingModel));
        var chat = new OpenAiChatService(factory.Chat(), factory.ChatModel, NullLogger<OpenAiChatService>.Instance);
        var index = new CosineIndex();
        using var testDb = new TestDb();

        using (var db = testDb.Create())
        {
            var docs = new DocumentService(db, embeddings, index);
            foreach (var file in Directory.GetFiles(Path.Combine(baseDir, "docs"), "*.txt"))
                await docs.AddAsync(new UploadRequest(Path.GetFileName(file), "Eval", "Various", "Sample", await File.ReadAllTextAsync(file)));
        }

        var report = new StringBuilder()
            .AppendLine($"Provider: {factory.Provider}, chat: {factory.ChatModel}, embeddings: {factory.EmbeddingModel}. {cases.Count(c => c.ExpectFile != null)} answerable + {cases.Count(c => c.ExpectFile == null)} unanswerable questions.")
            .AppendLine()
            .AppendLine("| MinScore | Citation accuracy | Refusal accuracy | False refusals | Avg latency (ms) |")
            .AppendLine("|---:|---:|---:|---:|---:|");

        foreach (var minScore in new[] { 0.20, 0.30, 0.40 })
        {
            int citedRight = 0, refusedRight = 0, falseRefusals = 0;
            var latencies = new List<long>();
            foreach (var c in cases)
            {
                using var db = testDb.Create();
                var rag = new AnswerService(embeddings, index, db, chat) { MinScore = minScore };
                var sw = Stopwatch.StartNew();
                var answer = await rag.AskAsync(c.Question);
                latencies.Add(sw.ElapsedMilliseconds);

                if (c.ExpectFile is null) { if (answer.Citations.Count == 0) refusedRight++; }
                else if (answer.Citations.Contains(c.ExpectFile)) citedRight++;
                else if (answer.Citations.Count == 0) falseRefusals++;

                output.WriteLine($"[{minScore:F2}] {c.Question} -> [{string.Join(", ", answer.Citations)}] {answer.Text}");
            }
            int answerable = cases.Count(c => c.ExpectFile != null), unanswerable = cases.Count - answerable;
            report.AppendLine($"| {minScore:F2} | {citedRight}/{answerable} ({(double)citedRight / answerable:P0}) " +
                              $"| {refusedRight}/{unanswerable} ({(double)refusedRight / unanswerable:P0}) " +
                              $"| {falseRefusals} | {latencies.Average():F0} |");
        }

        output.WriteLine(report.ToString());
        Directory.CreateDirectory("TestResults");
        await File.WriteAllTextAsync(Path.Combine("TestResults", "eval.md"), report.ToString());
    }

    private static OpenAiClientFactory? CreateFactory()
    {
        string? Env(string name) => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)) ? null : Environment.GetEnvironmentVariable(name);
        var endpoint = Env("AI_ENDPOINT");
        var key = Env("AI_KEY");
        if (endpoint is null && key is null) return null;
        return new OpenAiClientFactory(endpoint, key, Env("AI_CHAT_MODEL") ?? "gpt-4o-mini", Env("AI_EMBEDDING_MODEL") ?? "text-embedding-3-small");
    }

    /// <summary>Each question is embedded once, not once per MinScore setting.</summary>
    private sealed class CachingEmbeddingService(IEmbeddingService inner) : IEmbeddingService
    {
        private readonly ConcurrentDictionary<string, float[]> _cache = new();

        public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default) =>
            _cache.TryGetValue(text, out var v) ? v : _cache[text] = await inner.EmbedAsync(text, ct);
    }
}
