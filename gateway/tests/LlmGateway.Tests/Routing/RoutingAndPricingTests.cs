using LlmGateway.Core.Chat;
using LlmGateway.Core.Pricing;
using LlmGateway.Core.Routing;

namespace LlmGateway.Tests.Routing;

public sealed class RoutingAndPricingTests
{
    [Theory]
    [InlineData("ollama/qwen2.5:7b", "ollama", "qwen2.5:7b")]
    [InlineData("openai/gpt-4o-mini", "openai", "gpt-4o-mini")]
    [InlineData("azure/deployments/gpt-4o", "azure", "deployments/gpt-4o")]
    public void Parses_targets_splitting_on_the_first_slash(string value, string provider, string model)
    {
        var target = RouteTarget.Parse(value);

        Assert.Equal(provider, target.Provider);
        Assert.Equal(model, target.Model);
    }

    [Theory]
    [InlineData("sem-barra")]
    [InlineData("/modelo")]
    [InlineData("provedor/")]
    public void Rejects_malformed_targets(string value) =>
        Assert.Throws<FormatException>(() => RouteTarget.Parse(value));

    [Fact]
    public void Finds_aliases_ignoring_case()
    {
        var catalog = new ModelCatalog([new ModelRoute("GPT-4o-mini", [RouteTarget.Parse("openai/gpt-4o-mini")])]);

        Assert.NotNull(catalog.Find("gpt-4o-mini"));
        Assert.Null(catalog.Find("gpt-5"));
    }

    [Fact]
    public void Refuses_aliases_without_targets() =>
        Assert.Throws<ArgumentException>(() => new ModelCatalog([new ModelRoute("vazio", [])]));

    [Fact]
    public void Calculates_cost_from_input_and_output_prices()
    {
        var prices = new PriceTable(new Dictionary<string, ModelPrice>
        {
            ["openai/gpt-4o-mini"] = new(0.15m, 0.60m),
        });

        var cost = prices.Cost(RouteTarget.Parse("openai/gpt-4o-mini"), new TokenUsage(1_000_000, 500_000));

        Assert.Equal(0.45m, cost);
    }

    [Fact]
    public void Unknown_prices_cost_zero()
    {
        var prices = new PriceTable(new Dictionary<string, ModelPrice>());

        Assert.Equal(0m, prices.Cost(RouteTarget.Parse("ollama/qwen2.5:7b"), new TokenUsage(1000, 1000)));
    }

    [Fact]
    public void Small_requests_keep_fractional_cents()
    {
        var prices = new PriceTable(new Dictionary<string, ModelPrice> { ["mock/mock-1"] = new(0.50m, 1.50m) });

        Assert.Equal(0.000065m, prices.Cost(RouteTarget.Parse("mock/mock-1"), new TokenUsage(40, 30)));
    }
}
