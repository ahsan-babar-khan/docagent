using System.ClientModel;
using Azure;
using Azure.AI.OpenAI;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Embeddings;

namespace DocAgent.Core.Ai;

/// <summary>
/// Builds the chat and embedding clients from configuration values.
/// Azure OpenAI is used when an endpoint is given; otherwise falls back to the public OpenAI API.
/// This is the only place that knows which provider is used.
/// </summary>
public class OpenAiClientFactory
{
    private readonly AzureOpenAIClient? _azure;
    private readonly OpenAIClient? _openAi;
    private readonly string _chatModel, _embeddingModel;

    public OpenAiClientFactory(string? endpoint, string? key, string chatModel, string embeddingModel)
    {
        (_chatModel, _embeddingModel) = (chatModel, embeddingModel);
        if (string.IsNullOrWhiteSpace(key))
            throw new AiNotConfiguredException(
                "No AI key configured. Set AzureOpenAI:Key (and AzureOpenAI:Endpoint) with dotnet user-secrets, or OpenAI:Key for the OpenAI fallback.");

        if (!string.IsNullOrWhiteSpace(endpoint))
            _azure = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(key));
        else
            _openAi = new OpenAIClient(new ApiKeyCredential(key));
    }

    public string ChatModel => _chatModel;
    public ChatClient Chat() => _azure?.GetChatClient(_chatModel) ?? _openAi!.GetChatClient(_chatModel);
    public EmbeddingClient Embeddings() => _azure?.GetEmbeddingClient(_embeddingModel) ?? _openAi!.GetEmbeddingClient(_embeddingModel);
}
