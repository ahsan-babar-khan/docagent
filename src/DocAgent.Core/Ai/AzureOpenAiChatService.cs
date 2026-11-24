using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;

namespace DocAgent.Core.Ai;

public class AzureOpenAiChatService(ChatClient chat, string deployment, ILogger<AzureOpenAiChatService> logger) : IChatService
{
    public ChatClient Client => chat; // reused by the agent

    public async Task<string> CompleteJsonAsync(string system, string user, CancellationToken ct = default)
    {
        var options = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat(), // structured response
            Temperature = 0.1f                                            // low = more consistent
        };
        var sw = Stopwatch.StartNew();
        ChatCompletion result = (await chat.CompleteChatAsync(
            [new SystemChatMessage(system), new UserChatMessage(user)], options, ct)).Value;

        logger.LogInformation("LLM call {Deployment} took {Ms} ms, tokens in {In} out {Out}",
            deployment, sw.ElapsedMilliseconds, result.Usage?.InputTokenCount, result.Usage?.OutputTokenCount);

        return result.Content.Count > 0 ? result.Content[0].Text : "{}";
    }
}
