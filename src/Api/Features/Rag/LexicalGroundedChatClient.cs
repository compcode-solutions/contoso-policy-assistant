using System.Text.RegularExpressions;

namespace Contoso.PolicyAssistant.Api.Features.Rag;

/// <summary>
/// Extractive offline answerer: picks the best overlapping sentence(s) from top chunks.
/// </summary>
public sealed class LexicalGroundedChatClient : IGroundedChatClient
{
    private static readonly Regex SentenceSplit = new(@"(?<=[\.!\?])\s+", RegexOptions.Compiled);

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "and", "or", "but", "for", "are", "is", "was", "were", "be", "been",
        "to", "of", "in", "on", "at", "by", "as", "it", "its", "this", "that", "these", "those",
        "with", "from", "have", "has", "had", "do", "does", "did", "will", "would", "can", "could",
        "should", "may", "might", "must", "i", "you", "we", "they", "he", "she", "my", "your",
        "our", "their", "me", "him", "her", "us", "them", "what", "which", "who", "whom", "how",
        "when", "where", "why", "about", "into", "over", "after", "before", "than", "then", "also",
        "just", "like", "get", "got", "each", "other", "some", "any", "all", "own", "same", "so",
        "too", "very", "contoso", "policy", "policies"
    };

    public const string Model = "lexical-extractive";

    public string ProviderName => "Lexical";
    public string ModelName => Model;

    public Task<GroundedChatResult> AnswerAsync(
        string question,
        IReadOnlyList<RetrievedChunk> context,
        CancellationToken ct = default)
    {
        if (context.Count == 0)
        {
            return Task.FromResult(Wrap(
                "I don't know based on the policies I can access. Please ask about Contoso leave, expenses, travel, laptops, or (if you're a supervisor) safety escalation."));
        }

        var qTokens = Tokenize(question);
        if (qTokens.Count == 0)
        {
            return Task.FromResult(Wrap("I don't know based on the available policies."));
        }

        // Chunk-level gate: at least one retrieved chunk must share 2+ content tokens
        var bestChunkOverlap = context.Max(h => OverlapScore(qTokens, Tokenize(h.Chunk.Text)));
        if (bestChunkOverlap < 2)
        {
            return Task.FromResult(Wrap(
                "I don't know based on the available policy context. Try a more specific Contoso policy question."));
        }

        var bestSentences = new List<(int N, string Sentence, int Score)>();

        foreach (var hit in context)
        {
            var sentences = SentenceSplit.Split(hit.Chunk.Text)
                .Select(s => s.Trim())
                .Where(s => s.Length > 20)
                .ToArray();

            if (sentences.Length == 0)
            {
                bestSentences.Add((hit.N, Truncate(hit.Chunk.Text, 280), OverlapScore(qTokens, Tokenize(hit.Chunk.Text))));
                continue;
            }

            foreach (var s in sentences)
            {
                bestSentences.Add((hit.N, s, OverlapScore(qTokens, Tokenize(s))));
            }
        }

        var picked = bestSentences
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.N)
            .Where(x => x.Score >= 2)
            .Take(2)
            .ToList();

        if (picked.Count == 0)
        {
            return Task.FromResult(Wrap(
                "I don't know based on the available policy context. Try a more specific Contoso policy question."));
        }

        var parts = picked.Select(p => $"{p.Sentence.TrimEnd('.')} [{p.N}].");
        return Task.FromResult(Wrap(string.Join(' ', parts)));
    }

    private static GroundedChatResult Wrap(string text) => new()
    {
        Text = text,
        Model = Model,
        PromptTokens = 0,
        CompletionTokens = 0
    };

    internal static HashSet<string> Tokenize(string text) =>
        text.ToLowerInvariant()
            .Split([' ', '\t', '\n', ',', '.', '?', '!', ';', ':', '"', '\'', '(', ')', '/', '-', '*', '#'],
                StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim('$'))
            .Where(t => t.Length > 2 && !StopWords.Contains(t) && !t.All(char.IsDigit))
            .ToHashSet();

    private static int OverlapScore(HashSet<string> q, HashSet<string> s) =>
        q.Count(s.Contains);

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max].TrimEnd() + "…";
}
