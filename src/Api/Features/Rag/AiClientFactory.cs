using Azure;
using Azure.AI.OpenAI;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Embeddings;

namespace Contoso.PolicyAssistant.Api.Features.Rag;

public sealed record AiClientSet(
    IEmbeddingClient Embeddings,
    IGroundedChatClient Chat,
    string RequestedProvider,
    string ActiveProvider,
    bool HostedConfigured,
    string EmbeddingModel,
    string ChatModel,
    int EmbeddingDimensions);

public static class AiClientFactory
{
    public static AiClientSet Create(IConfiguration config)
    {
        var requested = ResolveRequestedProvider(config);

        if (string.Equals(requested, "Ifm", StringComparison.OrdinalIgnoreCase)
            || string.Equals(requested, "ifm", StringComparison.OrdinalIgnoreCase))
        {
            return CreateIfmPrimary(config, requested);
        }

        if (string.Equals(requested, "Gemini", StringComparison.OrdinalIgnoreCase))
        {
            var apiKey = config["Ai:Gemini:ApiKey"]
                ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            var chatModel = config["Ai:Gemini:ChatModel"] ?? GeminiGroundedChatClient.DefaultModel;
            var embedModel = config["Ai:Gemini:EmbeddingModel"] ?? GeminiEmbeddingClient.DefaultModel;

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
                return new AiClientSet(
                    new GeminiEmbeddingClient(http, apiKey, embedModel, GeminiEmbeddingClient.DefaultDimensions),
                    new GeminiGroundedChatClient(http, apiKey, chatModel),
                    requested,
                    "Gemini",
                    HostedConfigured: true,
                    embedModel,
                    chatModel,
                    GeminiEmbeddingClient.DefaultDimensions);
            }

            return Lexical(requested);
        }

        if (string.Equals(requested, "AzureOpenAI", StringComparison.OrdinalIgnoreCase))
        {
            var endpoint = config["Ai:AzureOpenAI:Endpoint"];
            var apiKey = config["Ai:AzureOpenAI:ApiKey"];
            var chatDeployment = config["Ai:AzureOpenAI:ChatDeployment"] ?? "gpt-4o-mini";
            var embedDeployment = config["Ai:AzureOpenAI:EmbeddingDeployment"] ?? "text-embedding-3-small";

            if (!string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(apiKey))
            {
                var azure = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
                EmbeddingClient emb = azure.GetEmbeddingClient(embedDeployment);
                ChatClient chat = azure.GetChatClient(chatDeployment);
                return new AiClientSet(
                    new OpenAiEmbeddingClient(emb, "AzureOpenAI", embedDeployment),
                    new OpenAiGroundedChatClient(chat, "AzureOpenAI", chatDeployment),
                    requested,
                    "AzureOpenAI",
                    HostedConfigured: true,
                    embedDeployment,
                    chatDeployment,
                    OpenAiEmbeddingClient.DefaultDimensions);
            }
        }

        if (string.Equals(requested, "OpenAI", StringComparison.OrdinalIgnoreCase)
            || string.Equals(requested, "AzureOpenAI", StringComparison.OrdinalIgnoreCase))
        {
            var apiKey = config["Ai:OpenAI:ApiKey"]
                ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            var chatModel = config["Ai:OpenAI:ChatModel"] ?? "gpt-4o-mini";
            var embedModel = config["Ai:OpenAI:EmbeddingModel"] ?? "text-embedding-3-small";

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                var openai = new OpenAIClient(apiKey);
                return new AiClientSet(
                    new OpenAiEmbeddingClient(openai.GetEmbeddingClient(embedModel), "OpenAI", embedModel),
                    new OpenAiGroundedChatClient(openai.GetChatClient(chatModel), "OpenAI", chatModel),
                    requested,
                    "OpenAI",
                    HostedConfigured: true,
                    embedModel,
                    chatModel,
                    OpenAiEmbeddingClient.DefaultDimensions);
            }
        }

        return Lexical(requested);
    }

    /// <summary>
    /// IFM for answers; Gemini embeddings unchanged. Falls back to Gemini chat once
    /// on IFM 429/5xx/timeout. Requires both LLM_API_KEY (or Ai:Ifm:ApiKey) and Gemini key.
    /// </summary>
    private static AiClientSet CreateIfmPrimary(IConfiguration config, string requested)
    {
        var ifmKey = FirstNonEmpty(
            config["Ai:Ifm:ApiKey"],
            Environment.GetEnvironmentVariable("LLM_API_KEY"),
            Environment.GetEnvironmentVariable("IFM_API_KEY"));
        var baseUrl = FirstNonEmpty(
            config["Ai:Ifm:BaseUrl"],
            Environment.GetEnvironmentVariable("LLM_BASE_URL"),
            IfmGroundedChatClient.DefaultBaseUrl)!;
        var chatModel = FirstNonEmpty(
            config["Ai:Ifm:ChatModel"],
            Environment.GetEnvironmentVariable("LLM_MODEL"),
            IfmGroundedChatClient.DefaultModel)!;
        var effort = FirstNonEmpty(
            config["Ai:Ifm:ReasoningEffort"],
            "low")!;

        var geminiKey = config["Ai:Gemini:ApiKey"]
            ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        var embedModel = config["Ai:Gemini:EmbeddingModel"] ?? GeminiEmbeddingClient.DefaultModel;
        var geminiChatModel = config["Ai:Gemini:ChatModel"] ?? GeminiGroundedChatClient.DefaultModel;

        if (string.IsNullOrWhiteSpace(ifmKey) || string.IsNullOrWhiteSpace(geminiKey))
            return Lexical(requested);

        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var geminiHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        IGroundedChatClient ifmChat = new IfmGroundedChatClient(http, ifmKey, chatModel, baseUrl, effort);
        IGroundedChatClient geminiChat = new GeminiGroundedChatClient(geminiHttp, geminiKey, geminiChatModel);
        IGroundedChatClient chat = new FallbackGroundedChatClient(ifmChat, geminiChat);

        return new AiClientSet(
            new GeminiEmbeddingClient(geminiHttp, geminiKey, embedModel, GeminiEmbeddingClient.DefaultDimensions),
            chat,
            requested,
            "ifm",
            HostedConfigured: true,
            embedModel,
            chatModel,
            GeminiEmbeddingClient.DefaultDimensions);
    }

    internal static string ResolveRequestedProvider(IConfiguration config)
    {
        var llm = Environment.GetEnvironmentVariable("LLM_PROVIDER");
        if (!string.IsNullOrWhiteSpace(llm))
            return llm.Trim();

        var requested = (config["Ai:Provider"] ?? "Gemini").Trim();
        return string.IsNullOrWhiteSpace(requested) ? "Gemini" : requested;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
        }
        return null;
    }

    private static AiClientSet Lexical(string requested) =>
        new(
            new LexicalEmbeddingClient(),
            new LexicalGroundedChatClient(),
            requested,
            "Lexical",
            HostedConfigured: false,
            LexicalEmbeddingClient.Model,
            LexicalGroundedChatClient.Model,
            LexicalEmbeddingClient.Dimensions);
}
