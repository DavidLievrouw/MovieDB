namespace MovieDB.Common;

public enum ErrorKind
{
    Validation = 0,
    NotFound = 1,
    Conflict = 2,
    Forbidden = 3,

    /// <summary>An optional external service is not set up (for example, no API token).</summary>
    NotConfigured = 4,

    /// <summary>An external service could not be reached, timed out or failed.</summary>
    Unavailable = 5,

    /// <summary>An external service refused the request because of too many requests.</summary>
    RateLimited = 6,

    /// <summary>An external service answered with data that could not be understood.</summary>
    InvalidResponse = 7,

    /// <summary>An external service rejected our credentials (a configuration problem, not a transient failure).</summary>
    Misconfigured = 8,
}

/// <summary>Failure description. <see cref="Fields"/> maps a property name (or "" for the form) to messages.</summary>
/// <param name="Kind">The category of the failure.</param>
/// <param name="Message">A general description of the failure.</param>
/// <param name="Fields">Messages per property name (or "" for the form); null when not field-specific.</param>
public sealed record Error(ErrorKind Kind, string Message, IReadOnlyDictionary<string, string[]>? Fields = null)
{
    public static Error Validation(string field, string message) =>
        new(ErrorKind.Validation, message, new Dictionary<string, string[]> { [field] = [message] });

    public static Error Validation(IReadOnlyDictionary<string, string[]> fields) =>
        new(ErrorKind.Validation, "Validation failed.", fields);

    public static Error NotFound(string message = "Not found.") => new(ErrorKind.NotFound, message);

    public static Error Conflict(string message) => new(ErrorKind.Conflict, message);

    public static Error Forbidden(string message) => new(ErrorKind.Forbidden, message);

    /// <summary>Creates an error for an optional external service that is not set up.</summary>
    /// <param name="message">A short technical description (never shown to the user).</param>
    /// <returns>The error.</returns>
    public static Error NotConfigured(string message) => new(ErrorKind.NotConfigured, message);

    /// <summary>Creates an error for an external service that could not be reached or failed.</summary>
    /// <param name="message">A short technical description (never shown to the user).</param>
    /// <returns>The error.</returns>
    public static Error Unavailable(string message) => new(ErrorKind.Unavailable, message);

    /// <summary>Creates an error for an external service that refused the request because of too many requests.</summary>
    /// <param name="message">A short technical description (never shown to the user).</param>
    /// <returns>The error.</returns>
    public static Error RateLimited(string message) => new(ErrorKind.RateLimited, message);

    /// <summary>Creates an error for an external service that answered with data that could not be understood.</summary>
    /// <param name="message">A short technical description (never shown to the user).</param>
    /// <returns>The error.</returns>
    public static Error InvalidResponse(string message) => new(ErrorKind.InvalidResponse, message);

    /// <summary>Creates an error for an external service that rejected our credentials.</summary>
    /// <param name="message">A short technical description (never shown to the user).</param>
    /// <returns>The error.</returns>
    public static Error Misconfigured(string message) => new(ErrorKind.Misconfigured, message);
}

public readonly record struct Result<T>
{
    private readonly T? _value;
    private readonly Error? _error;

    private Result(T? value, Error? error)
    {
        _value = value;
        _error = error;
    }

    public bool IsSuccess => _error is null;
    public T Value => _error is null ? _value! : throw new InvalidOperationException(_error.Message);
    public Error Error => _error ?? throw new InvalidOperationException("Result is successful.");

    public static Result<T> Ok(T value) => new(value, null);

    public static Result<T> Fail(Error error) => new(default, error);

    public static implicit operator Result<T>(Error error) => Fail(error);
}

public static class Result
{
    public static Result<Unit> Ok() => Result<Unit>.Ok(Unit.Value);
}

public readonly record struct Unit
{
    public static readonly Unit Value;
}
