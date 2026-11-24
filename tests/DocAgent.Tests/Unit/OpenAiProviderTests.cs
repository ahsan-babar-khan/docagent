using System.Collections.Concurrent;
using System.Text.Json;
using DocAgent.Core.Ai;
using DocAgent.Core.Search;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DocAgent.Tests.Unit;

/// <summary>
/// Provider selection, plus the real OpenAI client classes talking to a tiny fake OpenAI-compatible server
/// (the same API shape Ollama exposes at http://localhost:11434/v1). Proves base-URL handling, request shape
/// (model names, JSON mode) and response parsing without any key or network access.
/// </summary>
public class OpenAiProviderTests : IAsyncLifetime
{
    private WebApplication _server = null!;
    private string _baseUrl = "";
    private readonly ConcurrentQueue<(string Path, JsonElement Body)> _requests = new();

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        _server = builder.Build();

        _server.MapPost("/v1/embeddings", async (HttpContext ctx) =>
        {
            _requests.Enqueue(("/v1/embeddings", await JsonSerializer.DeserializeAsync<JsonElement>(ctx.Request.Body)));
            return Results.Text("""
                {"object":"list","model":"nomic-embed-text",
                 "data":[{"object":"embedding","index":0,"embedding":[0.25,-0.5,0.75]}],
                 "usage":{"prompt_tokens":3,"total_tokens":3}}
                """, "application/json");
        });
        _server.MapPost("/v1/chat/completions", async (HttpContext ctx) =>
        {
            _requests.Enqueue(("/v1/chat/completions", await JsonSerializer.DeserializeAsync<JsonElement>(ctx.Request.Body)));
            return Results.Text("""
                {"id":"chatcmpl-1","object":"chat.completion","created":1764000000,"model":"qwen2.5:7b",
                 "choices":[{"index":0,"finish_reason":"stop",
                   "message":{"role":"assistant","content":"{\"answer\":\"1 March 2027\",\"citations\":[\"Contract_Acme.txt\"]}"}}],
                 "usage":{"prompt_tokens":40,"completion_tokens":12,"total_tokens":52}}
                """, "application/json");
        });

        await _server.StartAsync();
        _baseUrl = _server.Urls.First() + "/v1";
    }

    public async Task DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    public async Task Embeddings_go_to_the_custom_endpoint_with_the_configured_model()
    {
        var factory = new OpenAiClientFactory(_baseUrl, key: null, "qwen2.5:7b", "nomic-embed-text");

        var vector = await new OpenAiEmbeddingService(factory.Embeddings(), factory.EmbeddingModel).EmbedAsync("The Acme contract renews.");

        vector.Should().Equal(0.25f, -0.5f, 0.75f);
        var (path, body) = _requests.Single();
        path.Should().Be("/v1/embeddings");
        body.GetProperty("model").GetString().Should().Be("nomic-embed-text");
        body.GetProperty("encoding_format").GetString().Should().Be("float");
    }

    [Fact]
    public void Base64_embeddings_are_parsed_too()
    {
        var floats = new[] { 0.25f, -0.5f, 0.75f };
        var base64 = Convert.ToBase64String(floats.SelectMany(BitConverter.GetBytes).ToArray());

        OpenAiEmbeddingService.Parse(JsonDocument.Parse($"\"{base64}\"").RootElement).Should().Equal(floats);
    }

    [Fact]
    public async Task Chat_uses_json_mode_and_returns_the_message_content()
    {
        var factory = new OpenAiClientFactory(_baseUrl, key: null, "qwen2.5:7b", "nomic-embed-text");
        var chat = new OpenAiChatService(factory.Chat(), factory.ChatModel, NullLogger<OpenAiChatService>.Instance);

        var json = await chat.CompleteJsonAsync("system prompt", "user prompt");

        JsonDocument.Parse(json).RootElement.GetProperty("citations")[0].GetString().Should().Be("Contract_Acme.txt");
        var (path, body) = _requests.Single();
        path.Should().Be("/v1/chat/completions");
        body.GetProperty("model").GetString().Should().Be("qwen2.5:7b");
        body.GetProperty("response_format").GetProperty("type").GetString().Should().Be("json_object");
    }

    [Fact]
    public void No_endpoint_and_no_key_is_reported_as_not_configured() =>
        FluentActions.Invoking(() => new OpenAiClientFactory(null, "", "m", "e"))
            .Should().Throw<AiNotConfiguredException>();

    [Theory]
    [InlineData("http://localhost:11434/v1", null, "openai-compatible (localhost)")]
    [InlineData(null, "sk-test", "openai")]
    [InlineData("https://my-res.openai.azure.com/", "key", "azure-openai")]
    public void Provider_is_chosen_from_the_endpoint(string? endpoint, string? key, string expected) =>
        new OpenAiClientFactory(endpoint, key, "m", "e").Provider.Should().Be(expected);

    [Fact]
    public void Azure_without_a_key_is_reported_as_not_configured() =>
        FluentActions.Invoking(() => new OpenAiClientFactory("https://my-res.openai.azure.com/", null, "m", "e"))
            .Should().Throw<AiNotConfiguredException>();

    [Fact]
    public void Lsh_index_takes_its_size_from_the_first_vector()
    {
        var lsh = new LshIndex(tables: 4, bits: 8);
        lsh.Search([1, 0, 0], 5).Should().BeEmpty("nothing is indexed yet");

        lsh.Add(1, [1, 0, 0]);

        lsh.Dims.Should().Be(3);
        lsh.Search([1, 0, 0], 1).Single().ChunkId.Should().Be(1);
        FluentActions.Invoking(() => lsh.Add(2, [1, 0])).Should().Throw<ArgumentException>();
    }
}
