namespace DocAgent.Core.Ai;

public interface IChatService
{
    Task<string> CompleteJsonAsync(string system, string user, CancellationToken ct = default);
}
