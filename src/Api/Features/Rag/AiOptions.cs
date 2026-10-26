namespace Contoso.PolicyAssistant.Api.Features.Rag;

public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>Gemini, Ifm, Lexical, OpenAI, or AzureOpenAI. Env LLM_PROVIDER=ifm also selects Ifm.</summary>
    public string Provider { get; set; } = "Gemini";

    /// <summary>
    /// Global cap on hosted (Gemini/IFM/OpenAI/Azure) ask requests per UTC day.
    /// Kept under Gemini free-tier generate RPD (20 for flash-lite) with headroom.
    /// Lexical / demo-script answers do not consume this quota. IFM calls count here too.
    /// </summary>
    public int DailyRequestCeiling { get; set; } = 8;

    /// <summary>Per-IP ask requests allowed inside <see cref="PerIpWindowMinutes"/>.</summary>
    public int PerIpLimit { get; set; } = 10;

    /// <summary>Window for the per-IP limiter, in minutes.</summary>
    public int PerIpWindowMinutes { get; set; } = 15;

    public AzureOpenAiOptions AzureOpenAI { get; set; } = new();
    public OpenAiOptions OpenAI { get; set; } = new();
    public GeminiOptions Gemini { get; set; } = new();
    public IfmOptions Ifm { get; set; } = new();
}

public sealed class IfmOptions
{
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = IfmGroundedChatClient.DefaultBaseUrl;
    public string ChatModel { get; set; } = IfmGroundedChatClient.DefaultModel;
    /// <summary>low | medium | high — IFM chat_template_kwargs.reasoning_effort</summary>
    public string ReasoningEffort { get; set; } = "low";
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
