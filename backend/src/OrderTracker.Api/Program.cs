using System.Text.Json.Serialization;
using OrderTracker.Api.Endpoints;
using OrderTracker.Api.ErrorHandling;
using OrderTracker.Application;
using OrderTracker.Infrastructure;

// Composition root: the only place that knows every layer.

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<BadRequestExceptionHandler>();
// Surface malformed bodies as exceptions in every environment so the handler above can describe them.
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// The Angular dev server proxies /api, so CORS is only needed if the UI is served from elsewhere.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()
    .WithExposedHeaders(OrderEndpoints.ReplayHeader, "Location")));

var app = builder.Build();

// `dotnet OrderTracker.Api.dll migrate` applies migrations and exits (used by the Compose "migrate" service).
// Database:MigrateOnStartup (on in Development) applies them before serving instead.
if (args.Contains("migrate", StringComparer.OrdinalIgnoreCase))
{
    await DatabaseMigrator.MigrateAsync(app.Configuration, app.Logger);
    return;
}
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
    await DatabaseMigrator.MigrateAsync(app.Configuration, app.Logger);

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
