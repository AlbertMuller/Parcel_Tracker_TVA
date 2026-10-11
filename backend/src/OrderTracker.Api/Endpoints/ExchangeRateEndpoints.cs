using OrderTracker.Api.ErrorHandling;
using OrderTracker.Application.ExchangeRates;

namespace OrderTracker.Api.Endpoints;

public static class ExchangeRateEndpoints
{
    public static IEndpointRouteBuilder MapExchangeRateEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").WithTags("Currencies");

        api.MapGet("/currencies", GetCurrencies)
            .WithSummary("Currencies that exchange rates are available for (ISO code → name).")
            .Produces<IReadOnlyDictionary<string, string>>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        api.MapGet("/exchange-rates", GetRates)
            .WithSummary("Latest exchange rates for a base currency (default USD). rates[X] = units of X per 1 base.")
            .Produces<ExchangeRatesDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> GetCurrencies(GetCurrenciesHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetCurrenciesQuery(), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> GetRates(GetExchangeRatesHandler handler, CancellationToken cancellationToken, string? @base = "USD")
    {
        var result = await handler.HandleAsync(new GetExchangeRatesQuery(@base), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }
}
