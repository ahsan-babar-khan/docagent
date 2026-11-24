using DocAgent.Core.Ai;
using DocAgent.Core.Search;

namespace DocAgent.Core.Documents;

public class DocumentService(DocAgentDb db, IEmbeddingService embeddings, IVectorIndex index)
{
    public async Task<Document> AddAsync(UploadRequest req, CancellationToken ct = default)
    {
        var doc = new Document
        {
            FileName = req.FileName, Project = req.Project,
            Customer = req.Customer ?? "", Type = req.Type ?? "", Content = req.Content
        };

        // One call per chunk keeps it simple; batching would cut cost and latency at scale.
        var order = 0;
        foreach (var text in Chunker.Split(req.Content))
            doc.Chunks.Add(new Chunk { Order = order++, Text = text, Embedding = await embeddings.EmbedAsync(text, ct) });

        db.Documents.Add(doc);
        await db.SaveChangesAsync(ct);
        foreach (var chunk in doc.Chunks) index.Add(chunk.Id, chunk.Embedding);
        return doc;
    }
}
