using Xunit;

namespace BPM.Infrastructure.Tests;

/// <summary>依赖真实 Windows API 的集成测试:非 Windows 环境自动跳过(CI 使用 windows-latest,会真实执行)。</summary>
public sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "仅在 Windows 上运行。";
        }
    }
}
