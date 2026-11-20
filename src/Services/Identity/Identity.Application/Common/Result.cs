namespace Identity.Application.Common;

public enum ErrorType { Validation, Unauthorized, Conflict, NotFound }

public sealed record Error(ErrorType Type, string Code, string Description);

public sealed class Result<T>
{
    private Result(T? value, IReadOnlyList<Error> errors)
    {
        Value = value;
        Errors = errors;
    }

    public T? Value { get; }
    public IReadOnlyList<Error> Errors { get; }
    public bool IsSuccess => Errors.Count == 0;

    public static Result<T> Success(T value) => new(value, []);
    public static Result<T> Failure(params Error[] errors) => new(default, errors);
    public static Result<T> Failure(IEnumerable<Error> errors) => new(default, errors.ToList());
}