using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using OrderTracker.Domain.Common;

namespace OrderTracker.Api.ErrorHandling;

public static class ProblemMapping
{
    /// <summary>
    /// Turns a business <see cref="Error"/> into RFC 9457 ProblemDetails. Every problem carries the stable
    /// <c>code</c> (e.g. <c>order.status.final</c>) plus the error's metadata as extensions.
    /// </summary>
    public static IResult ToProblem(this Error error)
    {
        var extensions = new Dictionary<string, object?>(error.Metadata) { ["code"] = error.Code };

        if (error is ValidationError validation)
            return TypedResults.ValidationProblem(validation.Errors.ToDictionary(), title: error.Title, extensions: extensions);

        return TypedResults.Problem(
            statusCode: StatusCodeFor(error.Type),
            title: error.Title,
            detail: error.Message,
            extensions: extensions);
    }

    public static int StatusCodeFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Unprocessable => StatusCodes.Status422UnprocessableEntity,
        ErrorType.Unavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError,
    };
}

/// <summary>Turns malformed requests (e.g. invalid JSON) into a 400 problem response instead of a 500.</summary>
public sealed class BadRequestExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException badRequest)
            return false;

        httpContext.Response.StatusCode = badRequest.StatusCode;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = badRequest.StatusCode,
                Title = "The request could not be read.",
                Detail = badRequest.InnerException?.Message ?? badRequest.Message,
                Extensions = { ["code"] = "request.malformed" },
            },
        });
    }
}
