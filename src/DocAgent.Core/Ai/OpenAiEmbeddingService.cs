using System.ClientModel;
using System.ClientModel.Primitives;
using System.Runtime.InteropServices;
using System.Text.Json;
using OpenAI.Embeddings;

namespace DocAgent.Core.Ai;

/// <summary>
/// Embeddings from any OpenAI-compatible server. Uses the SDK's protocol method instead of
/// <c>GenerateEmbeddingAsync</c>: the convenience method always asks for base64 and can only parse base64, which breaks
/// with servers that return plain float arrays. Here the request asks for <c>"encoding_format": "float"</c> (standard
/// OpenAI API) and the response is parsed in either format.
/// </summary>
public class OpenAiEmbeddingService(EmbeddingClient client, string model) : IEmbeddingService
{
    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var request = BinaryContent.Create(BinaryData.FromObjectAsJson(new { model, input = text, encoding_format = "float" }));
        ClientResult result = await client.GenerateEmbeddingsAsync(request, new RequestOptions { CancellationToken = ct });

        using var json = JsonDocument.Parse(result.GetRawResponse().Content);
        var embedding = json.RootElement.GetProperty("data")[0].GetProperty("embedding");
        return Parse(embedding);
    }

    internal static float[] Parse(JsonElement embedding) => embedding.ValueKind switch
    {
        JsonValueKind.Array => embedding.EnumerateArray().Select(e => e.GetSingle()).ToArray(),
        JsonValueKind.String => MemoryMarshal.Cast<byte, float>(Convert.FromBase64String(embedding.GetString()!)).ToArray(),
        _ => throw new FormatException($"Unexpected embedding format: {embedding.ValueKind}.")
    };
}
