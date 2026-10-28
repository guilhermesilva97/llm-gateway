using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LlmGateway.Core.Chat;
using LlmGateway.Infrastructure.Configuration;

namespace LlmGateway.Infrastructure.Providers;

public sealed class OpenAiCompatibleProvider(string name, ProviderOptions options, IHttpClientFactory httpClients) : IChatProvider
{
    public const string HttpClientName = "llm-providers";

    public string Name => name;

    public string Type => "openai-compatible";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(options.BaseUrl)
        && (!options.RequireApiKey || !string.IsNullOrWhiteSpace(options.ApiKey));

    public async Task<JsonObject> CompleteAsync(JsonObject request, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        try
        {
            return await JsonNode.ParseAsync(body, cancellationToken: cancellationToken) as JsonObject
                ?? throw new ProviderTransientException($"{name}: resposta vazia.");
        }
        catch (JsonException error)
        {
            throw new ProviderTransientException($"{name}: resposta não é JSON válido.", error);
        }
    }

    public async Task<IChatStream> OpenStreamAsync(JsonObject request, CancellationToken cancellationToken)
    {
        var response = await SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        try
        {
            await EnsureSuccessAsync(response, cancellationToken);
            var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            return new ChatStream(ReadEventsAsync(body), response);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private async Task<HttpResponseMessage> SendAsync(JsonObject payload, HttpCompletionOption completion, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{options.BaseUrl!.TrimEnd('/')}/chat/completions")
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        }
        var http = httpClients.CreateClient(HttpClientName);
        http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        try
        {
            return await http.SendAsync(request, completion, cancellationToken);
        }
        catch (HttpRequestException error)
        {
            throw new ProviderTransientException($"{name}: falha de conexão ({error.Message}).", error);
        }
        catch (TaskCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProviderTransientException($"{name}: tempo limite de {options.TimeoutSeconds}s excedido.", error);
        }
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = ErrorMessage(body);
        var status = (int)response.StatusCode;
        if (response.StatusCode == HttpStatusCode.TooManyRequests || status >= 500)
        {
            throw new ProviderTransientException($"{name}: HTTP {status} ({message}).");
        }
        throw new ProviderRejectedException(name, status, message);
    }

    private static async IAsyncEnumerable<JsonObject> ReadEventsAsync(Stream body, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(body);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }
            var data = line[5..].Trim();
            if (data == "[DONE]")
            {
                yield break;
            }
            if (JsonNode.Parse(data) is JsonObject chunk)
            {
                yield return chunk;
            }
        }
    }

    private static string ErrorMessage(string body)
    {
        try
        {
            if (JsonNode.Parse(body)?["error"] is { } error)
            {
                return error is JsonObject ? error["message"]?.ToString() ?? error.ToJsonString() : error.ToString();
            }
        }
        catch (JsonException)
        {
        }
        return body.Length > 300 ? body[..300] : body;
    }
}
