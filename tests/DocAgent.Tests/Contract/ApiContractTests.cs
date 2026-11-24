using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocAgent.Tests.Support;
using FluentAssertions;

namespace DocAgent.Tests.Contract;

/// <summary>
/// Step 8.4: the JSON shape of each endpoint is a contract with clients.
/// If someone renames or removes a field, these tests break before a client does.
/// </summary>
public class ApiContractTests : IDisposable
{
    private readonly ApiContext _api = new();

    private static string[] Fields(JsonElement e) => e.EnumerateObject().Select(p => p.Name).ToArray();

    private Task<HttpResponseMessage> Upload(string content = "The Acme contract renews on 1 March 2027.") =>
        _api.Client.PostAsJsonAsync("/documents",
            new { fileName = "Contract_Acme.txt", project = "Apollo", customer = "Acme", type = "Contract", content });

    [Fact]
    public async Task Post_documents_returns_exactly_the_agreed_fields()
    {
        var response = await Upload();

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location!.ToString().Should().MatchRegex(@"^/documents/\d+$");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Fields(json).Should().BeEquivalentTo("id", "fileName", "project", "customer", "type", "chunks");
    }

    [Fact]
    public async Task Get_documents_returns_list_items_with_the_agreed_fields()
    {
        await Upload();

        var json = await _api.Client.GetFromJsonAsync<JsonElement>("/documents?customer=Acme");

        Fields(json[0]).Should().BeEquivalentTo("id", "fileName", "project", "customer", "type", "uploadedAt");
    }

    [Fact]
    public async Task Post_ask_returns_text_and_citations()
    {
        await Upload();

        var json = await (await _api.Client.PostAsJsonAsync("/ask", new { question = "When does the Acme contract renew?" }))
            .Content.ReadFromJsonAsync<JsonElement>();

        Fields(json).Should().BeEquivalentTo("text", "citations");
        json.GetProperty("citations").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Oversized_upload_is_rejected_with_400()
    {
        var response = await Upload(new string('x', 200 * 1024 + 1));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Overlong_question_is_rejected_with_400()
    {
        var response = await _api.Client.PostAsJsonAsync("/ask", new { question = new string('q', 1001) });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    public void Dispose() => _api.Dispose();
}
