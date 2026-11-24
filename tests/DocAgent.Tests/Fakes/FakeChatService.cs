using System.Text.RegularExpressions;
using DocAgent.Core.Ai;

namespace DocAgent.Tests.Fakes;

/// <summary>Echoes the first source's file name as the citation. Can be scripted to return any reply.</summary>
public class FakeChatService : IChatService
{
    public int Calls { get; private set; }
    public string? LastUserPrompt { get; private set; }

    /// <summary>When set, returned verbatim instead of the default echo reply.</summary>
    public string? ScriptedReply { get; set; }

    public Task<string> CompleteJsonAsync(string system, string user, CancellationToken ct = default)
    {
        Calls++;
        LastUserPrompt = user;
        if (ScriptedReply is not null) return Task.FromResult(ScriptedReply);

        var file = Regex.Match(user, @"File: (\S+)").Groups[1].Value;
        return Task.FromResult($$"""{"answer":"From {{file}}.","citations":["{{file}}"]}""");
    }
}
