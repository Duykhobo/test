namespace LegacyVault.Domain.Common;

/// <summary>
/// Kết quả của một thao tác: hoặc thành công, hoặc thất bại kèm Error.
/// </summary>
/// <example>
/// Result r = user.Activate();
/// if (r.IsFailure) return r.Error;   // trả lỗi lên trên, không throw
/// </example>
public class Result
{
    protected Result(Error? error) => Error = error;

    public Error? Error { get; }
    public bool IsSuccess => Error is null;
    public bool IsFailure => !IsSuccess;

    public static Result Success() => new(null);
    public static Result Failure(Error error) => new(error);

    // Cho phép viết "return SomeErrors.NotFound;" trong method trả về Result.
    public static implicit operator Result(Error error) => Failure(error);
}

/// <summary>
/// Kết quả có kèm giá trị khi thành công.
/// </summary>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T value) : base(null) => _value = value;
    private Result(Error error) : base(error) { }

    /// <summary>Chỉ đọc khi IsSuccess. Đọc khi thất bại là lỗi lập trình nên throw.</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Không thể đọc Value của một Result thất bại.");

    public static Result<T> Success(T value) => new(value);
    public new static Result<T> Failure(Error error) => new(error);

    // "return dto;" hoặc "return AuthErrors.X;" đều tự chuyển thành Result<T>.
    public static implicit operator Result<T>(T value) => Success(value);
    public static implicit operator Result<T>(Error error) => Failure(error);
}
