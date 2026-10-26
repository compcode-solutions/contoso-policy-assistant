using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Contoso.PolicyAssistant.Api.Features.Rag;

/// <summary>
/// OpenAI-compatible chat against IFM (K2 Horizon). Uses HttpClient so we can
/// pass chat_template_kwargs and ignore reasoning fields the OpenAI 2.1.0 SDK
/// does not model. Endpoint customisation on OpenAIClientOptions was verified
/// for 2.1.0; this client prefers raw HTTP for reasoning_effort + strip.
/// </summary>
public sealed class IfmGroundedChatClient : IGroundedChatClient
{
    public const string DefaultModel = "IFM/K2-Horizon-375B-A23B";
    public const string DefaultBaseUrl = "https://api.ifm.ai/v1";

    private static readonly Regex ThinkTagRx = new(
        @"<think>[\s\S]*?</think>|<ifm\|think(?:_fast(?:er)?)?>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly string _reasoningEffort;

    public IfmGroundedChatClient(
        HttpClient http,
        string apiKey,
        string modelName,
        string? baseUrl = null,
        string reasoningEffort = "low")
    {
        _http = http;
        _apiKey = apiKey;
        ModelName = string.IsNullOrWhiteSpace(modelName) ? DefaultModel : modelName;
        _baseUrl = (baseUrl ?? DefaultBaseUrl).TrimEnd('/');
        _reasoningEffort = string.IsNullOrWhiteSpace(reasoningEffort) ? "low" : reasoningEffort;
    }

    public string ProviderName => "ifm";
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

        var payload = new Dictionary<string, object?>
        {
            ["model"] = ModelName,
            ["temperature"] = 0.2,
            ["max_tokens"] = 2048,
            ["messages"] = new object[]
            {
                new Dictionary<string, string>
                {
                    ["role"] = "system",
                    ["content"] =
                        """
                        You are Contoso Policy Assistant. Answer ONLY using the CONTEXT blocks.
                        Rules:
                        - If CONTEXT is insufficient, say exactly: I don't know based on the available policies.
                        - Cite sources inline like [1] or [2] matching CONTEXT numbers.
                        - Never invent policies, menus, or unrelated facts.
                        - Be concise (2–5 sentences).
                        """
                },
                new Dictionary<string, string>
                {
                    ["role"] = "user",
                    ["content"] =
                        $"""
                        CONTEXT:
                        {sb}

                        QUESTION:
                        {question}
                        """
                }
            },
            ["chat_template_kwargs"] = new Dictionary<string, string>
            {
                ["reasoning_effort"] = _reasoningEffort
            }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions")
        {
            Content = JsonContent.Create(payload)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        using var res = await _http.SendAsync(req, ct);
        var raw = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            throw new LlmHttpException(
                (int)res.StatusCode,
                $"IFM chat HTTP {(int)res.StatusCode}");
        }

        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        if (!root.TryGetProperty("choices", out var choices)
            || choices.GetArrayLength() == 0)
        {
            throw new LlmHttpException(null, "IFM chat returned no choices");
        }

        var message = choices[0].GetProperty("message");
        var content = message.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
        content = StripReasoning(content);

        var promptTokens = 0;
        var completionTokens = 0;
        if (root.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("prompt_tokens", out var pt)) promptTokens = pt.GetInt32();
            if (usage.TryGetProperty("completion_tokens", out var ctkn)) completionTokens = ctkn.GetInt32();
        }

        return new GroundedChatResult
        {
            Text = string.IsNullOrWhiteSpace(content)
                ? "I don't know based on the available policies."
                : content.Trim(),
            Model = ModelName,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens
        };
    }

    public static string StripReasoning(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return ThinkTagRx.Replace(text, "").Trim();
    }
}

/// <summary>Carries HTTP status so fallback can map 429 / 5xx without parsing messages.</summary>
public sealed class LlmHttpException : HttpRequestException
{
    public string ReasonCode { get; }

    public LlmHttpException(int? statusCode, string message)
        : base(
            message,
            inner: null,
            statusCode: statusCode.HasValue ? (System.Net.HttpStatusCode)statusCode.Value : null)
    {
        ReasonCode = statusCode switch
        {
            429 => "429",
            >= 500 and <= 599 => "5xx",
            _ => statusCode?.ToString() ?? "error"
        };
    }
}
