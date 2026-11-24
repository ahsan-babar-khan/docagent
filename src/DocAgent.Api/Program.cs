using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.RateLimiting;
using DocAgent.Core.Agent;
using DocAgent.Core.Ai;
using DocAgent.Core.Documents;
using DocAgent.Core.Rag;
using DocAgent.Core.Search;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---- Observability (Step 11.1): only when Terraform/App Service provides a connection string
if (!string.IsNullOrEmpty(config["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
    builder.Services.AddOpenTelemetry().UseAzureMonitor();

// ---- Storage
builder.Services.AddDbContext<DocAgentDb>(o =>
    o.UseSqlite(config.GetConnectionString("Db") ?? "Data Source=docagent.db"));

// ---- AI clients (Step 4 + 6). Resolved lazily, so the app starts without keys; only AI calls need them.
builder.Services.AddSingleton(_ =>
{
    var azure = config.GetSection("AzureOpenAI");
    var openAiKey = config["OpenAI:Key"];
    return string.IsNullOrWhiteSpace(azure["Key"]) && !string.IsNullOrWhiteSpace(openAiKey)
        ? new OpenAiClientFactory(null, openAiKey, config["OpenAI:ChatModel"] ?? "gpt-4o-mini",
                                  config["OpenAI:EmbeddingModel"] ?? "text-embedding-3-small")
        : new OpenAiClientFactory(azure["Endpoint"], azure["Key"], azure["ChatDeployment"] ?? "gpt-4o-mini",
                                  azure["EmbeddingDeployment"] ?? "text-embedding-3-small");
});
builder.Services.AddSingleton<IEmbeddingService>(sp =>
    new AzureOpenAiEmbeddingService(sp.GetRequiredService<OpenAiClientFactory>().Embeddings()));
builder.Services.AddSingleton(sp =>
{
    var f = sp.GetRequiredService<OpenAiClientFactory>();
    return new AzureOpenAiChatService(f.Chat(), f.ChatModel, sp.GetRequiredService<ILogger<AzureOpenAiChatService>>());
});
builder.Services.AddSingleton<IChatService>(sp => sp.GetRequiredService<AzureOpenAiChatService>());

// ---- Vector index (Step 5): exact cosine by default, LSH from the thesis with Search:Mode=lsh
builder.Services.AddSingleton<IVectorIndex>(_ =>
    config["Search:Mode"] == "lsh"
        ? new LshIndex(dims: config.GetValue("Search:Dims", 1536),
                       tables: config.GetValue("Search:Tables", 8),
                       bits: config.GetValue("Search:Bits", 12))
        : new CosineIndex());

// ---- Services
builder.Services.AddScoped<DocumentService>();
builder.Services.AddScoped(sp => new AnswerService(
    sp.GetRequiredService<IEmbeddingService>(), sp.GetRequiredService<IVectorIndex>(),
    sp.GetRequiredService<DocAgentDb>(), sp.GetRequiredService<IChatService>())
    { MinScore = config.GetValue("Rag:MinScore", AnswerService.DefaultMinScore) });
builder.Services.AddScoped<DocAgentTools>();
builder.Services.AddScoped(sp => new DocAgentRunner(
    sp.GetRequiredService<AzureOpenAiChatService>().Client, sp.GetRequiredService<DocAgentTools>()));

// ---- Limits (Step 11.3): AI endpoints cost money, so cap them
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddFixedWindowLimiter("ai", l =>
    {
        l.PermitLimit = config.GetValue("RateLimit:PermitPerMinute", 20);
        l.Window = TimeSpan.FromMinutes(1);
        l.QueueLimit = 0;
    });
});

var app = builder.Build();
app.UseRateLimiter();

// Create the database and reload every stored vector into the in-memory index
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DocAgentDb>();
    db.Database.EnsureCreated();
    var index = app.Services.GetRequiredService<IVectorIndex>();
    foreach (var c in db.Chunks.AsNoTracking()) index.Add(c.Id, c.Embedding);
}

app.MapGet("/", () => Results.Ok(new { name = "DocAgent", endpoints = new[] { "POST /documents", "GET /documents", "POST /ask", "POST /agent" } }));

app.MapPost("/documents", async (UploadRequest req, DocumentService docs, CancellationToken ct) =>
{
    var errors = Limits.ValidateUpload(req);
    if (errors.Count > 0) return Results.ValidationProblem(errors);

    var doc = await docs.AddAsync(req, ct);
    return Results.Created($"/documents/{doc.Id}",
        new { doc.Id, doc.FileName, doc.Project, doc.Customer, doc.Type, Chunks = doc.Chunks.Count });
});

app.MapGet("/documents", async (string? project, string? customer, string? type, DocAgentDb db) =>
    await db.Documents
        .Where(d => project == null || d.Project == project)
        .Where(d => customer == null || d.Customer == customer)
        .Where(d => type == null || d.Type == type)
        .Select(d => new { d.Id, d.FileName, d.Project, d.Customer, d.Type, d.UploadedAt })
        .ToListAsync());

app.MapGet("/documents/{id:int}", async (int id, DocAgentDb db) =>
    await db.Documents.Where(d => d.Id == id)
        .Select(d => new { d.Id, d.FileName, d.Project, d.Customer, d.Type, d.UploadedAt, d.Content })
        .FirstOrDefaultAsync() is { } doc ? Results.Ok(doc) : Results.NotFound());

app.MapPost("/ask", async (AskRequest req, AnswerService rag, CancellationToken ct) =>
{
    var error = Limits.ValidateQuestion(req.Question);
    if (error is not null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["question"] = [error] });
    return Results.Ok(await rag.AskAsync(req.Question, req.Project, ct));
}).RequireRateLimiting("ai");

app.MapPost("/agent", async (AskRequest req, HttpRequest http, DocAgentRunner agent, CancellationToken ct) =>
{
    var error = Limits.ValidateQuestion(req.Question);
    if (error is not null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["question"] = [error] });

    // Demo shortcut: allowed projects come from a header. In a real system they come from the user's token (Entra ID groups).
    var allowed = http.Headers["X-Allowed-Projects"].ToString()
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
    return Results.Ok(await agent.RunAsync(req.Question, allowed, ct));
}).RequireRateLimiting("ai");

app.Run();

public record AskRequest(string Question, string? Project);

public static class Limits
{
    public const int MaxContentChars = 200 * 1024;  // ~200 KB of text
    public const int MaxQuestionChars = 1000;

    public static Dictionary<string, string[]> ValidateUpload(UploadRequest req)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(req.FileName)) errors["fileName"] = ["File name is required."];
        if (string.IsNullOrWhiteSpace(req.Project)) errors["project"] = ["Project is required."];
        if (string.IsNullOrWhiteSpace(req.Content)) errors["content"] = ["Content is required."];
        else if (req.Content.Length > MaxContentChars) errors["content"] = [$"Content is larger than {MaxContentChars / 1024} KB."];
        return errors;
    }

    public static string? ValidateQuestion(string? q) =>
        string.IsNullOrWhiteSpace(q) ? "Question is required."
        : q.Length > MaxQuestionChars ? $"Question is longer than {MaxQuestionChars} characters."
        : null;
}

public partial class Program { } // lets the tests start the API in memory
