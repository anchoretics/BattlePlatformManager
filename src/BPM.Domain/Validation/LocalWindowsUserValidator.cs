using BPM.Shared;

namespace BPM.Domain.Validation;

public static class LocalWindowsUserValidator
{
    public static Result Validate(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName) || userName.Length > 20 ||
            userName != userName.Trim() || userName.EndsWith('.') ||
            userName.Any(c => char.IsControl(c) || "\"/\\[]:;|=,+*?<>@".Contains(c)))
        {
            return Result.Fail(ErrorCode.ValidationFailed,
                "请输入 1–20 个字符的本地用户名(例如 WoW2),不能包含域名、首尾空格、末尾句点或 \" / \\ [ ] : ; | = , + * ? < > @ 等字符。");
        }

        return Result.Ok();
    }
}
