namespace MovieDB.Common;

public enum ErrorKind
{
    Validation = 0,
    NotFound = 1,
    Conflict = 2,
    Forbidden = 3,
}

/// <summary>Failure description. <see cref="Fields"/> maps a property name (or "" for the form) to messages.</summary>
public sealed record Error(ErrorKind Kind, string Message, IReadOnlyDictionary<string, string[]>? Fields = null)
{
    public static Error Validation(string field, string message) =>
        new(ErrorKind.Validation, message, new Dictionary<string, string[]> { [field] = [message] });

    public static Error Validation(IReadOnlyDictionary<string, string[]> fields) =>
        new(ErrorKind.Validation, "Validation failed.", fields);

    public static Error NotFound(string message = "Not found.") => new(ErrorKind.NotFound, message);

    public static Error Conflict(string message) => new(ErrorKind.Conflict, message);

    public static Error Forbidden(string message) => new(ErrorKind.Forbidden, message);
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
