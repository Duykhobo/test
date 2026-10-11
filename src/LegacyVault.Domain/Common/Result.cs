namespace LegacyVault.Domain.Common;

/// <summary>
/// Kết quả của một thao tác: hoặc thành công, hoặc thất bại kèm Error.
/// </summary>
/// <example>
/// Result result = Result.Success();
/// bool succeeded = result.IsSuccess;
/// </example>
public class Result
{
    /// <summary>Khởi tạo kết quả từ lỗi; null biểu thị thành công.</summary>
    /// <param name="error">Lỗi nghiệp vụ hoặc null khi thành công.</param>
    protected Result(Error? error) => Error = error;

    /// <summary>Lỗi nghiệp vụ khi thất bại; null khi thành công.</summary>
    public Error? Error { get; }

    /// <summary>Kết quả không chứa lỗi nghiệp vụ.</summary>
    public bool IsSuccess => Error is null;

    /// <summary>Kết quả chứa lỗi nghiệp vụ.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>Tạo kết quả thành công không kèm giá trị.</summary>
    /// <returns>Kết quả có IsSuccess bằng true.</returns>
    public static Result Success() => new(null);

    /// <summary>Tạo kết quả thất bại từ lỗi nghiệp vụ.</summary>
    /// <param name="error">Lỗi bắt buộc khác null theo hợp đồng đầu vào.</param>
    /// <returns>Kết quả mang lỗi đã cung cấp.</returns>
    public static Result Failure(Error error) => new(error);

    /// <summary>Chuyển lỗi nghiệp vụ thành kết quả thất bại.</summary>
    /// <param name="error">Lỗi bắt buộc khác null theo hợp đồng đầu vào.</param>
    /// <returns>Kết quả mang lỗi đã cung cấp.</returns>
    public static implicit operator Result(Error error) => Failure(error);
}

/// <summary>
/// Kết quả có kèm giá trị khi thành công.
/// </summary>
/// <typeparam name="T">Kiểu giá trị trả về khi thao tác thành công.</typeparam>
public sealed class Result<T> : Result
{
    private const string FailedValueAccessMessage = "Không thể đọc Value của một Result thất bại.";

    /// <summary>Giá trị chỉ có ý nghĩa khi kết quả thành công.</summary>
    private readonly T? _value;

    /// <summary>Khởi tạo kết quả thành công kèm giá trị.</summary>
    /// <param name="value">Giá trị trả về của thao tác.</param>
    private Result(T value) : base(null) => _value = value;

    /// <summary>Khởi tạo kết quả thất bại kèm lỗi nghiệp vụ.</summary>
    /// <param name="error">Lỗi bắt buộc khác null theo hợp đồng đầu vào.</param>
    private Result(Error error) : base(error) { }

    /// <summary>Chỉ đọc khi IsSuccess. Đọc khi thất bại là lỗi lập trình nên throw.</summary>
    /// <exception cref="InvalidOperationException">Đọc giá trị của kết quả thất bại.</exception>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException(FailedValueAccessMessage);

    /// <summary>Tạo kết quả thành công kèm giá trị.</summary>
    /// <param name="value">Giá trị trả về của thao tác.</param>
    /// <returns>Kết quả có IsSuccess bằng true và Value đã cung cấp.</returns>
    public static Result<T> Success(T value) => new(value);

    /// <summary>Tạo kết quả thất bại không có giá trị để đọc.</summary>
    /// <param name="error">Lỗi bắt buộc khác null theo hợp đồng đầu vào.</param>
    /// <returns>Kết quả mang lỗi đã cung cấp.</returns>
    public new static Result<T> Failure(Error error) => new(error);

    /// <summary>Chuyển giá trị thành kết quả thành công.</summary>
    /// <param name="value">Giá trị trả về của thao tác.</param>
    /// <returns>Kết quả thành công kèm giá trị.</returns>
    public static implicit operator Result<T>(T value) => Success(value);

    /// <summary>Chuyển lỗi nghiệp vụ thành kết quả thất bại có kiểu giá trị.</summary>
    /// <param name="error">Lỗi bắt buộc khác null theo hợp đồng đầu vào.</param>
    /// <returns>Kết quả thất bại; không được đọc Value.</returns>
    public static implicit operator Result<T>(Error error) => Failure(error);
}
