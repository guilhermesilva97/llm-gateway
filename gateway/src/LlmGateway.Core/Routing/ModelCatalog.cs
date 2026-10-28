namespace LlmGateway.Core.Routing;

public sealed record RouteTarget(string Provider, string Model)
{
    public static RouteTarget Parse(string value)
    {
        var separator = value.IndexOf('/');
        if (separator <= 0 || separator == value.Length - 1)
        {
            throw new FormatException($"Destino inválido '{value}'. Use o formato provedor/modelo.");
        }
        return new RouteTarget(value[..separator], value[(separator + 1)..]);
    }

    public override string ToString() => $"{Provider}/{Model}";
}

public sealed record ModelRoute(string Alias, IReadOnlyList<RouteTarget> Targets);

public sealed class ModelCatalog
{
    private readonly Dictionary<string, ModelRoute> _routes;

    public ModelCatalog(IEnumerable<ModelRoute> routes)
    {
        _routes = new Dictionary<string, ModelRoute>(StringComparer.OrdinalIgnoreCase);
        foreach (var route in routes)
        {
            if (route.Targets.Count == 0)
            {
                throw new ArgumentException($"O alias '{route.Alias}' não tem destinos.");
            }
            _routes[route.Alias] = route;
        }
    }

    public IReadOnlyCollection<ModelRoute> All => _routes.Values;

    public ModelRoute? Find(string alias) => _routes.GetValueOrDefault(alias);
}
