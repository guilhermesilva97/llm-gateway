using System.Security.Cryptography;
using System.Text;
using LlmGateway.Api.Http;
using LlmGateway.Core.Chat;
using LlmGateway.Core.Pricing;
using LlmGateway.Core.Routing;
using LlmGateway.Infrastructure.Administration;
using LlmGateway.Infrastructure.Configuration;
using LlmGateway.Infrastructure.Providers;
using Microsoft.Extensions.Options;

namespace LlmGateway.Api.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/admin").AddEndpointFilter(RequireAdminKeyAsync);

        admin.MapGet("/overview", (AdminService service, int? hours, CancellationToken ct) =>
            service.OverviewAsync(hours ?? 24, ct));

        admin.MapGet("/clients", (AdminService service, CancellationToken ct) => service.ListClientsAsync(ct));

        admin.MapPost("/clients", (AdminService service, NewClient request, CancellationToken ct) =>
            Guard(() => service.CreateClientAsync(request, ct), created => Results.Created($"/admin/clients/{created.Client.Id}", created)));

        admin.MapPatch("/clients/{id}", (AdminService service, string id, ClientPatch patch, CancellationToken ct) =>
            Guard(() => service.UpdateClientAsync(id, patch, ct), Results.Ok));

        admin.MapPost("/clients/{id}/rotate-key", (AdminService service, string id, CancellationToken ct) =>
            Guard(() => service.RotateKeyAsync(id, ct), Results.Ok));

        admin.MapGet("/requests", (AdminService service, int? limit, string? clientId, CancellationToken ct) =>
            service.RecentRequestsAsync(limit ?? 50, clientId, ct));

        admin.MapGet("/providers", (ResilientProviderExecutor executor) => executor.Statuses());

        admin.MapGet("/models", (ModelCatalog catalog, PriceTable prices) => catalog.All
            .OrderBy(r => r.Alias, StringComparer.OrdinalIgnoreCase)
            .Select(r => new
            {
                r.Alias,
                Targets = r.Targets.Select(t => new { Target = t.ToString(), Price = prices.Find(t) }),
            }));
    }

    private static async Task<IResult> Guard<T>(Func<Task<T>> action, Func<T, IResult> onSuccess)
    {
        try
        {
            return onSuccess(await action());
        }
        catch (GatewayException error)
        {
            return Results.Json(ErrorResponses.Body(error.StatusCode, error.Code, error.Message), statusCode: error.StatusCode);
        }
    }

    private static async ValueTask<object?> RequireAdminKeyAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var expected = http.RequestServices.GetRequiredService<IOptions<GatewayOptions>>().Value.AdminKey;
        var provided = http.Request.Headers["x-admin-key"].ToString();
        if (string.IsNullOrEmpty(expected) || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(provided)))
        {
            return Results.Json(ErrorResponses.Body(401, "invalid_admin_key", "Chave administrativa ausente ou inválida."), statusCode: 401);
        }
        return await next(context);
    }
}
