using LlmGateway.Api.Endpoints;
using LlmGateway.Api.Http;
using LlmGateway.Api.Observability;
using LlmGateway.Infrastructure;

if (args.Contains("--healthcheck"))
{
    return await HealthProbe.RunAsync(Environment.GetEnvironmentVariable("HEALTHCHECK_URL") ?? "http://localhost:8080/health");
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGatewayInfrastructure(builder.Configuration);
builder.Services.AddGatewayObservability(builder.Configuration);
builder.Services.AddPerClientRateLimiting();

var app = builder.Build();

app.UseMiddleware<ClientAuthenticationMiddleware>();
app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapOpenAiEndpoints();
app.MapAdminEndpoints();

await app.RunAsync();
return 0;

public partial class Program;

internal static class HealthProbe
{
    public static async Task<int> RunAsync(string url)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            using var response = await http.GetAsync(url);
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception)
        {
            return 1;
        }
    }
}
