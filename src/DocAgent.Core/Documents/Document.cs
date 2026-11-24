namespace DocAgent.Core.Documents;

public class Document
{
    public int Id { get; set; }
    public required string FileName { get; set; }
    public required string Project { get; set; }
    public required string Customer { get; set; }
    public required string Type { get; set; }   // Contract, Memo, Invoice...
    public required string Content { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public List<Chunk> Chunks { get; set; } = new();
}

public class Chunk
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public Document Document { get; set; } = null!;
    public int Order { get; set; }
    public required string Text { get; set; }
    public float[] Embedding { get; set; } = Array.Empty<float>(); // EF Core 8 stores this as JSON
}

public record UploadRequest(string FileName, string Project, string Customer, string Type, string Content);
