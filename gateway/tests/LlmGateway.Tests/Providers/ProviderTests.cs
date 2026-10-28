using System.Text.Json.Nodes;
using LlmGateway.Core.Chat;
using LlmGateway.Core.Routing;
using LlmGateway.Core.Telemetry;
using LlmGateway.Infrastructure.Configuration;
using LlmGateway.Infrastructure.Providers;
using LlmGateway.Tests.Support;

namespace LlmGateway.Tests.Providers;

public sealed class ResilientProviderExecutorTests
{
    private static readonly ResilienceOptions FastResilience = new()
    {
        RetryAttempts = 2,
        RetryDelayMs = 1,
        MinimumThroughput = 2,
        FailureRatio = 0.5,
        SamplingSeconds = 30,
        BreakSeconds = 30,
    };

    [Fact]
    public async Task Retries_transient_failures_before_giving_up()
    {
        var provider = new ScriptedProvider("flaky", failuresBeforeSuccess: 1);
        var executor = Executor(provider);

        var response = await executor.CompleteAsync(new RouteTarget("flaky", "m"), Request(), default);

        Assert.Equal(2, provider.Calls);
        Assert.Equal("ok", response["result"]!.GetValue<string>());
    }

    [Fact]
    public async Task Opens_the_circuit_after_repeated_failures()
    {
        var provider = new ScriptedProvider("down", failuresBeforeSuccess: int.MaxValue);
        var executor = Executor(provider);

        await Assert.ThrowsAsync<ProviderUnavailableException>(() =>
            executor.CompleteAsync(new RouteTarget("down", "m"), Request(), default));

        Assert.False(executor.IsAvailable("down"));
        Assert.Equal("open", executor.Statuses().Single().Circuit);
        Assert.True(executor.Statuses().Single().Failures > 0);
    }

    [Fact]
    public async Task Does_not_retry_rejected_requests()
    {
        var provider = new ScriptedProvider("strict", failuresBeforeSuccess: 0, reject: true);
        var executor = Executor(provider);

        await Assert.ThrowsAsync<ProviderRejectedException>(() =>
            executor.CompleteAsync(new RouteTarget("strict", "m"), Request(), default));

        Assert.Equal(1, provider.Calls);
        Assert.True(executor.IsAvailable("strict"));
    }

    [Fact]
    public async Task Unknown_providers_are_unavailable()
    {
        var executor = Executor(new ScriptedProvider("known", 0));

        Assert.False(executor.IsAvailable("unknown"));
        await Assert.ThrowsAsync<ProviderUnavailableException>(() =>
            executor.CompleteAsync(new RouteTarget("unknown", "m"), Request(), default));
    }

    [Fact]
    public void Providers_without_credentials_are_not_available()
    {
        var unconfigured = new OpenAiCompatibleProvider("openai", new ProviderOptions
        {
            BaseUrl = "https://api.openai.com/v1",
            RequireApiKey = true,
        }, null!);

        Assert.False(Executor(unconfigured).IsAvailable("openai"));
    }

    private static ResilientProviderExecutor Executor(IChatProvider provider) =>
        new([provider], FastResilience, new GatewayTelemetry(TestMeters.Create()), TimeProvider.System);

    private static JsonObject Request() => new() { ["model"] = "m" };

    private sealed class ScriptedProvider(string name, int failuresBeforeSuccess, bool reject = false) : IChatProvider
    {
        public int Calls { get; private set; }

        public string Name => name;

        public string Type => "scripted";

        public bool IsConfigured => true;

        public Task<JsonObject> CompleteAsync(JsonObject request, CancellationToken cancellationToken)
        {
            Calls++;
            if (reject)
            {
                throw new ProviderRejectedException(name, 400, "inválido");
            }
            if (Calls <= failuresBeforeSuccess)
            {
                throw new ProviderTransientException("instável");
            }
            return Task.FromResult(new JsonObject { ["result"] = "ok" });
        }

        public Task<IChatStream> OpenStreamAsync(JsonObject request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}

public sealed class MockChatProviderTests
{
    private readonly MockChatProvider _provider = new("mock", new ProviderOptions { Type = "mock" }, TimeProvider.System);

    [Fact]
    public async Task Answers_deterministically_in_openai_format()
    {
        var first = await _provider.CompleteAsync(Request("Olá"), default);
        var second = await _provider.CompleteAsync(Request("Olá"), default);

        Assert.Equal(first["id"]!.GetValue<string>(), second["id"]!.GetValue<string>());
        Assert.Equal("chat.completion", first["object"]!.GetValue<string>());
        Assert.Contains("\"Olá\"", first["choices"]![0]!["message"]!["content"]!.GetValue<string>());
        Assert.NotNull(TokenUsage.FromJson(first["usage"]));
    }

    [Fact]
    public async Task Streams_chunks_and_reports_usage_only_when_asked()
    {
        var request = Request("Olá mundo");
        request["stream_options"] = new JsonObject { ["include_usage"] = true };

        await using var stream = await _provider.OpenStreamAsync(request, default);
        var chunks = new List<JsonObject>();
        await foreach (var chunk in stream)
        {
            chunks.Add(chunk);
        }

        Assert.Contains("Olá mundo", string.Concat(chunks.Select(ChatPayload.DeltaText)));
        Assert.Equal("stop", chunks[^2]["choices"]![0]!["finish_reason"]!.GetValue<string>());
        Assert.NotNull(TokenUsage.FromJson(chunks[^1]["usage"]));
    }

    [Fact]
    public async Task Fails_when_configured_to_be_unstable()
    {
        var unstable = new MockChatProvider("mock-unstable", new ProviderOptions { Type = "mock", FailureRate = 1 }, TimeProvider.System);

        await Assert.ThrowsAsync<ProviderTransientException>(() => unstable.CompleteAsync(Request("Olá"), default));
    }

    private static JsonObject Request(string message) => new()
    {
        ["model"] = "mock-1",
        ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = message }),
    };
}
