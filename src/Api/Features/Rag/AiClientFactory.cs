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
        var requested = (config["Ai:Provider"] ?? "Gemini").Trim();
        if (string.IsNullOrWhiteSpace(requested)) requested = "Gemini";

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
