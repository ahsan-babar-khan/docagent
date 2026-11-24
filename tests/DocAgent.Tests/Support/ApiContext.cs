using DocAgent.Core.Ai;
using DocAgent.Core.Search;
using DocAgent.Tests.Fakes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace DocAgent.Tests.Support;

/// <summary>
/// Starts the real API in memory with the AI services replaced by deterministic fakes and a fresh SQLite file.
/// Reqnroll creates one per scenario and injects it into the step classes; it is disposed after the scenario.
/// </summary>
public class ApiContext : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _dbFile = Path.Combine(Path.GetTempPath(), $"docagent-test-{Guid.NewGuid():N}.db");

    public HttpClient Client { get; }
    public IServiceProvider Services => _factory.Services;
    public HttpResponseMessage? LastResponse { get; set; }

    public ApiContext()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Db", $"Data Source={_dbFile}");
            b.ConfigureTestServices(s =>
            {
                // The last registration of a service wins, so these replace the real ones.
                s.AddSingleton<IEmbeddingService, FakeEmbeddingService>();
                s.AddSingleton<IChatService, FakeChatService>();
                s.AddSingleton<IVectorIndex, CosineIndex>();
            });
        });
        Client = _factory.CreateClient();
    }

    public void Dispose()
    {
        Client.Dispose();
        _factory.Dispose();
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbFile); } catch (IOException) { /* best effort */ }
    }
}
