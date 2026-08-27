namespace Contoso.PolicyAssistant.Api.Features.Rag;

public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>Gemini, Lexical, OpenAI, or AzureOpenAI.</summary>
    public string Provider { get; set; } = "Gemini";

    /// <summary>
    /// Global cap on hosted (Gemini/OpenAI/Azure) ask requests per UTC day.
    /// Config value, not a hardcoded constant. Lexical fallback is used after this.
    /// </summary>
    public int DailyRequestCeiling { get; set; } = 10;

    /// <summary>Per-IP ask requests allowed inside <see cref="PerIpWindowMinutes"/>.</summary>
    public int PerIpLimit { get; set; } = 10;

    /// <summary>Window for the per-IP limiter, in minutes.</summary>
    public int PerIpWindowMinutes { get; set; } = 15;

    public AzureOpenAiOptions AzureOpenAI { get; set; } = new();
    public OpenAiOptions OpenAI { get; set; } = new();
    public GeminiOptions Gemini { get; set; } = new();
}

public sealed class AzureOpenAiOptions
{
    public string Endpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string ChatDeployment { get; set; } = "gpt-4o-mini";
    public string EmbeddingDeployment { get; set; } = "text-embedding-3-small";
}

public sealed class OpenAiOptions
{
    public string ApiKey { get; set; } = "";
    public string ChatModel { get; set; } = "gpt-4o-mini";
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
}

public sealed class GeminiOptions
{
    public string ApiKey { get; set; } = "";
    public string ChatModel { get; set; } = GeminiGroundedChatClient.DefaultModel;
    public string EmbeddingModel { get; set; } = GeminiEmbeddingClient.DefaultModel;
}
