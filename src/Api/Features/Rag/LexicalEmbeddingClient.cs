using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Contoso.PolicyAssistant.Api.Features.Rag;

/// <summary>
/// Offline stand-in: hashed bag-of-words vectors. Good enough for golden demos without API keys.
/// </summary>
public sealed class LexicalEmbeddingClient : IEmbeddingClient
{
    private static readonly Regex TokenRx = new(@"[a-z0-9$]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    public const int Dimensions = 256;
    public const string Model = "lexical-bow-256";

    public string ProviderName => "Lexical";
    public string ModelName => Model;

    public Task<EmbeddingCallResult> EmbedAsync(string text, CancellationToken ct = default) =>
        Task.FromResult(new EmbeddingCallResult
        {
            Vector = Embed(text),
            TokenCount = 0,
            Model = Model
        });

    public Task<EmbeddingBatchResult> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken ct = default) =>
        Task.FromResult(new EmbeddingBatchResult
        {
            Vectors = texts.Select(Embed).ToList(),
            TokenCount = 0,
            Model = Model
        });

    public static float[] Embed(string text)
    {
        var vec = new float[Dimensions];
        foreach (Match m in TokenRx.Matches(text.ToLowerInvariant()))
        {
            var token = m.Value;
            if (token.Length < 2) continue;
            var idx = IndexOf(token);
            vec[idx] += 1f;
            // light bigram boost with next char hash
            var idx2 = IndexOf(token + "#");
            vec[idx2] += 0.35f;
        }

        // L2 normalize
        double norm = 0;
        for (var i = 0; i < vec.Length; i++) norm += vec[i] * vec[i];
        if (norm > 0)
        {
            var inv = (float)(1.0 / Math.Sqrt(norm));
            for (var i = 0; i < vec.Length; i++) vec[i] *= inv;
        }

        return vec;
    }

    private static int IndexOf(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        var n = BitConverter.ToUInt32(hash, 0);
        return (int)(n % Dimensions);
    }
}
