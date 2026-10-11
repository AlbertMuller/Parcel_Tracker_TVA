using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrderTracker.Application.Abstractions;
using OrderTracker.Application.ExchangeRates;
using OrderTracker.Application.Orders;
using OrderTracker.Domain.Orders;
using OrderTracker.Infrastructure.ExchangeRates;
using OrderTracker.Infrastructure.Persistence;

namespace OrderTracker.Infrastructure;

/// <summary>
/// Two connection strings, two database roles:
/// <list type="bullet">
/// <item><c>ConnectionStrings:Orders</c>, used by the running API. In Docker this role can only read,
/// insert and update order data; it cannot change the schema.</item>
/// <item><c>ConnectionStrings:OrdersMigrations</c>, used only to apply migrations. In Docker this role
/// owns the schema.</item>
/// </list>
/// </summary>
public static class DependencyInjection
{
    public const string AppConnectionName = "Orders";
    public const string MigrationsConnectionName = "OrdersMigrations";
    public const string ExchangeRatesSection = "ExchangeRates";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Resolved when a DbContext is first created, not at startup, so the `migrate` command can run with
        // only the migrations connection configured.
        services.AddDbContext<OrdersDbContext>((_, options) => ConfigureDbContext(options,
            configuration.GetConnectionString(AppConnectionName)
                ?? throw new InvalidOperationException($"Connection string '{AppConnectionName}' is not configured.")));

        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IOrderQueries, OrderQueries>();

        var rates = configuration.GetSection(ExchangeRatesSection);
        services.Configure<ExchangeRateCacheOptions>(rates);
        services.AddHttpClient(FrankfurterExchangeRateProvider.HttpClientName, client =>
        {
            var options = rates.Get<FrankfurterOptions>() ?? new FrankfurterOptions();
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = options.Timeout;
        });
        services.AddSingleton<IExchangeRateProvider, FrankfurterExchangeRateProvider>();

        // Healthy only when the database is reachable, so Compose won't route traffic to an API that can't serve it.
        services.AddHealthChecks().AddDbContextCheck<OrdersDbContext>("database");

        return services;
    }

    internal static void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();
}

/// <summary>Applies EF Core migrations; run by the `migrate` command or on startup in Development.</summary>
public static class DatabaseMigrator
{
    /// <summary>Applies pending EF Core migrations using the migrations connection (falls back to the app one).</summary>
    public static async Task MigrateAsync(IConfiguration configuration, ILogger logger, CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString(DependencyInjection.MigrationsConnectionName)
            ?? configuration.GetConnectionString(DependencyInjection.AppConnectionName)
            ?? throw new InvalidOperationException($"Connection string '{DependencyInjection.MigrationsConnectionName}' is not configured.");

        var options = new DbContextOptionsBuilder<OrdersDbContext>();
        DependencyInjection.ConfigureDbContext(options, connectionString);
        await using var db = new OrdersDbContext(options.Options);

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count == 0)
        {
            logger.LogInformation("Database schema is up to date.");
            return;
        }

        logger.LogInformation("Applying {Count} migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
        await db.Database.MigrateAsync(cancellationToken);
        logger.LogInformation("Migrations applied.");
    }
}

/// <summary>Lets <c>dotnet ef migrations add</c> build the model without starting the web app or a database.</summary>
public sealed class OrdersDbContextDesignTimeFactory : IDesignTimeDbContextFactory<OrdersDbContext>
{
    public OrdersDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<OrdersDbContext>();
        DependencyInjection.ConfigureDbContext(options, "Host=localhost;Database=ordertracker;Username=design_time");
        return new OrdersDbContext(options.Options);
    }
}
