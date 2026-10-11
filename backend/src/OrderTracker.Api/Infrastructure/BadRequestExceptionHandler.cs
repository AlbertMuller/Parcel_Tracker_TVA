using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace OrderTracker.Api.Infrastructure;

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
            },
        });
    }
}
