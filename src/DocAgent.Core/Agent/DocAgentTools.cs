using System.Text.Json;
using DocAgent.Core.Ai;
using DocAgent.Core.Documents;
using DocAgent.Core.Search;
using Microsoft.EntityFrameworkCore;
using OpenAI.Chat;

namespace DocAgent.Core.Agent;

/// <summary>
/// The three tools the agent can call. Access control lives here, not in the prompt:
/// every tool only returns documents from the caller's allowed projects, so even a tricked model cannot reach other data.
/// </summary>
public class DocAgentTools(IEmbeddingService embeddings, IVectorIndex index, DocAgentDb db)
{
    public static readonly ChatTool Search = ChatTool.CreateFunctionTool(
        "search_documents", "Semantic search over document chunks. Returns matching text with document id and file name.",
        BinaryData.FromString("""{"type":"object","properties":{"query":{"type":"string"},"project":{"type":"string"}},"required":["query"]}"""));

    public static readonly ChatTool List = ChatTool.CreateFunctionTool(
        "list_documents", "List documents by metadata (project, customer, type). All filters are optional. Newest first.",
        BinaryData.FromString("""{"type":"object","properties":{"project":{"type":"string"},"customer":{"type":"string"},"type":{"type":"string"}}}"""));

    public static readonly ChatTool Read = ChatTool.CreateFunctionTool(
        "read_document", "Read the full text of one document by id.",
        BinaryData.FromString("""{"type":"object","properties":{"id":{"type":"integer"}},"required":["id"]}"""));

    public static IEnumerable<ChatTool> All => [Search, List, Read];

    /// <summary>Runs one tool call by name. Always returns JSON, never throws on bad model input.</summary>
    public async Task<string> RunAsync(string name, string argumentsJson, IReadOnlySet<string> allowed, CancellationToken ct = default)
    {
        JsonElement args;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            args = doc.RootElement.Clone();
        }
        catch (JsonException) { return """{"error":"arguments are not valid JSON"}"""; }

        return name switch
        {
            "search_documents" => await SearchAsync(args, allowed, ct),
            "list_documents"   => await ListAsync(args, allowed, ct),
            "read_document"    => await ReadAsync(args, allowed, ct),
            _ => """{"error":"unknown tool"}"""
        };
    }

    /// <summary>A string argument, or null when missing or blank. Small models often send "" for filters they do not use.</summary>
    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString()!.Trim() : null;

    private async Task<string> SearchAsync(JsonElement a, IReadOnlySet<string> allowed, CancellationToken ct)
    {
        var query = Str(a, "query");
        if (string.IsNullOrWhiteSpace(query)) return """{"error":"query is required"}""";

        var hits = index.Search(await embeddings.EmbedAsync(query, ct), 20);
        var ids = hits.Select(h => h.ChunkId).ToList();
        var project = Str(a, "project");
        var allowedList = allowed.ToList();
        var rows = await db.Chunks
            .Where(c => ids.Contains(c.Id) && allowedList.Contains(c.Document.Project))   // access control
            .Where(c => project == null || c.Document.Project.ToLower() == project.ToLower())
            .Select(c => new { c.Id, DocumentId = c.Document.Id, c.Document.FileName, c.Document.Project, c.Text })
            .ToListAsync(ct);

        var ranked = rows.Join(hits, r => r.Id, h => h.ChunkId,
                               (r, h) => new { r.DocumentId, r.FileName, r.Project, r.Text, Score = Math.Round(h.Score, 3) })
                         .OrderByDescending(r => r.Score).Take(5);
        return JsonSerializer.Serialize(ranked);
    }

    private async Task<string> ListAsync(JsonElement a, IReadOnlySet<string> allowed, CancellationToken ct)
    {
        string? p = Str(a, "project"), c = Str(a, "customer"), t = Str(a, "type");
        var allowedList = allowed.ToList();
        var rows = await db.Documents
            .Where(d => allowedList.Contains(d.Project))                                   // access control
            // Filters ignore case ("contract" finds "Contract"). Access control above stays an exact match.
            .Where(d => (p == null || d.Project.ToLower() == p.ToLower())
                     && (c == null || d.Customer.ToLower() == c.ToLower())
                     && (t == null || d.Type.ToLower() == t.ToLower()))
            .OrderByDescending(d => d.UploadedAt)
            .Select(d => new { d.Id, d.FileName, d.Project, d.Customer, d.Type, d.UploadedAt })
            .ToListAsync(ct);
        return JsonSerializer.Serialize(rows);
    }

    private async Task<string> ReadAsync(JsonElement a, IReadOnlySet<string> allowed, CancellationToken ct)
    {
        if (a.ValueKind != JsonValueKind.Object || !a.TryGetProperty("id", out var idEl)
            || idEl.ValueKind != JsonValueKind.Number || !idEl.TryGetInt32(out var id))
            return """{"error":"id must be an integer"}""";

        var allowedList = allowed.ToList();
        var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id && allowedList.Contains(d.Project), ct); // access control
        return doc is null ? """{"error":"not found or no access"}"""
                           : JsonSerializer.Serialize(new { doc.Id, doc.FileName, doc.Project, doc.Content });
    }
}
