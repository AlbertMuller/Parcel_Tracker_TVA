using OrderTracker.Api.Application;
using OrderTracker.Api.Contracts;
using OrderTracker.Api.Domain;

namespace OrderTracker.Api.Endpoints;

public static class OrderEndpoints
{
    /// <summary>Set on a 200 response to POST /api/orders when the order already existed.</summary>
    public const string ReplayHeader = "Idempotent-Replay";

    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/api/orders").WithTags("Orders");

        orders.MapPost("/", CreateOrder)
            .WithSummary("Submit an order. Resubmitting the same externalReference never creates a duplicate.")
            .Produces<OrderResponse>(StatusCodes.Status201Created)
            .Produces<OrderResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        orders.MapGet("/", ListOrders)
            .WithSummary("List orders, newest first. Optional filters: status, search (reference, customer name or email).")
            .Produces<PagedResponse<OrderSummaryResponse>>()
            .ProducesValidationProblem();

        orders.MapGet("/{id:guid}", GetOrder)
            .WithName(nameof(GetOrder))
            .Produces<OrderResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        orders.MapPatch("/{id:guid}/status", ChangeStatus)
            .WithSummary("Change an order's status. Pending → Confirmed → Fulfilled; Pending/Confirmed → Cancelled.")
            .Produces<OrderResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        orders.MapPatch("/{id:guid}/currency", ChangeCurrency)
            .WithSummary("Re-price a Pending order in another currency at the latest exchange rate.")
            .Produces<OrderResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static IResult CreateOrder(CreateOrderRequest request, OrderService service, HttpContext http, LinkGenerator links)
    {
        var result = service.Create(request);
        switch (result.Outcome)
        {
            case CreateOrderOutcome.Created:
                return TypedResults.CreatedAtRoute(OrderResponse.From(result.Order!), nameof(GetOrder), new { id = result.Order!.Id });

            case CreateOrderOutcome.AlreadySubmitted:
                http.Response.Headers[ReplayHeader] = "true";
                http.Response.Headers.Location = links.GetUriByName(http, nameof(GetOrder), new { id = result.Order!.Id });
                return TypedResults.Ok(OrderResponse.From(result.Order));

            case CreateOrderOutcome.ReferenceConflict:
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "External reference already used",
                    detail: $"Reference '{result.Order!.ExternalReference}' was already used for order #{result.Order.Number} " +
                            "with different details. No new order was created. Use a new reference for a different order.",
                    extensions: new Dictionary<string, object?>
                    {
                        ["existingOrderId"] = result.Order.Id,
                        ["existingOrderNumber"] = result.Order.Number,
                    });

            default:
                return TypedResults.ValidationProblem(result.Errors!.ToDictionary(), title: "The order has validation errors.");
        }
    }

    private static IResult ListOrders(OrderService service, string? status, string? search, int? page, int? pageSize)
    {
        OrderStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!OrderStatusRules.TryParse(status, out var parsed))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["status"] = [$"'{status}' is not a valid status. Valid statuses: {string.Join(", ", Enum.GetNames<OrderStatus>())}."],
                });
            }
            statusFilter = parsed;
        }

        var result = service.List(statusFilter, search, page, pageSize);
        var effectivePage = Math.Max(1, page ?? 1);
        var effectiveSize = Math.Clamp(pageSize ?? 50, 1, OrderService.MaxPageSize);

        return TypedResults.Ok(new PagedResponse<OrderSummaryResponse>(
            result.Items.Select(OrderSummaryResponse.From).ToList(),
            effectivePage,
            effectiveSize,
            result.TotalCount));
    }

    private static IResult GetOrder(Guid id, OrderService service) =>
        service.Get(id) is { } order
            ? TypedResults.Ok(OrderResponse.From(order))
            : NotFound(id);

    private static IResult ChangeStatus(Guid id, ChangeStatusRequest request, OrderService service)
    {
        var result = service.ChangeStatus(id, request.Status);
        return result.Outcome switch
        {
            ChangeStatusOutcome.Changed or ChangeStatusOutcome.Unchanged => TypedResults.Ok(OrderResponse.From(result.Order!)),
            ChangeStatusOutcome.NotFound => NotFound(id),
            ChangeStatusOutcome.InvalidStatus => TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["status"] = [result.Message!] }),
            _ => TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Status change not allowed",
                detail: result.Message,
                extensions: new Dictionary<string, object?>
                {
                    ["currentStatus"] = result.Order!.Status.ToString(),
                    ["allowedTransitions"] = OrderStatusRules.AllowedTransitionsFrom(result.Order.Status).Select(s => s.ToString()).ToArray(),
                }),
        };
    }

    private static async Task<IResult> ChangeCurrency(Guid id, ChangeCurrencyRequest request, OrderService service, CancellationToken cancellationToken)
    {
        var result = await service.ChangeCurrencyAsync(id, request.Currency, cancellationToken);
        return result.Outcome switch
        {
            ChangeCurrencyOutcome.Changed or ChangeCurrencyOutcome.Unchanged => TypedResults.Ok(OrderResponse.From(result.Order!)),
            ChangeCurrencyOutcome.NotFound => NotFound(id),
            ChangeCurrencyOutcome.InvalidCurrency => TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["currency"] = [result.Message!] }),
            ChangeCurrencyOutcome.NotAllowed => TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict, title: "Currency change not allowed", detail: result.Message),
            ChangeCurrencyOutcome.RateNotAvailable => TypedResults.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity, title: "No exchange rate", detail: result.Message),
            _ => TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable, title: "Exchange rates unavailable", detail: result.Message),
        };
    }

    private static IResult NotFound(Guid id) =>
        TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Order not found", detail: $"No order exists with id {id}.");
}
