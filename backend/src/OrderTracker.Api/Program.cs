using System.Text.Json.Serialization;
using OrderTracker.Api.Application;
using OrderTracker.Api.Endpoints;
using OrderTracker.Api.Infrastructure;
using OrderTracker.Api.Infrastructure.ExchangeRates;
using OrderTracker.Api.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<BadRequestExceptionHandler>();
// Surface malformed bodies as exceptions in every environment so the handler above can describe them.
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Healthy only when the database is reachable, so Compose won't route traffic to an API that can't serve it.
builder.Services.AddHealthChecks().AddDbContextCheck<OrdersDbContext>("database");
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOrdersDatabase(builder.Configuration);
builder.Services.AddScoped<OrderService>();

// Live exchange rates (ECB via Frankfurter), cached by ExchangeRateService.
var exchangeRateSection = builder.Configuration.GetSection(ExchangeRateOptions.Section);
builder.Services.Configure<ExchangeRateOptions>(exchangeRateSection);
builder.Services.AddHttpClient(FrankfurterExchangeRateProvider.HttpClientName, client =>
{
    var options = exchangeRateSection.Get<ExchangeRateOptions>() ?? new ExchangeRateOptions();
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = options.Timeout;
});
builder.Services.AddSingleton<IExchangeRateProvider, FrankfurterExchangeRateProvider>();
builder.Services.AddSingleton<ExchangeRateService>();

// The Angular dev server proxies /api, so CORS is only needed if the UI is served from elsewhere.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()
    .WithExposedHeaders(OrderEndpoints.ReplayHeader, "Location")));

var app = builder.Build();

// `dotnet OrderTracker.Api.dll migrate` applies migrations and exits (used by the Compose "migrate" service).
// Database:MigrateOnStartup (on in Development) applies them before serving instead.
if (args.Contains("migrate", StringComparer.OrdinalIgnoreCase))
{
    await DatabaseSetup.MigrateAsync(app.Configuration, app.Logger);
    return;
}
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
    await DatabaseSetup.MigrateAsync(app.Configuration, app.Logger);

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();

// Swagger is on in Development, and can be enabled elsewhere (e.g. Docker) with Swagger__Enabled=true.
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
}

app.MapHealthChecks("/health");
app.MapOrderEndpoints();
app.MapExchangeRateEndpoints();

app.Run();

// Exposed for WebApplicationFactory in the integration tests.
public partial class Program;
