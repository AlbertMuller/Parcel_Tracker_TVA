using OrderTracker.Api.Application;
using OrderTracker.Api.Contracts;
using OrderTracker.Api.Domain;

namespace OrderTracker.Tests;

public class OrderServiceTests
{
    // ── Creating orders and server-side totals ─────────────────────────────

    [Fact]
    public void New_order_is_pending_with_totals_computed_on_the_server()
    {
        var (service, _, clock) = TestData.NewService();

        var result = service.Create(TestData.ValidRequest());

        Assert.Equal(CreateOrderOutcome.Created, result.Outcome);
        var order = result.Order!;
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(1, order.Number);
        Assert.Equal([59.97m, 5.50m], order.LineItems.Select(l => l.LineTotal));
        Assert.Equal(65.47m, order.Subtotal);
        Assert.Equal(65.47m, order.Total);
        Assert.Equal(clock.GetUtcNow(), order.CreatedAt);
        Assert.Equal("USD", order.Currency);
        Assert.StartsWith("cus_", order.Customer.Id);
    }

    [Fact]
    public void Invalid_order_is_not_stored()
    {
        var (service, repository, _) = TestData.NewService();

        var result = service.Create(TestData.ValidRequest() with { Currency = null });

        Assert.Equal(CreateOrderOutcome.Invalid, result.Outcome);
        Assert.Equal(0, repository.List(new()).TotalCount);
    }

    // ── Duplicate prevention ───────────────────────────────────────────────

    [Fact]
    public void Resubmitting_the_same_order_returns_the_original_without_creating_a_duplicate()
    {
        var (service, repository, clock) = TestData.NewService();
        var first = service.Create(TestData.ValidRequest("PO-42"));
        clock.Advance(TimeSpan.FromMinutes(5));

        var second = service.Create(TestData.ValidRequest("PO-42"));

        Assert.Equal(CreateOrderOutcome.AlreadySubmitted, second.Outcome);
        Assert.Equal(first.Order!.Id, second.Order!.Id);
        Assert.Equal(first.Order.CreatedAt, second.Order.CreatedAt);
        Assert.Equal(1, repository.List(new()).TotalCount);
    }

    [Theory]
    [InlineData("po-42")]
    [InlineData("  PO-42  ")]
    public void Reference_matching_ignores_case_and_surrounding_whitespace(string resubmittedReference)
    {
        var (service, repository, _) = TestData.NewService();
        service.Create(TestData.ValidRequest("PO-42"));

        var second = service.Create(TestData.ValidRequest(resubmittedReference));

        Assert.Equal(CreateOrderOutcome.AlreadySubmitted, second.Outcome);
        Assert.Equal(1, repository.List(new()).TotalCount);
    }

    [Fact]
    public void Formatting_differences_in_the_payload_still_count_as_the_same_submission()
    {
        var (service, _, _) = TestData.NewService();
        service.Create(TestData.ValidRequest("PO-42"));

        var reformatted = TestData.ValidRequest("PO-42") with
        {
            Customer = new CustomerInput("  jane.doe@example.COM ", " Jane Doe "),
            Currency = "USD",
            LineItems = [new LineItemInput(" SKU-A", "Widget ", 3.0m, 19.990m), new LineItemInput("SKU-B", "Gadget", 1, 5.5m)],
        };

        Assert.Equal(CreateOrderOutcome.AlreadySubmitted, service.Create(reformatted).Outcome);
    }

    [Fact]
    public void Reusing_a_reference_for_a_different_order_is_a_conflict_and_leaves_the_original_untouched()
    {
        var (service, repository, _) = TestData.NewService();
        var original = service.Create(TestData.ValidRequest("PO-42")).Order!;

        var different = TestData.ValidRequest("PO-42", new LineItemInput("SKU-Z", "Something else", 10, 1m));
        var result = service.Create(different);

        Assert.Equal(CreateOrderOutcome.ReferenceConflict, result.Outcome);
        Assert.Equal(original.Id, result.Order!.Id);
        Assert.Equal(1, repository.List(new()).TotalCount);
        Assert.Equal(original, repository.GetById(original.Id));
    }

    [Fact]
    public void Resubmitting_after_the_status_changed_still_returns_the_existing_order()
    {
        var (service, _, _) = TestData.NewService();
        var original = service.Create(TestData.ValidRequest("PO-42")).Order!;
        service.ChangeStatus(original.Id, "Confirmed");

        var again = service.Create(TestData.ValidRequest("PO-42"));

        Assert.Equal(CreateOrderOutcome.AlreadySubmitted, again.Outcome);
        Assert.Equal(OrderStatus.Confirmed, again.Order!.Status);
    }

    [Fact]
    public async Task Concurrent_submissions_of_the_same_reference_create_exactly_one_order()
    {
        var (service, repository, _) = TestData.NewService();
        using var start = new ManualResetEventSlim(false);

        var tasks = Enumerable.Range(0, 50).Select(_ => Task.Run(() =>
        {
            start.Wait();
            return service.Create(TestData.ValidRequest("PO-RACE"));
        })).ToArray();
        start.Set();
        var results = await Task.WhenAll(tasks);

        Assert.Single(results, r => r.Outcome == CreateOrderOutcome.Created);
        Assert.All(results, r => Assert.Equal(results[0].Order!.Id, r.Order!.Id));
        Assert.Equal(1, repository.List(new()).TotalCount);
    }

    // ── Listing ────────────────────────────────────────────────────────────

    [Fact]
    public void Lists_most_recent_orders_first()
    {
        var (service, _, clock) = TestData.NewService();
        service.Create(TestData.ValidRequest("PO-1"));
        clock.Advance(TimeSpan.FromMinutes(1));
        service.Create(TestData.ValidRequest("PO-2"));
        clock.Advance(TimeSpan.FromMinutes(1));
        service.Create(TestData.ValidRequest("PO-3"));

        var page = service.List(null, null, null, null);

        Assert.Equal(["PO-3", "PO-2", "PO-1"], page.Items.Select(o => o.ExternalReference));
    }

    [Fact]
    public void Orders_created_at_the_same_instant_are_still_newest_first()
    {
        var (service, _, _) = TestData.NewService();
        service.Create(TestData.ValidRequest("PO-1"));
        service.Create(TestData.ValidRequest("PO-2"));

        var page = service.List(null, null, null, null);

        Assert.Equal(["PO-2", "PO-1"], page.Items.Select(o => o.ExternalReference));
    }

    [Fact]
    public void Can_filter_by_status_and_search_and_page_results()
    {
        var (service, _, clock) = TestData.NewService();
        for (var i = 1; i <= 5; i++)
        {
            service.Create(TestData.ValidRequest($"PO-{i}"));
            clock.Advance(TimeSpan.FromSeconds(1));
        }
        var confirmed = service.List(null, "PO-2", null, null).Items.Single();
        service.ChangeStatus(confirmed.Id, "Confirmed");

        Assert.Equal(["PO-2"], service.List(OrderStatus.Confirmed, null, null, null).Items.Select(o => o.ExternalReference));

        var secondPage = service.List(null, null, page: 2, pageSize: 2);
        Assert.Equal(5, secondPage.TotalCount);
        Assert.Equal(["PO-3", "PO-2"], secondPage.Items.Select(o => o.ExternalReference));
    }

    // ── Status changes ─────────────────────────────────────────────────────

    [Fact]
    public void Walks_an_order_through_the_happy_path_and_records_history()
    {
        var (service, _, clock) = TestData.NewService();
        var order = service.Create(TestData.ValidRequest()).Order!;

        clock.Advance(TimeSpan.FromHours(1));
        var confirmed = service.ChangeStatus(order.Id, "Confirmed");
        clock.Advance(TimeSpan.FromHours(1));
        var fulfilled = service.ChangeStatus(order.Id, "fulfilled");

        Assert.Equal(ChangeStatusOutcome.Changed, confirmed.Outcome);
        Assert.Equal(ChangeStatusOutcome.Changed, fulfilled.Outcome);
        var final = service.Get(order.Id)!;
        Assert.Equal(OrderStatus.Fulfilled, final.Status);
        Assert.Equal(clock.GetUtcNow(), final.UpdatedAt);
        Assert.Equal(
            [OrderStatus.Pending, OrderStatus.Confirmed, OrderStatus.Fulfilled],
            final.StatusHistory.Select(h => h.To));
    }

    [Fact]
    public void Rejected_change_explains_why_and_leaves_the_order_unchanged()
    {
        var (service, _, _) = TestData.NewService();
        var order = service.Create(TestData.ValidRequest()).Order!;

        var result = service.ChangeStatus(order.Id, "Fulfilled");

        Assert.Equal(ChangeStatusOutcome.NotAllowed, result.Outcome);
        Assert.Contains("must be confirmed before it can be fulfilled", result.Message);
        Assert.Equal(OrderStatus.Pending, service.Get(order.Id)!.Status);
    }

    [Fact]
    public void Cancelled_orders_cannot_be_reopened()
    {
        var (service, _, _) = TestData.NewService();
        var order = service.Create(TestData.ValidRequest()).Order!;
        service.ChangeStatus(order.Id, "Cancelled");

        var result = service.ChangeStatus(order.Id, "Confirmed");

        Assert.Equal(ChangeStatusOutcome.NotAllowed, result.Outcome);
        Assert.Contains("cancelled", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Requesting_the_current_status_again_is_a_harmless_no_op()
    {
        var (service, _, _) = TestData.NewService();
        var order = service.Create(TestData.ValidRequest()).Order!;
        service.ChangeStatus(order.Id, "Confirmed");

        var again = service.ChangeStatus(order.Id, "Confirmed");

        Assert.Equal(ChangeStatusOutcome.Unchanged, again.Outcome);
        Assert.Equal(2, service.Get(order.Id)!.StatusHistory.Count);
    }

    [Fact]
    public void Unknown_status_and_unknown_order_are_reported()
    {
        var (service, _, _) = TestData.NewService();
        var order = service.Create(TestData.ValidRequest()).Order!;

        Assert.Equal(ChangeStatusOutcome.InvalidStatus, service.ChangeStatus(order.Id, "Shipped").Outcome);
        Assert.Equal(ChangeStatusOutcome.NotFound, service.ChangeStatus(Guid.NewGuid(), "Confirmed").Outcome);
    }

    [Fact]
    public async Task Concurrent_conflicting_status_changes_never_corrupt_the_order()
    {
        // Fulfil and cancel race on a confirmed order: exactly one may win, and the order must
        // end up in a final state whose history contains exactly one transition out of Confirmed.
        for (var run = 0; run < 25; run++)
        {
            var (service, _, _) = TestData.NewService();
            var order = service.Create(TestData.ValidRequest()).Order!;
            service.ChangeStatus(order.Id, "Confirmed");

            var results = await Task.WhenAll(
                Task.Run(() => service.ChangeStatus(order.Id, "Fulfilled")),
                Task.Run(() => service.ChangeStatus(order.Id, "Cancelled")));

            Assert.Single(results, r => r.Outcome == ChangeStatusOutcome.Changed);
            var final = service.Get(order.Id)!;
            Assert.True(OrderStatusRules.IsFinal(final.Status));
            Assert.Equal(3, final.StatusHistory.Count);
            Assert.Equal(3, final.Version);
        }
    }
}
