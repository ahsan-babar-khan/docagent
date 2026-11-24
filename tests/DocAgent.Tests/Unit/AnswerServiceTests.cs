using DocAgent.Core.Documents;
using DocAgent.Core.Rag;
using DocAgent.Core.Search;
using DocAgent.Tests.Fakes;
using DocAgent.Tests.Support;
using FluentAssertions;

namespace DocAgent.Tests.Unit;

public class AnswerServiceTests : IDisposable
{
    private readonly TestDb _testDb = new();
    private readonly FakeEmbeddingService _embeddings = new();
    private readonly FakeChatService _chat = new();
    private readonly CosineIndex _index = new();

    private async Task AddAsync(string file, string project, string content)
    {
        using var db = _testDb.Create();
        await new DocumentService(db, _embeddings, _index).AddAsync(new UploadRequest(file, project, "Acme", "Contract", content));
    }

    private AnswerService Service(DocAgentDb db, double minScore = AnswerService.DefaultMinScore) =>
        new(_embeddings, _index, db, _chat) { MinScore = minScore };

    [Fact]
    public async Task Refuses_without_calling_the_llm_when_nothing_is_similar()
    {
        await AddAsync("Contract_Acme.txt", "Apollo", "The Acme contract renews on 1 March 2027.");
        using var db = _testDb.Create();

        var answer = await Service(db).AskAsync("Capital of Australia?");

        answer.Text.Should().Be(AnswerService.NotFound);
        answer.Citations.Should().BeEmpty();
        _chat.Calls.Should().Be(0, "no tokens are spent when nothing matches");
    }

    [Fact]
    public async Task Drops_citations_of_files_that_were_never_shown_to_the_model()
    {
        await AddAsync("Contract_Acme.txt", "Apollo", "The Acme contract renews on 1 March 2027.");
        _chat.ScriptedReply = """{"answer":"1 March 2027","citations":["Contract_Acme.txt","Invented.txt"]}""";
        using var db = _testDb.Create();

        var answer = await Service(db).AskAsync("When does the Acme contract renew?");

        answer.Citations.Should().Equal("Contract_Acme.txt");
    }

    [Theory]
    [InlineData("this is not json")]
    [InlineData("""{"citations":["Contract_Acme.txt"]}""")]
    [InlineData("null")]
    public async Task Fails_closed_on_malformed_model_output(string reply)
    {
        await AddAsync("Contract_Acme.txt", "Apollo", "The Acme contract renews on 1 March 2027.");
        _chat.ScriptedReply = reply;
        using var db = _testDb.Create();

        var answer = await Service(db).AskAsync("When does the Acme contract renew?");

        answer.Text.Should().Be(AnswerService.NotFound);
        answer.Citations.Should().BeEmpty();
    }

    [Fact]
    public async Task Project_filter_only_uses_chunks_from_that_project()
    {
        await AddAsync("Contract_Acme.txt", "Apollo", "The Acme contract renews on 1 March 2027.");
        await AddAsync("Contract_Acme_Zeus.txt", "Zeus", "The Acme contract renews on 1 March 2027.");
        using var db = _testDb.Create();

        await Service(db).AskAsync("When does the Acme contract renew?", project: "Zeus");

        _chat.LastUserPrompt.Should().Contain("Contract_Acme_Zeus.txt").And.NotContain("File: Contract_Acme.txt");
    }

    [Fact]
    public async Task Embeddings_survive_a_round_trip_through_the_database()
    {
        await AddAsync("Contract_Acme.txt", "Apollo", "The Acme contract renews on 1 March 2027.");
        using var db = _testDb.Create();

        var chunk = db.Chunks.Single();

        chunk.Embedding.Should().HaveCount(FakeEmbeddingService.Dims);
        chunk.Embedding.Should().Equal(await _embeddings.EmbedAsync(chunk.Text));
    }

    private static readonly string[] Sent = ["Contract_Acme.txt", "Apollo_Plan.txt"];

    [Theory]
    [InlineData("""{"answer":"x","citations":[1]}""", "Contract_Acme.txt")]                       // number (qwen2.5:3b does this)
    [InlineData("""{"answer":"x","citations":["[2]"]}""", "Apollo_Plan.txt")]                     // "[n]"
    [InlineData("""{"answer":"x","citations":["contract_acme.txt"]}""", "Contract_Acme.txt")]     // case
    [InlineData("""{"answer":"x","citations":["[1] File: Contract_Acme.txt"]}""", "Contract_Acme.txt")] // extra text
    [InlineData("""{"Answer":"x","Citations":"Apollo_Plan.txt"}""", "Apollo_Plan.txt")]           // single value, other casing
    public void Citations_are_mapped_to_the_sources_that_were_sent(string reply, string expected) =>
        AnswerService.Parse(reply, Sent).Citations.Should().Equal(expected);

    [Theory]
    [InlineData("""{"answer":"x","citations":[3]}""")]               // index out of range
    [InlineData("""{"answer":"x","citations":[0, -1]}""")]
    [InlineData("""{"answer":"x","citations":["Invented.txt"]}""")]  // never shown
    [InlineData("""{"answer":"x","citations":[""]}""")]
    [InlineData("""{"answer":"x","citations":[{"file":"Contract_Acme.txt"}]}""")]
    public void Citations_that_do_not_match_a_sent_source_are_dropped(string reply) =>
        AnswerService.Parse(reply, Sent).Citations.Should().BeEmpty();

    [Fact]
    public async Task Only_sources_above_the_threshold_are_shown_to_the_model()
    {
        await AddAsync("Contract_Acme.txt", "Apollo", "The Acme contract renews on 1 March 2027.");
        await AddAsync("Menu.txt", "Canteen", "Monday soup, Tuesday fish, Wednesday pasta.");
        using var db = _testDb.Create();

        await Service(db).AskAsync("When does the Acme contract renew?");

        _chat.LastUserPrompt.Should().Contain("Contract_Acme.txt").And.NotContain("Menu.txt");
    }

    public void Dispose() => _testDb.Dispose();
}
