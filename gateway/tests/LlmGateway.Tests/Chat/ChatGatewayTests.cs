using System.Text.Json.Nodes;
using LlmGateway.Core.Chat;
using LlmGateway.Core.Clients;
using LlmGateway.Core.Pricing;
using LlmGateway.Core.Privacy;
using LlmGateway.Core.Routing;
using LlmGateway.Core.Telemetry;
using LlmGateway.Tests.Support;

namespace LlmGateway.Tests.Chat;

public sealed class ChatGatewayTests
{
    private static readonly GatewayClient Client = new("app", "App", 60, 10_000);

    private readonly FakeProviderExecutor _providers = new();
    private readonly InMemoryResponseCache _cache = new();
    private readonly InMemoryQuotaStore _quotas = new();
    private readonly ListUsageSink _usage = new();
    private readonly ChatGateway _gateway;

    public ChatGatewayTests()
    {
        var catalog = new ModelCatalog(
        [
            new ModelRoute("principal", [RouteTarget.Parse("primary/model-a"), RouteTarget.Parse("backup/model-b")]),
            new ModelRoute("unico", [RouteTarget.Parse("primary/model-a")]),
        ]);
        var prices = new PriceTable(new Dictionary<string, ModelPrice>
        {
            ["primary/model-a"] = new(1_000m, 2_000m),
            ["backup/model-b"] = new(10m, 20m),
        });
        _gateway = new ChatGateway(
            catalog, prices, new PiiMasker(), _providers, _cache, _quotas, _usage,
            new GatewayTelemetry(TestMeters.Create()), TimeProvider.System);
    }

    [Fact]
    public async Task Routes_to_the_first_target()
    {
        var result = await _gateway.CompleteAsync(Command("Olá"), default);

        Assert.Equal("primary", result.Metadata.Target.Provider);
        Assert.Equal(0, result.Metadata.Fallbacks);
        Assert.Equal("model-a", _providers.Calls.Single().Request["model"]!.GetValue<string>());
    }

    [Fact]
    public async Task Falls_back_when_the_first_provider_fails()
    {
        _providers.Failing.Add("primary");

        var result = await _gateway.CompleteAsync(Command("Olá"), default);

        Assert.Equal("backup", result.Metadata.Target.Provider);
        Assert.Equal(1, result.Metadata.Fallbacks);
        Assert.Equal(1, _usage.Records.Single().Fallbacks);
        Assert.Equal("ok", _usage.Records.Single().Status);
    }

    [Fact]
    public async Task Skips_unavailable_providers_without_calling_them()
    {
        _providers.Unavailable.Add("primary");

        var result = await _gateway.CompleteAsync(Command("Olá"), default);

        Assert.Equal("backup", result.Metadata.Target.Provider);
        Assert.DoesNotContain(_providers.Calls, c => c.Target.Provider == "primary");
    }

    [Fact]
    public async Task Returns_502_when_every_target_fails()
    {
        _providers.Failing.UnionWith(["primary", "backup"]);

        var error = await Assert.ThrowsAsync<GatewayException>(() => _gateway.CompleteAsync(Command("Olá"), default));

        Assert.Equal(502, error.StatusCode);
        Assert.Equal("all_providers_failed", error.Code);
        Assert.Equal("all_providers_failed", _usage.Records.Single().Status);
    }

    [Fact]
    public async Task Provider_rejections_are_not_retried_on_other_targets()
    {
        _providers.Rejecting.Add("primary");

        await Assert.ThrowsAsync<ProviderRejectedException>(() => _gateway.CompleteAsync(Command("Olá"), default));

        Assert.DoesNotContain(_providers.Calls, c => c.Target.Provider == "backup");
    }

    [Fact]
    public async Task Unknown_models_return_404()
    {
        var error = await Assert.ThrowsAsync<GatewayException>(() =>
            _gateway.CompleteAsync(Command("Olá", model: "inexistente"), default));

        Assert.Equal(404, error.StatusCode);
    }

    [Fact]
    public async Task Masks_pii_before_the_provider_and_restores_it_in_the_answer()
    {
        var result = await _gateway.CompleteAsync(Command("Meu e-mail é ana@x.com e meu CPF 529.982.247-25"), default);

        var sent = ChatPayload.LastUserMessage(_providers.Calls.Single().Request);
        Assert.Equal("Meu e-mail é [EMAIL_1] e meu CPF [CPF_1]", sent);
        Assert.Equal("eco: Meu e-mail é ana@x.com e meu CPF 529.982.247-25", Content(result.Body));
        Assert.Equal(2, _usage.Records.Single().PiiMasked);
        Assert.DoesNotContain("ana@x.com", _usage.Records.Single().PromptPreview);
    }

    [Fact]
    public async Task Keeps_placeholders_when_the_client_disables_rehydration()
    {
        var result = await _gateway.CompleteAsync(Command("Contato ana@x.com", rehydrate: false), default);

        Assert.Equal("eco: Contato [EMAIL_1]", Content(result.Body));
    }

    [Fact]
    public async Task Clients_that_opt_out_send_raw_text_but_logs_stay_masked()
    {
        var client = Client with { MaskPii = false };

        await _gateway.CompleteAsync(Command("Contato ana@x.com", client: client), default);

        Assert.Equal("Contato ana@x.com", ChatPayload.LastUserMessage(_providers.Calls.Single().Request));
        Assert.Equal("Contato [EMAIL_1]", _usage.Records.Single().PromptPreview);
    }

    [Fact]
    public async Task Identical_requests_are_served_from_cache_at_zero_cost()
    {
        var first = await _gateway.CompleteAsync(Command("Qual a capital do Brasil?"), default);
        var second = await _gateway.CompleteAsync(Command("Qual a capital do Brasil?"), default);

        Assert.Single(_providers.Calls);
        Assert.False(first.Metadata.CacheHit);
        Assert.True(second.Metadata.CacheHit);
        Assert.Equal(0m, second.CostUsd);
        Assert.Equal(Content(first.Body), Content(second.Body));
    }

    [Fact]
    public async Task Cache_entries_are_shared_by_masked_prompts_and_rehydrated_per_request()
    {
        await _gateway.CompleteAsync(Command("Avise ana@x.com"), default);
        var second = await _gateway.CompleteAsync(Command("Avise bia@y.com"), default);

        Assert.True(second.Metadata.CacheHit);
        Assert.Equal("eco: Avise bia@y.com", Content(second.Body));
    }

    [Fact]
    public async Task Cache_can_be_bypassed()
    {
        await _gateway.CompleteAsync(Command("Olá"), default);
        await _gateway.CompleteAsync(Command("Olá", bypassCache: true), default);

        Assert.Equal(2, _providers.Calls.Count);
    }

    [Fact]
    public async Task Rejects_requests_when_the_monthly_quota_is_exhausted()
    {
        await _quotas.AddAsync(Client.Id, Client.MonthlyTokenQuota, default);

        var error = await Assert.ThrowsAsync<GatewayException>(() => _gateway.CompleteAsync(Command("Olá"), default));

        Assert.Equal(429, error.StatusCode);
        Assert.Equal("quota_exceeded", error.Code);
        Assert.Empty(_providers.Calls);
    }

    [Fact]
    public async Task Calculates_cost_and_consumes_quota_from_reported_usage()
    {
        var result = await _gateway.CompleteAsync(Command("Olá"), default);

        Assert.Equal(new TokenUsage(10, 5), result.Usage);
        Assert.Equal(0.02m, result.CostUsd);
        Assert.Equal(15, await _quotas.GetUsedAsync(Client.Id, default));
        Assert.Equal(0.02m, _usage.Records.Single().CostUsd);
    }

    [Fact]
    public async Task Estimates_usage_when_the_provider_does_not_report_it()
    {
        _providers.FixedUsage = null;

        var result = await _gateway.CompleteAsync(Command("12345678"), default);

        Assert.True(result.Usage.PromptTokens > 0);
        Assert.True(result.Usage.CompletionTokens > 0);
    }

    [Fact]
    public async Task Streaming_rehydrates_chunks_and_hides_usage_when_not_requested()
    {
        await using var session = await _gateway.StreamAsync(Command("Fale com ana@x.com", stream: true), default);
        var chunks = await Collect(session);

        var text = string.Concat(chunks.Select(ChatPayload.DeltaText));
        Assert.Equal("eco: Fale com ana@x.com", text);
        Assert.DoesNotContain(chunks, c => c["usage"] is not null);
        Assert.Equal(new TokenUsage(7, 3).TotalTokens, await _quotas.GetUsedAsync(Client.Id, default));
        Assert.True(_usage.Records.Single().Stream);
    }

    [Fact]
    public async Task Streaming_forwards_usage_when_the_client_asks_for_it()
    {
        var command = Command("Olá", stream: true);
        command.Payload["stream_options"] = new JsonObject { ["include_usage"] = true };

        await using var session = await _gateway.StreamAsync(command, default);
        var chunks = await Collect(session);

        Assert.Contains(chunks, c => c["usage"] is JsonObject);
    }

    private static ChatCommand Command(
        string message,
        string model = "principal",
        bool rehydrate = true,
        bool bypassCache = false,
        bool stream = false,
        GatewayClient? client = null) => new(
            client ?? Client,
            new JsonObject
            {
                ["model"] = model,
                ["stream"] = stream,
                ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = message }),
            },
            Guid.NewGuid().ToString("N"),
            rehydrate,
            bypassCache);

    private static string Content(JsonObject body) =>
        body["choices"]![0]!["message"]!["content"]!.GetValue<string>();

    private static async Task<List<JsonObject>> Collect(ChatStreamSession session)
    {
        var chunks = new List<JsonObject>();
        await foreach (var chunk in session.ReadAllAsync())
        {
            chunks.Add(chunk);
        }
        return chunks;
    }
}
