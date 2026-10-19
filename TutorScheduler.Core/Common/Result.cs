namespace TutorScheduler.Core.Common;

public enum ResultStatus
{
    Success,
    NotFound,
    Conflict,
    ValidationError,
    Unauthorized,
    Forbidden
}

public class Result
{
    public ResultStatus Status { get; }
    public string? Error { get; }

    private Result(ResultStatus status, string? error)
    {
        Status = status;
        Error = error;
    }

    public bool IsSuccess => Status == ResultStatus.Success;

    public static Result Success() => new(ResultStatus.Success, null);
    public static Result Fail(string error) => new(ResultStatus.ValidationError, error);
    public static Result Conflict(string error) => new(ResultStatus.Conflict, error);
    public static Result NotFound(string error) => new(ResultStatus.NotFound, error);
    public static Result Forbidden(string error) => new(ResultStatus.Forbidden, error);
}

public class Result<T>
{
    public ResultStatus Status { get; }
    public string? Error { get; }
    public T? Value { get; }

    private Result(ResultStatus status, string? error, T? value)
    {
        Status = status;
        Error = error;
        Value = value;
    }

    public bool IsSuccess => Status == ResultStatus.Success;

    public static Result<T> Success(T value) => new(ResultStatus.Success, null, value);
    public static Result<T> Fail(string error) => new(ResultStatus.ValidationError, error, default);
    public static Result<T> Conflict(string error) => new(ResultStatus.Conflict, error, default);
    public static Result<T> NotFound(string error) => new(ResultStatus.NotFound, error, default);
    public static Result<T> Unauthorized(string error) => new(ResultStatus.Unauthorized, error, default);
    public static Result<T> Forbidden(string error) => new(ResultStatus.Forbidden, error, default);
}
