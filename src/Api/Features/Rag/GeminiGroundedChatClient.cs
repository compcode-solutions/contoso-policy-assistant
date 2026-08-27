using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contoso.PolicyAssistant.Api.Features.Rag;

/// <summary>
/// Gemini Developer API generation. Default model is gemini-2.5-flash-lite
/// (free tier in AI Studio; no billing required).
/// </summary>
public sealed class GeminiGroundedChatClient : IGroundedChatClient
{
    public const string DefaultModel = "gemini-2.5-flash-lite";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly string _apiKey;

    public GeminiGroundedChatClient(HttpClient http, string apiKey, string modelName)
    {
        _http = http;
        _apiKey = apiKey;
        ModelName = modelName;
    }

    public string ProviderName => "Gemini";
    public string ModelName { get; }

    public async Task<GroundedChatResult> AnswerAsync(
        string question,
        IReadOnlyList<RetrievedChunk> context,
        CancellationToken ct = default)
    {
        if (context.Count == 0)
        {
            return new GroundedChatResult
            {
                Text = "I don't know based on the policies I can access.",
                Model = ModelName
            };
        }

        var sb = new StringBuilder();
        foreach (var hit in context)
        {
            sb.AppendLine($"[{hit.N}] Source: {hit.Chunk.FileName} ({hit.Chunk.Title})");
            sb.AppendLine(hit.Chunk.Text);
            sb.AppendLine();
        }

        var payload = new GeminiGenerateRequest
        {
            SystemInstruction = new GeminiContent
            {
                Parts =
                [
                    new GeminiPart
                    {
                        Text =
                            """
                            You are Contoso Policy Assistant. Answer ONLY using the CONTEXT blocks.
                            Rules:
                            - If CONTEXT is insufficient, say exactly: I don't know based on the available policies.
                            - Cite sources inline like [1] or [2] matching CONTEXT numbers.
                            - Never invent policies, menus, or unrelated facts.
                            - Be concise (2–5 sentences).
                            """
                    }
                ]
            },
            Contents =
            [
                new GeminiContent
                {
                    Role = "user",
                    Parts =
                    [
                        new GeminiPart
                        {
                            Text =
                                $"""
                                CONTEXT:
                                {sb}

                                QUESTION:
                                {question}
                                """
                        }
                    ]
                }
            ],
            GenerationConfig = new GeminiGenerationConfig
            {
                Temperature = 0.2,
                MaxOutputTokens = 512
            }
        };

        using var req = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{ModelName}:generateContent")
        {
            Content = JsonContent.Create(payload, options: JsonOpts)
        };
        req.Headers.TryAddWithoutValidation("x-goog-api-key", _apiKey);

        using var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Gemini generate HTTP {(int)res.StatusCode} {res.ReasonPhrase}");
        }

        var body = await res.Content.ReadFromJsonAsync<GeminiGenerateResponse>(JsonOpts, ct)
            ?? throw new HttpRequestException("Gemini generate returned an empty body.");

        var text = body.Candidates?
            .SelectMany(c => c.Content?.Parts ?? [])
            .Select(p => p.Text)
            .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t))
            ?.Trim()
            ?? "";

        return new GroundedChatResult
        {
            Text = string.IsNullOrWhiteSpace(text)
                ? "I don't know based on the available policies."
                : text,
            Model = ModelName,
            PromptTokens = body.UsageMetadata?.PromptTokenCount ?? 0,
            CompletionTokens = body.UsageMetadata?.CandidatesTokenCount ?? 0
        };
    }
}

internal sealed class GeminiGenerateRequest
{
    public GeminiContent? SystemInstruction { get; set; }
    public List<GeminiContent> Contents { get; set; } = [];
    public GeminiGenerationConfig? GenerationConfig { get; set; }
}

internal sealed class GeminiGenerationConfig
{
    public double Temperature { get; set; }
    public int MaxOutputTokens { get; set; }
}

internal sealed class GeminiGenerateResponse
{
    public List<GeminiCandidate>? Candidates { get; set; }
    public GeminiUsageMetadata? UsageMetadata { get; set; }
}

internal sealed class GeminiCandidate
{
    public GeminiContent? Content { get; set; }
}

internal sealed class GeminiUsageMetadata
{
    public int PromptTokenCount { get; set; }
    public int CandidatesTokenCount { get; set; }
}
