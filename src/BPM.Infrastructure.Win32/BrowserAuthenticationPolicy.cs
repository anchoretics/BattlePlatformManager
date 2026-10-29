using System.Security.Cryptography;
using System.Text;

namespace BPM.Infrastructure.Win32;

/// <summary>只接受已经验证过的国服浏览器交接入口；绝不重建或修改签名参数。</summary>
internal static class BrowserAuthenticationPolicy
{
    internal const int MaxUrlBytes = 16384;
    private static readonly string[] RequiredKeys = ["flow", "ref", "return", "returnHttps", "secure", "state"];

    internal static bool IsAllowed(string value)
    {
        if (value.Length > MaxUrlBytes || Encoding.UTF8.GetByteCount(value) > MaxUrlBytes ||
            value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '"' or '\\') ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 || uri.UserInfo.Length != 0 ||
            !uri.Host.Equals("account.battlenet.com.cn", StringComparison.OrdinalIgnoreCase) || uri.Fragment.Length != 0)
            return false;

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in uri.Query.TrimStart('?').Split('&'))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length != 2 || parts[1].Length == 0 || !keys.Add(parts[0])) return false;
        }
        return RequiredKeys.All(keys.Contains);
    }

    internal static bool IsFresh(DateTime eventUtc, DateTime nowUtc) =>
        nowUtc - eventUtc <= TimeSpan.FromSeconds(5) && eventUtc - nowUtc <= TimeSpan.FromSeconds(1);

    internal static string Fingerprint(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    internal static string? Extract(string shellName)
    {
        if (shellName.Length > MaxUrlBytes + 2) return null;
        // 只剥离成对外层引号；不以正则截断合法的单引号/括号等签名参数。
        if (shellName.Length >= 2 && shellName[0] is '\'' or '"' && shellName[^1] == shellName[0])
            shellName = shellName[1..^1];
        return shellName.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? shellName : null;
    }
}
