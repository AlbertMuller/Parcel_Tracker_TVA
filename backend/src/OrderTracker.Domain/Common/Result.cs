namespace OrderTracker.Domain.Common;

/// <summary>What kind of failure an <see cref="Error"/> is. The API maps each kind to an HTTP status.</summary>
public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Unprocessable,
    Unavailable,
}

/// <summary>
/// An expected business failure. <see cref="Code"/> is stable and machine-readable (e.g. <c>order.status.final</c>);
/// <see cref="Title"/> and <see cref="Message"/> are written for the people using the app.
/// </summary>
public record Error(string Code, string Title, string Message, ErrorType Type)
{
    /// <summary>Extra facts a client can act on, e.g. the id of the existing order in a conflict.</summary>
    public IReadOnlyDictionary<string, object?> Metadata { get; init; } = new Dictionary<string, object?>();
}

/// <summary>One or more invalid fields. Keys are JSON paths such as <c>lineItems[1].quantity</c>.</summary>
public sealed record ValidationError(IReadOnlyDictionary<string, string[]> Errors)
    : Error("validation_failed", "One or more fields are invalid.", "One or more fields are invalid.", ErrorType.Validation)
{
    public static ValidationError For(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}

/// <summary>Outcome of an operation that can fail for an expected business reason.</summary>
public class Result
{
    private readonly Error? _error;

    protected Result(Error? error) => _error = error;

    public bool IsSuccess => _error is null;
    public bool IsFailure => !IsSuccess;

    public Error Error => _error ?? throw new InvalidOperationException("A successful result has no error.");

    public static Result Success() => new(null);
    public static Result Failure(Error error) => new(error);

    public static implicit operator Result(Error error) => Failure(error);
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T value) : base(null) => _value = value;
    private Result(Error error) : base(error) { }

    public T Value => IsSuccess ? _value! : throw new InvalidOperationException($"A failed result has no value ({Error.Code}).");

    public static Result<T> Success(T value) => new(value);
    public static new Result<T> Failure(Error error) => new(error);

    public static implicit operator Result<T>(T value) => Success(value);
    public static implicit operator Result<T>(Error error) => Failure(error);
}
