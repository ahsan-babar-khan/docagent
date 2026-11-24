using System.ClientModel;
using Azure;
using Azure.AI.OpenAI;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Embeddings;

namespace DocAgent.Core.Ai;

/// <summary>
/// Builds the chat and embedding clients for any OpenAI-compatible provider. This is the only place that knows which one:
/// <list type="bullet">
/// <item>Ollama (free, local):  Endpoint = http://localhost:11434/v1, no key needed</item>
/// <item>OpenAI:                Endpoint empty, Key = sk-...</item>
/// <item>Azure OpenAI:          Endpoint = https://&lt;resource&gt;.openai.azure.com/, Key, models = deployment names</item>
/// <item>Any other OpenAI-compatible server: its base URL (+ key if it needs one)</item>
/// </list>
/// </summary>
public class OpenAiClientFactory
{
    private readonly AzureOpenAIClient? _azure;
    private readonly OpenAIClient? _openAi;

    public string Provider { get; }
    public string ChatModel { get; }
    public string EmbeddingModel { get; }

    public OpenAiClientFactory(string? endpoint, string? key, string chatModel, string embeddingModel)
    {
        (ChatModel, EmbeddingModel) = (chatModel, embeddingModel);
        bool hasEndpoint = !string.IsNullOrWhiteSpace(endpoint), hasKey = !string.IsNullOrWhiteSpace(key);

        if (!hasEndpoint && !hasKey)
            throw new AiNotConfiguredException(
                "No AI provider configured. Set AI:Endpoint (e.g. http://localhost:11434/v1 for Ollama) " +
                "and/or AI:Key with dotnet user-secrets. See README → Run it locally.");

        if (hasEndpoint && IsAzure(new Uri(endpoint!)))
        {
            if (!hasKey) throw new AiNotConfiguredException("Azure OpenAI needs AI:Key as well as AI:Endpoint.");
            _azure = new AzureOpenAIClient(new Uri(endpoint!), new AzureKeyCredential(key!));
            Provider = "azure-openai";
        }
        else
        {
            // Local servers such as Ollama ignore the key, but the client requires a non-empty one.
            var options = hasEndpoint ? new OpenAIClientOptions { Endpoint = new Uri(endpoint!) } : new OpenAIClientOptions();
            _openAi = new OpenAIClient(new ApiKeyCredential(hasKey ? key! : "not-needed"), options);
            Provider = hasEndpoint ? $"openai-compatible ({new Uri(endpoint!).Host})" : "openai";
        }
    }

    public static bool IsAzure(Uri endpoint) =>
        endpoint.Host.EndsWith(".openai.azure.com", StringComparison.OrdinalIgnoreCase) ||
        endpoint.Host.EndsWith(".cognitiveservices.azure.com", StringComparison.OrdinalIgnoreCase);

    public ChatClient Chat() => _azure?.GetChatClient(ChatModel) ?? _openAi!.GetChatClient(ChatModel);
    public EmbeddingClient Embeddings() => _azure?.GetEmbeddingClient(EmbeddingModel) ?? _openAi!.GetEmbeddingClient(EmbeddingModel);
}
