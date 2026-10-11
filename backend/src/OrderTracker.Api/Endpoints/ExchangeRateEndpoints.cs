using OrderTracker.Api.Application;
using OrderTracker.Api.Contracts;
using OrderTracker.Api.Infrastructure.ExchangeRates;

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
            .Produces<ExchangeRatesResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> GetCurrencies(ExchangeRateService rates, CancellationToken cancellationToken)
    {
        try
        {
            var currencies = await rates.GetCurrenciesAsync(cancellationToken);
            return TypedResults.Ok(new SortedDictionary<string, string>(currencies.ToDictionary(), StringComparer.Ordinal));
        }
        catch (ExchangeRatesUnavailableException ex)
        {
            return Unavailable(ex);
        }
    }

    private static async Task<IResult> GetRates(ExchangeRateService rates, CancellationToken cancellationToken, string? @base = "USD")
    {
        var code = (@base ?? "USD").Trim().ToUpperInvariant();
        if (!CreateOrderValidator.IsCurrencyCode(code))
            return InvalidBase($"'{@base}' is not a 3-letter ISO 4217 currency code.");

        try
        {
            var result = await rates.GetRatesAsync(code, cancellationToken);
            if (result is null)
                return InvalidBase($"No exchange rates are available for {code}.");

            var t = result.Table;
            return TypedResults.Ok(new ExchangeRatesResponse(t.Base, t.Date, t.Source, result.FetchedAt, result.IsStale, t.Rates));
        }
        catch (ExchangeRatesUnavailableException ex)
        {
            return Unavailable(ex);
        }
    }

    private static IResult InvalidBase(string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["base"] = [message] });

    private static IResult Unavailable(ExchangeRatesUnavailableException ex) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Exchange rates unavailable",
            detail: $"{ex.Message} Try again shortly.");
}
