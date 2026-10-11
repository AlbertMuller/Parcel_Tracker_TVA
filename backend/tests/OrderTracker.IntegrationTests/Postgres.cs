using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using OrderTracker.Application;
using OrderTracker.Application.ExchangeRates;
using OrderTracker.Infrastructure;
using InfrastructureSetup = OrderTracker.Infrastructure.DependencyInjection;
using OrderTracker.Infrastructure.Persistence;
using OrderTracker.TestKit;
using Testcontainers.PostgreSql;

namespace OrderTracker.IntegrationTests;

/// <summary>
/// One real PostgreSQL container (via Testcontainers, so Docker must be running) shared by the whole run.
/// Each test class gets its own freshly migrated database inside it. Testcontainers removes the container
/// when the run ends.
/// </summary>
public static class TestPostgres
{
    private static readonly Lazy<Task<PostgreSqlContainer>> Container = new(async () =>
    {
        var container = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await container.StartAsync();
        return container;
    });

    /// <summary>Creates an empty database, applies all migrations to it and returns its connection string.</summary>
    public static async Task<string> CreateMigratedDatabaseAsync()
    {
        var container = await Container.Value;
        var name = $"test_{Guid.NewGuid():N}";

        await using (var connection = new NpgsqlConnection(container.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { Database = name }.ConnectionString;
        await DatabaseMigrator.MigrateAsync(Configuration(connectionString), NullLogger.Instance);
        return connectionString;
    }

    public static IConfiguration Configuration(string connectionString) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"ConnectionStrings:{InfrastructureSetup.AppConnectionName}"] = connectionString,
            [$"ConnectionStrings:{InfrastructureSetup.MigrationsConnectionName}"] = connectionString,
        })
        .Build();
}

/// <summary>
/// The production container (Application + Infrastructure) against a real, migrated database, with fake
/// exchange rates and a controllable clock. Each operation runs in its own DI scope, like an HTTP request.
/// </summary>
public sealed class AppFixture : IAsyncLifetime
{
    public ServiceProvider Services { get; private set; } = null!;
    public string ConnectionString { get; private set; } = "";
    public ManualTimeProvider Clock { get; } = new();

    public async Task InitializeAsync()
    {
        ConnectionString = await TestPostgres.CreateMigratedDatabaseAsync();
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<TimeProvider>(Clock)
            .AddApplication()
            .AddInfrastructure(TestPostgres.Configuration(ConnectionString));
        services.Replace(ServiceDescriptor.Singleton<IExchangeRateProvider>(new FakeExchangeRateProvider()));
        Services = services.BuildServiceProvider();
    }

    public async Task DisposeAsync() => await Services.DisposeAsync();

    /// <summary>Runs <paramref name="action"/> with a handler (or any service) from a fresh scope.</summary>
    public async Task<TResult> Run<TService, TResult>(Func<TService, Task<TResult>> action) where TService : notnull
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    public OrdersDbContext NewDbContext()
    {
        var options = new DbContextOptionsBuilder<OrdersDbContext>();
        InfrastructureSetup.ConfigureDbContext(options, ConnectionString);
        return new OrdersDbContext(options.Options);
    }
}
