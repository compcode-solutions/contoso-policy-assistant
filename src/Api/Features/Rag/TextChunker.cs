namespace Contoso.PolicyAssistant.Api.Features.Rag;

/// <summary>
/// Rough ~500-token chunks using word windows (≈4 chars/token heuristic unused; word count used).
/// </summary>
public static class TextChunker
{
    public const int DefaultMaxWords = 380;
    public const int DefaultOverlapWords = 40;

    public static IReadOnlyList<string> Chunk(string markdown, int maxWords = DefaultMaxWords, int overlapWords = DefaultOverlapWords)
    {
        var normalized = markdown.Replace("\r\n", "\n").Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return [];
        }

        var words = normalized.Split([' ', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= maxWords)
        {
            return [normalized];
        }

        var chunks = new List<string>();
        var step = Math.Max(1, maxWords - overlapWords);
        for (var i = 0; i < words.Length; i += step)
        {
            var take = Math.Min(maxWords, words.Length - i);
            chunks.Add(string.Join(' ', words.Skip(i).Take(take)));
            if (i + take >= words.Length) break;
        }

        return chunks;
    }
}
