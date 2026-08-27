using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contoso.PolicyAssistant.Api.Features.Rag;

/// <summary>
/// Gemini Developer API embeddings (AI Studio key, not Vertex).
/// Model: gemini-embedding-001. Default native width is 3072; we request
/// Google's recommended Matryoshka size of 768 and L2-normalise.
/// </summary>
public sealed class GeminiEmbeddingClient : IEmbeddingClient
{
    public const string DefaultModel = "gemini-embedding-001";
    public const int DefaultDimensions = 768;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly int _dimensions;

    public GeminiEmbeddingClient(HttpClient http, string apiKey, string modelName, int dimensions = DefaultDimensions)
    {
        _http = http;
        _apiKey = apiKey;
        _dimensions = dimensions;
        ModelName = modelName;
    }

    public string ProviderName => "Gemini";
    public string ModelName { get; }

    public async Task<EmbeddingCallResult> EmbedAsync(string text, CancellationToken ct = default)
    {
        var batch = await EmbedBatchAsync([text], ct, taskType: "RETRIEVAL_QUERY");
        return new EmbeddingCallResult
        {
            Vector = batch.Vectors[0],
            TokenCount = batch.TokenCount,
            Model = ModelName
        };
    }

    public Task<EmbeddingBatchResult> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken ct = default) =>
        EmbedBatchAsync(texts, ct, taskType: "RETRIEVAL_DOCUMENT");

    private async Task<EmbeddingBatchResult> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken ct,
        string taskType)
    {
        if (texts.Count == 0)
        {
            return new EmbeddingBatchResult { Vectors = [], TokenCount = 0, Model = ModelName };
        }

        var vectors = new List<float[]>(texts.Count);
        var tokens = 0;
        const int batchSize = 16;
        for (var offset = 0; offset < texts.Count; offset += batchSize)
        {
            var slice = texts.Skip(offset).Take(batchSize).ToList();
            var payload = new GeminiBatchEmbedRequest
            {
                Requests = slice.Select(t => new GeminiEmbedRequest
                {
                    Model = $"models/{ModelName}",
                    Content = new GeminiContent { Parts = [new GeminiPart { Text = t }] },
                    TaskType = taskType,
                    OutputDimensionality = _dimensions
                }).ToList()
            };

            using var req = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://generativelanguage.googleapis.com/v1beta/models/{ModelName}:batchEmbedContents")
            {
                Content = JsonContent.Create(payload, options: JsonOpts)
            };
            req.Headers.TryAddWithoutValidation("x-goog-api-key", _apiKey);

            using var res = await _http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Gemini embeddings HTTP {(int)res.StatusCode} {res.ReasonPhrase}");
            }

            var body = await res.Content.ReadFromJsonAsync<GeminiBatchEmbedResponse>(JsonOpts, ct)
                ?? throw new HttpRequestException("Gemini embeddings returned an empty body.");
            if (body.Embeddings is null || body.Embeddings.Count != slice.Count)
            {
                throw new HttpRequestException("Gemini embeddings count did not match the batch.");
            }

            foreach (var emb in body.Embeddings)
            {
                var values = emb.Values ?? throw new HttpRequestException("Gemini embedding missing values.");
                vectors.Add(Normalize(values, _dimensions));
            }
        }

        return new EmbeddingBatchResult
        {
            Vectors = vectors,
            TokenCount = tokens,
            Model = ModelName
        };
    }

    internal static float[] Normalize(IReadOnlyList<float> src, int dims)
    {
        var n = Math.Min(src.Count, dims);
        var v = new float[dims];
        double norm = 0;
        for (var i = 0; i < n; i++)
        {
            v[i] = src[i];
            norm += src[i] * src[i];
        }
        if (norm > 0)
        {
            var inv = (float)(1.0 / Math.Sqrt(norm));
            for (var i = 0; i < n; i++) v[i] *= inv;
        }
        return v;
    }
}

internal sealed class GeminiBatchEmbedRequest
{
    public List<GeminiEmbedRequest> Requests { get; set; } = [];
}

internal sealed class GeminiEmbedRequest
{
    public string Model { get; set; } = "";
    public GeminiContent Content { get; set; } = new();
    public string? TaskType { get; set; }
    public int? OutputDimensionality { get; set; }
}

internal sealed class GeminiContent
{
    public string? Role { get; set; }
    public List<GeminiPart> Parts { get; set; } = [];
}

internal sealed class GeminiPart
{
    public string? Text { get; set; }
}

internal sealed class GeminiBatchEmbedResponse
{
    public List<GeminiEmbeddingValues>? Embeddings { get; set; }
}

internal sealed class GeminiEmbeddingValues
{
    public List<float>? Values { get; set; }
}
