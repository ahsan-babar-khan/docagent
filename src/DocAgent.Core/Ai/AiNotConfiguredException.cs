namespace DocAgent.Core.Ai;

/// <summary>Thrown when an AI call is needed but no key/endpoint is configured. The API maps it to 503.</summary>
public class AiNotConfiguredException(string message) : InvalidOperationException(message);
