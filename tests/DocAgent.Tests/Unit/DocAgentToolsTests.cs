using System.Text.Json;
using DocAgent.Core.Agent;
using DocAgent.Core.Documents;
using DocAgent.Core.Search;
using DocAgent.Tests.Fakes;
using DocAgent.Tests.Support;
using FluentAssertions;

namespace DocAgent.Tests.Unit;

public class DocAgentToolsTests : IDisposable
{
    private readonly TestDb _testDb = new();
    private readonly FakeEmbeddingService _embeddings = new();
    private readonly CosineIndex _index = new();
    private static readonly HashSet<string> Apollo = ["Apollo"];

    public DocAgentToolsTests()
    {
        using var db = _testDb.Create();
        var docs = new DocumentService(db, _embeddings, _index);
        docs.AddAsync(new("Contract_Acme.txt", "Apollo", "Acme", "Contract", "The Acme contract renews on 1 March 2027.")).GetAwaiter().GetResult();
        docs.AddAsync(new("Secret_Plan.txt", "Zeus", "Acme", "Plan", "The Acme budget for Zeus is 4 million euros.")).GetAwaiter().GetResult();
    }

    private async Task<string> Run(string tool, string args, IReadOnlySet<string>? allowed = null)
    {
        using var db = _testDb.Create();
        return await new DocAgentTools(_embeddings, _index, db).RunAsync(tool, args, allowed ?? Apollo);
    }

    [Fact]
    public async Task Search_only_returns_chunks_from_allowed_projects()
    {
        var result = await Run("search_documents", """{"query":"Acme"}""");
        result.Should().Contain("Contract_Acme.txt").And.NotContain("Secret_Plan.txt");
    }

    [Fact]
    public async Task List_filters_by_metadata_within_allowed_projects()
    {
        var all = await Run("list_documents", "{}", new HashSet<string> { "Apollo", "Zeus" });
        var plans = await Run("list_documents", """{"type":"Plan"}""", new HashSet<string> { "Apollo", "Zeus" });

        JsonDocument.Parse(all).RootElement.GetArrayLength().Should().Be(2);
        plans.Should().Contain("Secret_Plan.txt").And.NotContain("Contract_Acme.txt");
    }

    [Theory]   // what qwen2.5:3b actually sent in a real run
    [InlineData("""{"project":"","customer":"","type":""}""")]
    [InlineData("""{"type":"contract"}""")]
    [InlineData("""{"customer":" acme "}""")]
    public async Task List_ignores_blank_filters_and_case(string args)
    {
        var result = await Run("list_documents", args);
        result.Should().Contain("Contract_Acme.txt");
    }

    [Fact]
    public async Task Case_insensitive_filters_do_not_widen_access()
    {
        (await Run("list_documents", """{"project":"zeus"}""")).Should().Be("[]");
        (await Run("search_documents", """{"query":"Acme budget","project":"ZEUS"}""")).Should().Be("[]");
    }

    [Fact]
    public async Task Read_returns_content_for_allowed_and_refuses_others()
    {
        (await Run("read_document", """{"id":1}""")).Should().Contain("renews on 1 March 2027");
        (await Run("read_document", """{"id":2}""")).Should().Contain("no access");
    }

    [Fact]
    public async Task No_allowed_projects_means_no_data()
    {
        (await Run("list_documents", "{}", new HashSet<string>())).Should().Be("[]");
    }

    [Theory]
    [InlineData("read_document", """{"id":"1"}""", "id must be an integer")]
    [InlineData("read_document", "not json", "not valid JSON")]
    [InlineData("search_documents", "{}", "query is required")]
    [InlineData("delete_everything", "{}", "unknown tool")]
    public async Task Bad_model_input_returns_an_error_instead_of_throwing(string tool, string args, string expected) =>
        (await Run(tool, args)).Should().Contain(expected);

    public void Dispose() => _testDb.Dispose();
}
