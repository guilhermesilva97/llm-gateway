using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;

namespace LlmGateway.Tests.Integration;

public sealed class GatewayApiTests(GatewayApiFactory factory) : IClassFixture<GatewayApiFactory>
{
    [Fact]
    public async Task Health_does_not_require_authentication()
    {
        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Requests_without_api_key_are_rejected_in_openai_format()
    {
        var response = await factory.CreateClient().GetAsync("/v1/models");
        var body = await Json(response);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("missing_api_key", body["error"]!["code"]!.GetValue<string>());
        Assert.Equal("authentication_error", body["error"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task Requests_with_unknown_key_are_rejected()
    {
        var response = await Client("gw_nao_existe").GetAsync("/v1/models");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Lists_configured_aliases()
    {
        var body = await Json(await Client(GatewayApiFactory.AppKey).GetAsync("/v1/models"));

        var ids = body["data"]!.AsArray().Select(m => m!["id"]!.GetValue<string>()).ToList();
        Assert.Equal(["mock-1", "resilient"], ids);
    }

    [Fact]
    public async Task Completes_chat_and_restores_masked_data_for_the_caller()
    {
        var response = await Client(GatewayApiFactory.AppKey)
            .PostAsync("/v1/chat/completions", Chat("mock-1", "Meu e-mail é ana@x.com"));
        var body = await Json(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("ana@x.com", Content(body));
        Assert.Equal("mock", Header(response, "x-gateway-provider"));
        Assert.Equal("miss", Header(response, "x-gateway-cache"));
        Assert.True(decimal.Parse(Header(response, "x-gateway-cost-usd"), System.Globalization.CultureInfo.InvariantCulture) > 0);
    }

    [Fact]
    public async Task Provider_only_sees_placeholders()
    {
        var client = Client(GatewayApiFactory.AppKey);
        client.DefaultRequestHeaders.Add("x-gateway-pii-rehydrate", "false");

        var body = await Json(await client.PostAsync("/v1/chat/completions", Chat("mock-1", "CPF 529.982.247-25, fone (11) 98765-4321")));

        var content = Content(body);
        Assert.Contains("[CPF_1]", content);
        Assert.Contains("[TELEFONE_1]", content);
        Assert.DoesNotContain("529.982.247-25", content);
    }

    [Fact]
    public async Task Clients_that_disable_masking_send_raw_text()
    {
        var client = Client(GatewayApiFactory.RawClientKey);
        client.DefaultRequestHeaders.Add("x-gateway-pii-rehydrate", "false");

        var body = await Json(await client.PostAsync("/v1/chat/completions", Chat("mock-1", "Contato ana@x.com")));

        Assert.Contains("ana@x.com", Content(body));
    }

    [Fact]
    public async Task Second_identical_request_is_a_cache_hit()
    {
        var client = Client(GatewayApiFactory.AppKey);
        var prompt = "Pergunta para cache " + Guid.NewGuid();

        await client.PostAsync("/v1/chat/completions", Chat("mock-1", prompt));
        var second = await client.PostAsync("/v1/chat/completions", Chat("mock-1", prompt));

        Assert.Equal("hit", Header(second, "x-gateway-cache"));
        Assert.Equal("cache", Header(second, "x-gateway-provider"));
    }

    [Fact]
    public async Task Streams_server_sent_events_until_done()
    {
        var response = await Client(GatewayApiFactory.AppKey)
            .PostAsync("/v1/chat/completions", Chat("mock-1", "Avise ana@x.com", stream: true));
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
        var events = raw.Split("\n\n", StringSplitOptions.RemoveEmptyEntries).Select(e => e["data: ".Length..]).ToList();
        Assert.Equal("[DONE]", events[^1]);
        var text = string.Concat(events[..^1]
            .Select(e => JsonNode.Parse(e)!["choices"]!.AsArray())
            .SelectMany(choices => choices)
            .Select(c => c!["delta"]?["content"]?.GetValue<string>() ?? string.Empty));
        Assert.Contains("ana@x.com", text);
        Assert.DoesNotContain("[EMAIL_1]", text);
    }

    [Fact]
    public async Task Falls_back_to_the_next_provider_when_the_first_is_offline()
    {
        var response = await Client(GatewayApiFactory.AppKey)
            .PostAsync("/v1/chat/completions", Chat("resilient", "Olá " + Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("mock", Header(response, "x-gateway-provider"));
        Assert.Equal("1", Header(response, "x-gateway-fallbacks"));
    }

    [Fact]
    public async Task Unknown_models_return_404()
    {
        var response = await Client(GatewayApiFactory.AppKey).PostAsync("/v1/chat/completions", Chat("gpt-99", "Olá"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("model_not_found", (await Json(response))["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task Invalid_json_returns_400()
    {
        var response = await Client(GatewayApiFactory.AppKey).PostAsync("/v1/chat/completions",
            new StringContent("{nao é json", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Rate_limits_each_client_independently()
    {
        var tiny = Client(GatewayApiFactory.TinyKey);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            statuses.Add((await tiny.GetAsync("/v1/models")).StatusCode);
        }
        var limited = await tiny.GetAsync("/v1/models");
        var other = await Client(GatewayApiFactory.AppKey).GetAsync("/v1/models");

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
        Assert.Equal("rate_limit_exceeded", (await Json(limited))["error"]!["code"]!.GetValue<string>());
        Assert.True(limited.Headers.Contains("Retry-After"));
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    [Fact]
    public async Task Admin_endpoints_require_the_admin_key()
    {
        var response = await factory.CreateClient().GetAsync("/admin/clients");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_creates_clients_whose_keys_work_immediately()
    {
        var admin = Admin();

        var created = await admin.PostAsJsonAsync("/admin/clients", new { name = "Novo Projeto Ágil", requestsPerMinute = 10 });
        var body = await Json(created);
        var key = body["apiKey"]!.GetValue<string>();

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("novo-projeto-agil", body["client"]!["id"]!.GetValue<string>());
        Assert.StartsWith("gw_", key);
        Assert.Equal(HttpStatusCode.OK, (await Client(key).GetAsync("/v1/models")).StatusCode);
    }

    [Fact]
    public async Task Deactivated_clients_lose_access()
    {
        var admin = Admin();
        var created = await Json(await admin.PostAsJsonAsync("/admin/clients", new { name = "Temporário" }));
        var id = created["client"]!["id"]!.GetValue<string>();

        await admin.PatchAsJsonAsync($"/admin/clients/{id}", new { active = false });
        var key = created["apiKey"]!.GetValue<string>();

        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(key).GetAsync("/v1/models")).StatusCode);
    }

    [Fact]
    public async Task Request_log_stores_only_masked_prompts()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        await Client(GatewayApiFactory.AppKey)
            .PostAsync("/v1/chat/completions", Chat("mock-1", $"log {marker} de bia@empresa.com"));

        JsonArray? requests = null;
        for (var attempt = 0; attempt < 50; attempt++)
        {
            requests = (await Json(await Admin().GetAsync("/admin/requests?limit=100&clientId=app"))).AsArray();
            if (requests.Any(r => r!["promptPreview"]!.GetValue<string>().Contains(marker)))
            {
                break;
            }
            await Task.Delay(100);
        }

        var entry = requests!.First(r => r!["promptPreview"]!.GetValue<string>().Contains(marker))!;
        Assert.Contains("[EMAIL_1]", entry["promptPreview"]!.GetValue<string>());
        Assert.DoesNotContain("bia@empresa.com", entry["promptPreview"]!.GetValue<string>());
        Assert.Equal(1, entry["piiMasked"]!.GetValue<int>());
    }

    [Fact]
    public async Task Admin_reports_provider_status_and_overview()
    {
        await Client(GatewayApiFactory.AppKey).PostAsync("/v1/chat/completions", Chat("mock-1", "status " + Guid.NewGuid()));

        var providers = (await Json(await Admin().GetAsync("/admin/providers"))).AsArray();
        var overview = await Json(await Admin().GetAsync("/admin/overview?hours=1"));

        Assert.Contains(providers, p => p!["name"]!.GetValue<string>() == "mock" && p["circuit"]!.GetValue<string>() == "closed");
        Assert.NotNull(overview["totals"]);
    }

    private HttpClient Client(string apiKey)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return client;
    }

    private HttpClient Admin()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("x-admin-key", GatewayApiFactory.AdminKey);
        return client;
    }

    private static StringContent Chat(string model, string message, bool stream = false) => new(
        new JsonObject
        {
            ["model"] = model,
            ["stream"] = stream,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = message }),
        }.ToJsonString(),
        Encoding.UTF8,
        "application/json");

    private static async Task<JsonNode> Json(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

    private static string Content(JsonNode body) =>
        body["choices"]![0]!["message"]!["content"]!.GetValue<string>();

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.First() : string.Empty;
}
