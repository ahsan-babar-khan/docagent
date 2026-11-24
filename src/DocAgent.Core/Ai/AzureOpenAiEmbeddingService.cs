using OpenAI.Embeddings;

namespace DocAgent.Core.Ai;

public class AzureOpenAiEmbeddingService(EmbeddingClient client) : IEmbeddingService
{
    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        OpenAIEmbedding embedding = (await client.GenerateEmbeddingAsync(text, cancellationToken: ct)).Value;
        return embedding.ToFloats().ToArray();
    }
}
