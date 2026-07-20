namespace BPM.Shared;

/// <summary>统一错误码,用于 UI 提示与日志归类。</summary>
public enum ErrorCode
{
    None = 0,
    ValidationFailed,
    ConfigNotFound,
    ConfigCorrupted,
    AccountNotFound,
    CredentialNotFound,
    CredentialWriteFailed,
    InvalidCredentials,
    ExecutableNotFound,
    LogonServiceUnavailable,
    LogonNotGranted,
    LaunchFailed,
    ShortcutFailed,
    Unexpected,
}

/// <summary>轻量结果类型:携带成功/失败、错误码、面向用户的消息与底层 Win32 错误码。</summary>
public class Result
{
    public bool IsSuccess { get; }
    public ErrorCode Code { get; }
    public string Message { get; }
    public int Win32Error { get; }

    protected Result(bool isSuccess, ErrorCode code, string message, int win32Error)
    {
        IsSuccess = isSuccess;
        Code = code;
        Message = message;
        Win32Error = win32Error;
    }

    public static Result Ok() => new(true, ErrorCode.None, string.Empty, 0);

    public static Result Fail(ErrorCode code, string message, int win32Error = 0) =>
        new(false, code, message, win32Error);
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    /// <summary>仅在 <see cref="Result.IsSuccess"/> 为 true 时可用。</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Result is a failure ({Code}): {Message}");

    private Result(bool isSuccess, T? value, ErrorCode code, string message, int win32Error)
        : base(isSuccess, code, message, win32Error)
    {
        _value = value;
    }

    public static Result<T> Ok(T value) => new(true, value, ErrorCode.None, string.Empty, 0);

    public static new Result<T> Fail(ErrorCode code, string message, int win32Error = 0) =>
        new(false, default, code, message, win32Error);

    public static Result<T> Fail(Result other) =>
        new(false, default, other.Code, other.Message, other.Win32Error);
}
