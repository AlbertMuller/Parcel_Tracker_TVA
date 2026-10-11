using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderTracker.Application.Abstractions;
using OrderTracker.Application.Orders;
using OrderTracker.Application.Orders.ChangeOrderStatus;
using OrderTracker.Application.Orders.ConvertOrderCurrency;
using OrderTracker.Application.Orders.ListOrders;
using OrderTracker.Application.Orders.SubmitOrder;
using OrderTracker.Domain.Orders;
using OrderTracker.Infrastructure.Persistence;
using OrderTracker.TestKit;

namespace OrderTracker.IntegrationTests;

/// <summary>
/// The aggregate's EF mapping, the unit of work and the read queries against real PostgreSQL: duplicates,
/// optimistic concurrency, constraints and listing.
/// </summary>
[Trait("Category", "Database")]
public class PersistenceTests(AppFixture app) : IClassFixture<AppFixture>
{
    private static string UniqueRef(string prefix = "PG") => $"{prefix}-{Guid.NewGuid():N}"[..30];

    private Task<Domain.Common.Result<SubmittedOrder>> SubmitAsync(SubmitOrderCommand command) =>
        app.Run<SubmitOrderHandler, Domain.Common.Result<SubmittedOrder>>(h => h.HandleAsync(command));

    private Task<Domain.Common.Result<OrderDto>> ChangeStatusAsync(Guid id, string status) =>
        app.Run<ChangeOrderStatusHandler, Domain.Common.Result<OrderDto>>(h => h.HandleAsync(new(id, status)));

    [Fact]
    public async Task An_aggregate_survives_a_save_and_reload_intact()
    {
        var submitted = (await SubmitAsync(TestData.ValidCommand(UniqueRef()))).Value.Order;
        await app.Run<ConvertOrderCurrencyHandler, Domain.Common.Result<OrderDto>>(h => h.HandleAsync(new(submitted.Id, "EUR")));
        var changed = (await ChangeStatusAsync(submitted.Id, "Confirmed")).Value;

        var reloaded = await app.Run<IOrderQueries, OrderDto?>(q => q.GetByIdAsync(submitted.Id));

        Assert.NotNull(reloaded);
        Assert.True(reloaded.Number > 0);
        // Everything the API returned straight after the write is exactly what's stored.
        Assert.Equivalent(changed, reloaded, strict: true);
        Assert.Equal([17.99m, 4.95m], reloaded.LineItems.Select(l => l.UnitPrice));
        Assert.Equal([19.99m, 5.50m], reloaded.LineItems.Select(l => l.BaseUnitPrice));
        Assert.Equal(2, reloaded.StatusHistory.Count);
        Assert.Single(reloaded.CurrencyHistory);
    }

    [Fact]
    public async Task Concurrent_identical_submissions_store_exactly_one_order()
    {
        // Each request has its own scope and connection, as in the API. Several pass the lookup at the same
        // moment; the unique index turns all but one into replays.
        var reference = UniqueRef("RACE");
        using var start = new ManualResetEventSlim(false);
        var tasks = Enumerable.Range(0, 20).Select(_ => Task.Run(async () =>
        {
            start.Wait();
            return await SubmitAsync(TestData.ValidCommand(reference));
        })).ToArray();

        start.Set();
        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.True(r.IsSuccess));
        Assert.Single(results, r => !r.Value.AlreadySubmitted);
        Assert.Single(results.Select(r => r.Value.Order.Id).Distinct());
        await using var db = app.NewDbContext();
        Assert.Equal(1, await db.Orders.CountAsync(o => o.Reference.Normalized == reference.ToUpperInvariant()));
    }

    [Fact]
    public async Task The_unit_of_work_reports_a_duplicate_reference()
    {
        var reference = UniqueRef();
        await SubmitAsync(TestData.ValidCommand(reference));

        var ex = await Assert.ThrowsAsync<DuplicateReferenceException>(() => app.Run<IServiceProvider, bool>(async sp =>
        {
            ((IOrderRepository)sp.GetService(typeof(IOrderRepository))!).Add(TestData.NewOrder(reference));
            await ((IUnitOfWork)sp.GetService(typeof(IUnitOfWork))!).SaveChangesAsync();
            return true;
        }));

        var pg = Assert.IsType<PostgresException>(Assert.IsType<DbUpdateException>(ex.InnerException).InnerException);
        Assert.Equal((PostgresErrorCodes.UniqueViolation, OrdersDbContext.UniqueReferenceIndex), (pg.SqlState, pg.ConstraintName));
    }

    [Fact]
    public async Task Check_constraints_reject_invalid_rows_written_around_the_domain()
    {
        var order = (await SubmitAsync(TestData.ValidCommand(UniqueRef()))).Value.Order;
        await using var db = app.NewDbContext();

        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync("UPDATE order_lines SET quantity = 0 WHERE order_id = {0}", order.Id));

        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
    }

    [Fact]
    public async Task Saving_from_a_stale_version_is_rejected()
    {
        var id = new OrderId((await SubmitAsync(TestData.ValidCommand(UniqueRef()))).Value.Order.Id);
        await using var first = app.NewDbContext();
        await using var second = app.NewDbContext();
        var a = await first.Orders.SingleAsync(o => o.Id == id);
        var b = await second.Orders.SingleAsync(o => o.Id == id);

        a.ChangeStatus(OrderStatus.Confirmed, TestData.Start);
        await first.SaveChangesAsync();
        b.ChangeStatus(OrderStatus.Cancelled, TestData.Start);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Racing_fulfil_and_cancel_let_exactly_one_win_and_keep_history_consistent()
    {
        for (var run = 0; run < 10; run++)
        {
            var id = (await SubmitAsync(TestData.ValidCommand(UniqueRef()))).Value.Order.Id;
            await ChangeStatusAsync(id, "Confirmed");

            var results = await Task.WhenAll(
                Task.Run(() => ChangeStatusAsync(id, "Fulfilled")),
                Task.Run(() => ChangeStatusAsync(id, "Cancelled")));

            Assert.Single(results, r => r.IsSuccess);
            Assert.Equal("order.status.final", results.Single(r => r.IsFailure).Error.Code);
            var stored = (await app.Run<IOrderQueries, OrderDto?>(q => q.GetByIdAsync(id)))!;
            Assert.Equal(3, stored.StatusHistory.Count); // the loser's history row was never committed
        }
    }

    [Fact]
    public async Task Lists_newest_first_with_filters_search_and_paging()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        for (var i = 1; i <= 5; i++)
        {
            app.Clock.Advance(TimeSpan.FromMinutes(1));
            await SubmitAsync(TestData.ValidCommand($"LIST-{tag}-{i}") with { Customer = new CustomerInput("jane@example.com", $"Customer {tag}") });
        }
        // Same instant as #5: the later order number comes first.
        await SubmitAsync(TestData.ValidCommand($"LIST-{tag}-6") with { Customer = new CustomerInput("jane@example.com", $"Customer {tag}") });

        async Task<PagedResult<OrderSummaryDto>> List(ListOrdersQuery q) => (await app.Run<ListOrdersHandler, Domain.Common.Result<PagedResult<OrderSummaryDto>>>(h => h.HandleAsync(q))).Value;

        var all = await List(new ListOrdersQuery(Search: tag));
        Assert.Equal(6, all.TotalCount);
        Assert.Equal(Enumerable.Range(1, 6).Reverse().Select(i => $"LIST-{tag}-{i}"), all.Items.Select(o => o.ExternalReference));
        Assert.All(all.Items, o => Assert.Equal(2, o.LineCount));

        var page2 = await List(new ListOrdersQuery(Search: tag, Page: 2, PageSize: 4));
        Assert.Equal([$"LIST-{tag}-2", $"LIST-{tag}-1"], page2.Items.Select(o => o.ExternalReference));

        Assert.Equal(6, (await List(new ListOrdersQuery(Search: $"CUSTOMER {tag.ToUpperInvariant()}"))).TotalCount); // ILIKE

        await ChangeStatusAsync(all.Items[^1].Id, "Confirmed");
        Assert.Equal([$"LIST-{tag}-1"], (await List(new ListOrdersQuery("Confirmed", tag))).Items.Select(o => o.ExternalReference));
    }

    [Fact]
    public async Task Search_treats_like_wildcards_literally()
    {
        await SubmitAsync(TestData.ValidCommand(UniqueRef("WILD")));

        async Task<int> Count(string search) =>
            (await app.Run<ListOrdersHandler, Domain.Common.Result<PagedResult<OrderSummaryDto>>>(h => h.HandleAsync(new ListOrdersQuery(Search: search)))).Value.TotalCount;

        Assert.Equal(0, await Count("%"));
        Assert.Equal(0, await Count("_"));
    }
}
