using Microsoft.EntityFrameworkCore;

namespace DocAgent.Core.Documents;

public class DocAgentDb(DbContextOptions<DocAgentDb> options) : DbContext(options)
{
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<Chunk> Chunks => Set<Chunk>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Document>().HasIndex(d => d.Project);
        b.Entity<Document>().HasIndex(d => d.Customer);
    }
}
