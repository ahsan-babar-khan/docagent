using System.Net;
using System.Net.Http.Json;
using DocAgent.Core.Ai;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace DocAgent.Tests.Contract;

/// <summary>
/// Regression: with no AI configured, a bad request must still get 400 (validation runs before AI services
/// are resolved), and a valid one gets a clear 503 instead of a 500 stack trace.
/// </summary>
public class ApiWithoutAiTests : IDisposable
{
    private readonly string _dbFile = Path.Combine(Path.GetTempPath(), $"docagent-noai-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ApiWithoutAiTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Db", $"Data Source={_dbFile}");
            b.ConfigureTestServices(s =>
                s.AddSingleton<OpenAiClientFactory>(_ => throw new AiNotConfiguredException("No AI key configured.")));
        });
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Invalid_upload_is_400_even_without_ai() =>
        (await _client.PostAsJsonAsync("/documents", new { fileName = "a.txt", project = "", customer = "Acme", type = "Memo", content = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

    [Theory]
    [InlineData("/ask")]
    [InlineData("/agent")]
    public async Task Valid_question_is_503_with_a_clear_message(string path)
    {
        var response = await _client.PostAsJsonAsync(path, new { question = "When does the Acme contract renew?" });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).Should().Contain("No AI key configured");
    }

    [Fact]
    public async Task Listing_documents_works_without_ai() =>
        (await _client.GetAsync("/documents")).StatusCode.Should().Be(HttpStatusCode.OK);

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbFile); } catch (IOException) { }
    }
}
