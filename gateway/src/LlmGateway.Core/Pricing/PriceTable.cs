using LlmGateway.Core.Chat;
using LlmGateway.Core.Routing;

namespace LlmGateway.Core.Pricing;

public sealed record ModelPrice(decimal InputPerMillion, decimal OutputPerMillion);

public sealed class PriceTable(IReadOnlyDictionary<string, ModelPrice> prices)
{
    private readonly Dictionary<string, ModelPrice> _prices =
        new(prices, StringComparer.OrdinalIgnoreCase);

    public ModelPrice? Find(RouteTarget target) => _prices.GetValueOrDefault(target.ToString());

    public decimal Cost(RouteTarget target, TokenUsage usage)
    {
        var price = Find(target);
        if (price is null)
        {
            return 0m;
        }
        var input = usage.PromptTokens * price.InputPerMillion / 1_000_000m;
        var output = usage.CompletionTokens * price.OutputPerMillion / 1_000_000m;
        return Math.Round(input + output, 8);
    }
}
