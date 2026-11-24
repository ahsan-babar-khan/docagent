using DocAgent.Core.Documents;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DocAgent.Tests.Support;

/// <summary>An in-memory SQLite database that lives as long as this object (the connection stays open).</summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public TestDb()
    {
        _connection.Open();
        using var db = Create();
        db.Database.EnsureCreated();
    }

    public DocAgentDb Create() =>
        new(new DbContextOptionsBuilder<DocAgentDb>().UseSqlite(_connection).Options);

    public void Dispose() => _connection.Dispose();
}
