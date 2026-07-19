using Azure;
using Azure.AI.OpenAI;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Embeddings;

namespace Contoso.PolicyAssistant.Api.Features.Rag;

public static class AiClientFactory
{
    public static (IEmbeddingClient Embeddings, IGroundedChatClient Chat, string Mode) Create(IConfiguration config)
    {
        var requested = (config["Ai:Provider"] ?? "Lexical").Trim();

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
                return (
                    new OpenAiEmbeddingClient(emb, "AzureOpenAI"),
                    new OpenAiGroundedChatClient(chat, "AzureOpenAI"),
                    "AzureOpenAI");
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
                return (
                    new OpenAiEmbeddingClient(openai.GetEmbeddingClient(embedModel), "OpenAI"),
                    new OpenAiGroundedChatClient(openai.GetChatClient(chatModel), "OpenAI"),
                    "OpenAI");
            }
        }

        // Safe default for lab/tests when keys are absent
        return (
            new LexicalEmbeddingClient(),
            new LexicalGroundedChatClient(),
            "Lexical");
    }
}
