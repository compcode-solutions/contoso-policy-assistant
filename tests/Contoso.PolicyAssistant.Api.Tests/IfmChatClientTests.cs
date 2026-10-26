using System.Net;
using System.Text;
using System.Text.Json;
using Contoso.PolicyAssistant.Api.Features.Rag;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Contoso.PolicyAssistant.Api.Tests;

public class IfmChatClientTests
{
    [Fact]
    public void StripReasoning_removes_think_tags()
    {
        var raw = "Hello <think>secret</think> world <ifm|think>";
        Assert.Equal("Hello  world", IfmGroundedChatClient.StripReasoning(raw));
    }

    [Fact]
    public void StripReasoning_keeps_answer_after_ifm_closer()
    {
        var raw = "Just answer from context. </ifm|think> The monthly wellness stipend is $40 USD [1].";
        Assert.Equal(
            "The monthly wellness stipend is $40 USD [1].",
            IfmGroundedChatClient.StripReasoning(raw));
    }

    [Fact]
    public async Task Ifm_client_uses_content_only_against_fake_server()
    {
        var handler = new FakeHandler((req, ct) =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.EndsWith("/chat/completions", req.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer test-key", req.Headers.Authorization!.ToString());
            var body = new
            {
                choices = new[]
                {
                    new
                    {
                        message = new
                        {
                            role = "assistant",
                            content = "Meal limit is $60 [1].",
                            reasoning = "I should cite the expense policy."
                        }
                    }
                },
                usage = new { prompt_tokens = 10, completion_tokens = 20 }
            };
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, body));
        });

        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var client = new IfmGroundedChatClient(http, "test-key", IfmGroundedChatClient.DefaultModel, "http://ifm.test/v1");

        var result = await client.AnswerAsync(
            "meal limit?",
            [SampleChunk()],
            CancellationToken.None);

        Assert.Equal("ifm", client.ProviderName);
        Assert.Contains("60", result.Text);
        Assert.DoesNotContain("should cite", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(10, result.PromptTokens);
        Assert.Equal(20, result.CompletionTokens);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Ifm_client_throws_LlmHttpException_on_429()
    {
        var handler = new FakeHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("""{"error":{"type":"rate_limit_error"}}""", Encoding.UTF8, "application/json")
            }));

        using var http = new HttpClient(handler);
        var client = new IfmGroundedChatClient(http, "test-key", IfmGroundedChatClient.DefaultModel, "http://ifm.test/v1");

        var ex = await Assert.ThrowsAsync<LlmHttpException>(() =>
            client.AnswerAsync("q", [SampleChunk()], CancellationToken.None));
        Assert.Equal(System.Net.HttpStatusCode.TooManyRequests, ex.StatusCode);
        Assert.Equal("429", ex.ReasonCode);
    }

    [Fact]
    public async Task Fallback_uses_gemini_once_on_ifm_429()
    {
        var ifmHandler = new FakeHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            }));
        var geminiCalls = 0;
        var primary = new IfmGroundedChatClient(
            new HttpClient(ifmHandler), "k", IfmGroundedChatClient.DefaultModel, "http://ifm.test/v1");
        var fallback = new StubChat("gemini", "gemini-2.5-flash-lite", () =>
        {
            geminiCalls++;
            return new GroundedChatResult { Text = "Fallback answer from Gemini [1].", Model = "gemini-2.5-flash-lite" };
        });

        var chat = new FallbackGroundedChatClient(primary, fallback);
        var result = await chat.AnswerAsync("meal?", [SampleChunk()], CancellationToken.None);

        Assert.Equal("ifm", chat.ProviderName);
        Assert.Contains("Gemini", result.Text);
        Assert.Equal(1, ifmHandler.CallCount);
        Assert.Equal(1, geminiCalls);
    }

    [Fact]
    public void Fallback_should_not_trigger_on_400()
    {
        var ex = new LlmHttpException(400, "bad request");
        Assert.False(FallbackGroundedChatClient.ShouldFallback(ex, CancellationToken.None));
        Assert.True(FallbackGroundedChatClient.ShouldFallback(new LlmHttpException(429, "rate"), CancellationToken.None));
        Assert.True(FallbackGroundedChatClient.ShouldFallback(new LlmHttpException(503, "down"), CancellationToken.None));
        Assert.True(FallbackGroundedChatClient.ShouldFallback(new TimeoutException(), CancellationToken.None));
    }

    [Fact]
    public void Factory_ifm_without_keys_stays_lexical()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ai:Provider"] = "Ifm"
            })
            .Build();

        var prev = Environment.GetEnvironmentVariable("LLM_PROVIDER");
        try
        {
            Environment.SetEnvironmentVariable("LLM_PROVIDER", null);
            var set = AiClientFactory.Create(config);
            Assert.Equal("Lexical", set.ActiveProvider);
            Assert.False(set.HostedConfigured);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LLM_PROVIDER", prev);
        }
    }

    [Fact]
    public void Factory_llm_provider_env_selects_ifm_when_keys_present()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ai:Provider"] = "Gemini",
                ["Ai:Gemini:ApiKey"] = "gemini-test-key",
                ["Ai:Ifm:ApiKey"] = "ifm-test-key"
            })
            .Build();

        var prev = Environment.GetEnvironmentVariable("LLM_PROVIDER");
        try
        {
            Environment.SetEnvironmentVariable("LLM_PROVIDER", "ifm");
            var set = AiClientFactory.Create(config);
            Assert.Equal("ifm", set.ActiveProvider);
            Assert.True(set.HostedConfigured);
            Assert.Equal("ifm", set.Chat.ProviderName);
            Assert.Equal("Gemini", set.Embeddings.ProviderName);
            Assert.Equal(IfmGroundedChatClient.DefaultModel, set.ChatModel);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LLM_PROVIDER", prev);
        }
    }

    private static RetrievedChunk SampleChunk() =>
        new()
        {
            N = 1,
            Score = 0.9f,
            Chunk = new PolicyChunk
            {
                Id = "e:0",
                DocumentId = "e",
                Title = "Expense",
                FileName = "expense.md",
                AllowedRoles = ["Employee"],
                Text = "Daily meal limit is $60 USD.",
                Embedding = [0.1f],
                LexicalEmbedding = [0.1f]
            }
        };

    private static HttpResponseMessage JsonResponse(HttpStatusCode code, object body) =>
        new(code)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _fn;
        public int CallCount { get; private set; }

        public FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> fn) =>
            _fn = fn;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return _fn(request, cancellationToken);
        }
    }

    private sealed class StubChat(string provider, string model, Func<GroundedChatResult> answer) : IGroundedChatClient
    {
        public string ProviderName { get; } = provider;
        public string ModelName { get; } = model;

        public Task<GroundedChatResult> AnswerAsync(
            string question,
            IReadOnlyList<RetrievedChunk> context,
            CancellationToken ct = default) =>
            Task.FromResult(answer());
    }
}
