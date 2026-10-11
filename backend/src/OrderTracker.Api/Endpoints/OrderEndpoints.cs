using OrderTracker.Api.Contracts;
using OrderTracker.Api.ErrorHandling;
using OrderTracker.Application.Orders;
using OrderTracker.Application.Orders.ChangeOrderStatus;
using OrderTracker.Application.Orders.ConvertOrderCurrency;
using OrderTracker.Application.Orders.GetOrder;
using OrderTracker.Application.Orders.ListOrders;
using OrderTracker.Application.Orders.SubmitOrder;

namespace OrderTracker.Api.Endpoints;

/// <summary>HTTP adapter for the order use cases: translate the request, call the handler, translate the result.</summary>
public static class OrderEndpoints
{
    /// <summary>Set on a 200 response to POST /api/orders when the order already existed.</summary>
    public const string ReplayHeader = "Idempotent-Replay";

    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/api/orders").WithTags("Orders");

        orders.MapPost("/", SubmitOrder)
            .WithSummary("Submit an order. Resubmitting the same externalReference never creates a duplicate.")
            .Produces<OrderDto>(StatusCodes.Status201Created)
            .Produces<OrderDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        orders.MapGet("/", ListOrders)
            .WithSummary("List orders, newest first. Optional filters: status, search (reference, customer name or email).")
            .Produces<PagedResult<OrderSummaryDto>>()
            .ProducesValidationProblem();

        orders.MapGet("/{id:guid}", GetOrder)
            .WithName(nameof(GetOrder))
            .Produces<OrderDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        orders.MapPatch("/{id:guid}/status", ChangeStatus)
            .WithSummary("Change an order's status. Pending → Confirmed → Fulfilled; Pending/Confirmed → Cancelled.")
            .Produces<OrderDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        orders.MapPatch("/{id:guid}/currency", ConvertCurrency)
            .WithSummary("Re-price a Pending order in another currency at the latest exchange rate.")
            .Produces<OrderDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> SubmitOrder(
        CreateOrderRequest request, SubmitOrderHandler handler, HttpContext http, LinkGenerator links, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToCommand(), cancellationToken);
        if (result.IsFailure)
            return result.Error.ToProblem();

        var (order, alreadySubmitted) = result.Value;
        if (!alreadySubmitted)
            return TypedResults.CreatedAtRoute(order, nameof(GetOrder), new { id = order.Id });

        http.Response.Headers[ReplayHeader] = "true";
        http.Response.Headers.Location = links.GetUriByName(http, nameof(GetOrder), new { id = order.Id });
        return TypedResults.Ok(order);
    }

    private static async Task<IResult> ListOrders(
        ListOrdersHandler handler, string? status, string? search, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ListOrdersQuery(status, search, page, pageSize), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> GetOrder(Guid id, GetOrderHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetOrderQuery(id), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> ChangeStatus(
        Guid id, ChangeStatusRequest request, ChangeOrderStatusHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ChangeOrderStatusCommand(id, request.Status), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> ConvertCurrency(
        Guid id, ChangeCurrencyRequest request, ConvertOrderCurrencyHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ConvertOrderCurrencyCommand(id, request.Currency), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }
}
