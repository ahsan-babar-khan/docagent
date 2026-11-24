using System.Net.Http.Json;
using System.Text.Json;
using DocAgent.Core.Agent;
using DocAgent.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;

namespace DocAgent.Tests.Steps;

/// <summary>
/// Calls the agent's tools exactly as the agent loop would, as if a tricked model had asked for them.
/// The point: access is enforced by code in the tools, so no prompt can widen it.
/// </summary>
[Binding]
public class AgentAccessSteps(ApiContext api)
{
    private string _toolResult = "";

    private static HashSet<string> Projects(string csv) =>
        csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();

    private async Task<string> RunTool(string name, object args, string allowed)
    {
        using var scope = api.Services.CreateScope();
        var tools = scope.ServiceProvider.GetRequiredService<DocAgentTools>();
        return await tools.RunAsync(name, JsonSerializer.Serialize(args), Projects(allowed));
    }

    [When("the agent lists documents with allowed projects {string}")]
    public async Task ListDocuments(string allowed) =>
        _toolResult = await RunTool("list_documents", new { }, allowed);

    [When("the agent reads the document {string} with allowed projects {string}")]
    public async Task ReadDocument(string fileName, string allowed)
    {
        // Look up the real id through the public API, as an attacker who knows the id would.
        var all = await api.Client.GetFromJsonAsync<List<DocumentSteps.DocumentRow>>("/documents");
        var id = all!.Single(d => d.FileName == fileName).Id;
        _toolResult = await RunTool("read_document", new { id }, allowed);
    }

    [Then("the tool result does not mention {string}")]
    public void DoesNotMention(string text) => _toolResult.Should().NotContain(text);

    [Then("the tool result says {string}")]
    public void Says(string text) => _toolResult.Should().Contain(text);
}
