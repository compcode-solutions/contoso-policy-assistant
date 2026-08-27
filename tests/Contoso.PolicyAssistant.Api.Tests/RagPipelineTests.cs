using Contoso.PolicyAssistant.Api.Features.Ask;
using Contoso.PolicyAssistant.Api.Features.Rag;
using Microsoft.Extensions.Options;
using Xunit;

namespace Contoso.PolicyAssistant.Api.Tests;

public class RagPipelineTests
{
    [Fact]
    public void Chunker_splits_long_text()
    {
        var words = string.Join(' ', Enumerable.Range(0, 500).Select(i => $"w{i}"));
        var chunks = TextChunker.Chunk(words, maxWords: 100, overlapWords: 10);
        Assert.True(chunks.Count > 1);
    }

    [Fact]
    public void Employee_retrieval_excludes_supervisor_chunk()
    {
        var leave = LexicalEmbeddingClient.Embed("employees receive 20 days of paid annual leave");
        var safety = LexicalEmbeddingClient.Embed("escalate Priority-1 ticket safety incident supervisors");

        var store = new InMemoryVectorStore();
        store.ReplaceAll(
        [
            new PolicyChunk
            {
                Id = "leave:0",
                DocumentId = "leave",
                Title = "Leave Policy",
                FileName = "leave-policy.md",
                AllowedRoles = ["Employee", "Supervisor"],
                Text = "Full-time employees receive 20 days of paid annual leave per calendar year.",
                Embedding = leave
            },
            new PolicyChunk
            {
                Id = "safety:0",
                DocumentId = "safety",
                Title = "Workplace Safety Escalation",
                FileName = "safety-escalate.md",
                AllowedRoles = ["Supervisor", "Admin"],
                Text = "Escalate by creating a Priority-1 ticket for safety incidents.",
                Embedding = safety
            }
        ], "Lexical");

        var q = LexicalEmbeddingClient.Embed("How many leave days do employees get?");
        var hits = store.Search(q, ["Employee"], topK: 4, minScore: 0.05f);

        Assert.Equal(1, hits.FilteredByRole);
        Assert.Equal(2, hits.CorpusCount);
        Assert.Equal(1, hits.VisibleBeforeScoring);
        Assert.DoesNotContain(hits.Hits, h => h.Chunk.FileName == "safety-escalate.md");
        Assert.Contains(hits.Hits, h => h.Chunk.FileName == "leave-policy.md");
    }

    [Fact]
    public void Employee_retrieval_excludes_supervisor_chunk_openai_shaped_vectors()
    {
        // Same ACL-before-scoring path with 1536-dim vectors (text-embedding-3-small width).
        // Proves a provider swap cannot reorder the filter ahead of cosine.
        const int dims = OpenAiEmbeddingClient.DefaultDimensions;
        var leave = PadTo(LexicalEmbeddingClient.Embed("employees receive 20 days of paid annual leave"), dims);
        var safety = PadTo(LexicalEmbeddingClient.Embed("escalate Priority-1 ticket safety incident supervisors"), dims);

        var store = new InMemoryVectorStore();
        store.ReplaceAll(
        [
            new PolicyChunk
            {
                Id = "leave:0",
                DocumentId = "leave",
                Title = "Leave Policy",
                FileName = "leave-policy.md",
                AllowedRoles = ["Employee", "Supervisor"],
                Text = "Full-time employees receive 20 days of paid annual leave per calendar year.",
                Embedding = leave,
                LexicalEmbedding = LexicalEmbeddingClient.Embed("employees receive 20 days of paid annual leave")
            },
            new PolicyChunk
            {
                Id = "safety:0",
                DocumentId = "safety",
                Title = "Workplace Safety Escalation",
                FileName = "safety-escalate.md",
                AllowedRoles = ["Supervisor", "Admin"],
                Text = "Escalate by creating a Priority-1 ticket for safety incidents.",
                Embedding = safety,
                LexicalEmbedding = LexicalEmbeddingClient.Embed("escalate Priority-1 ticket safety incident supervisors")
            }
        ], "OpenAI");

        var q = PadTo(LexicalEmbeddingClient.Embed("How many leave days do employees get?"), dims);
        var hits = store.Search(q, ["Employee"], topK: 4, minScore: 0.05f);

        Assert.Equal(1, hits.FilteredByRole);
        Assert.DoesNotContain(hits.Hits, h => h.Chunk.FileName == "safety-escalate.md");
        Assert.Contains(hits.Hits, h => h.Chunk.FileName == "leave-policy.md");
    }

    private static float[] PadTo(float[] src, int dims)
    {
        var v = new float[dims];
        Array.Copy(src, v, Math.Min(src.Length, dims));
        return v;
    }

    [Fact]
    public async Task Handler_answers_leave_question_grounded()
    {
        var handler = await BuildHandlerAsync();
        var result = await handler.HandleAsync(
            new AskQuestionRequest { Question = "How many leave days do I get each year?" },
            ["Employee"]);

        Assert.True(result.Grounded);
        Assert.Contains("20", result.Answer);
        Assert.NotEmpty(result.Citations);
        Assert.Contains(result.Citations, c => c.Title.Contains("leave", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Handler_refuses_unrelated_question()
    {
        var handler = await BuildHandlerAsync();
        var result = await handler.HandleAsync(
            new AskQuestionRequest { Question = "What's the cafeteria menu for Friday?" },
            ["Employee"]);

        Assert.False(result.Grounded);
        Assert.StartsWith("I don't know", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Citations);
    }

    [Fact]
    public async Task Handler_employee_cannot_ground_on_supervisor_sop()
    {
        var handler = await BuildHandlerAsync();
        var result = await handler.HandleAsync(
            new AskQuestionRequest
            {
                Question = "What is the workplace safety escalation SOP Priority-1 ticket process?"
            },
            ["Employee"]);

        Assert.DoesNotContain("Priority-1", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            result.Citations,
            c => c.Title.Contains("safety", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<AskQuestionHandler> BuildHandlerAsync()
    {
        var embeddings = new LexicalEmbeddingClient();
        var chat = new LexicalGroundedChatClient();
        var store = new InMemoryVectorStore();

        var leaveText =
            "Full-time employees receive 20 days of paid annual leave per calendar year. Leave requests must be submitted at least 5 business days in advance.";
        var expenseText =
            "Meals while traveling: up to $60 USD per day. Submit expenses within 30 days.";
        var safetyText =
            "Escalate by creating a Priority-1 ticket in the incident system within 1 hour for safety incidents.";

        var leaveEmb = await embeddings.EmbedAsync(leaveText);
        var expenseEmb = await embeddings.EmbedAsync(expenseText);
        var safetyEmb = await embeddings.EmbedAsync(safetyText);

        store.ReplaceAll(
        [
            new PolicyChunk
            {
                Id = "leave:0",
                DocumentId = "leave",
                Title = "Leave Policy",
                FileName = "leave-policy.md",
                AllowedRoles = ["Employee", "Supervisor", "Admin"],
                Text = leaveText,
                Embedding = leaveEmb.Vector,
                LexicalEmbedding = leaveEmb.Vector
            },
            new PolicyChunk
            {
                Id = "expense:0",
                DocumentId = "expense",
                Title = "Expense Policy",
                FileName = "expense-policy.md",
                AllowedRoles = ["Employee", "Supervisor", "Admin"],
                Text = expenseText,
                Embedding = expenseEmb.Vector,
                LexicalEmbedding = expenseEmb.Vector
            },
            new PolicyChunk
            {
                Id = "safety:0",
                DocumentId = "safety",
                Title = "Workplace Safety Escalation",
                FileName = "safety-escalate.md",
                AllowedRoles = ["Supervisor", "Admin"],
                Text = safetyText,
                Embedding = safetyEmb.Vector,
                LexicalEmbedding = safetyEmb.Vector
            }
        ], "Lexical");

        return new AskQuestionHandler(
            store,
            embeddings,
            chat,
            new NullAiCallLogger(),
            Options.Create(new RagOptions { TopK = 4, MinScore = 0.12f }));
    }
}
