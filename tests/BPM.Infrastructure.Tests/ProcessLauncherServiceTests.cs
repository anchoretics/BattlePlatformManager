using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using BPM.Application.Models;
using BPM.Infrastructure.Win32;
using BPM.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BPM.Infrastructure.Tests;

/// <summary>使用原生 API 替身验证启动边界,不登录真实用户、不启动战网。</summary>
[SupportedOSPlatform("windows")]
public class ProcessLauncherServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly LaunchRequest _request;
    private readonly FakeProcessCreationApi _processApi = new();
    private readonly ProcessLauncherService _service;

    public ProcessLauncherServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"bpm-launch-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
        var executable = Path.Combine(_tempDirectory, "测试 Launcher.exe");
        File.WriteAllText(executable, "stub");
        _request = new LaunchRequest
        {
            User = "WoW1",
            Domain = ".",
            Password = "synthetic-test-password",
            ExecutablePath = executable,
            Arguments = "--exec=\"launch WoW\"",
        };
        _service = new ProcessLauncherService(NullLogger<ProcessLauncherService>.Instance, _processApi);
    }

    public void Dispose()
    {
        File.Delete(_request.ExecutablePath);
        Directory.Delete(_tempDirectory);
    }

    [Fact]
    public void Launch_DelegatesProfileAndEnvironmentCreationToWindows()
    {
        var result = _service.Launch(_request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(4242);
        // 开机后配置文件尚未加载时,不能提前生成环境块,也不能传入 BPM 自身的环境。
        _processApi.LogonFlags.Should().Be(NativeMethods.LOGON_WITH_PROFILE);
        _processApi.Environment.Should().Be(IntPtr.Zero);
        _processApi.User.Should().Be(_request.User);
        _processApi.Domain.Should().Be(_request.Domain);
        _processApi.Password.Should().Be(_request.Password);
        _processApi.ClosedHandles.Should().BeEquivalentTo(new[] { new IntPtr(101), new IntPtr(102) });
    }

    [Fact]
    public void Launch_PreservesExecutableArgumentsAndWorkingDirectory()
    {
        var result = _service.Launch(_request);

        result.IsSuccess.Should().BeTrue();
        _processApi.ApplicationName.Should().Be(_request.ExecutablePath);
        _processApi.CommandLine.Should().Be($"\"{_request.ExecutablePath}\" {_request.Arguments}");
        _processApi.WorkingDirectory.Should().Be(_tempDirectory);
        _processApi.StartupInfoSize.Should().Be(Marshal.SizeOf<NativeMethods.STARTUPINFO>());
    }

    [Fact]
    public void Launch_UsesExplicitWorkingDirectory()
    {
        var result = _service.Launch(_request with { WorkingDirectory = Path.GetTempPath() });

        result.IsSuccess.Should().BeTrue();
        _processApi.WorkingDirectory.Should().Be(Path.GetTempPath());
    }

    [Fact]
    public void Launch_MissingExecutable_DoesNotCallWindows()
    {
        File.Delete(_request.ExecutablePath);

        var result = _service.Launch(_request);

        result.Code.Should().Be(ErrorCode.ExecutableNotFound);
        _processApi.Calls.Should().Be(0);
        _processApi.ClosedHandles.Should().BeEmpty();
    }

    [Theory]
    [InlineData(1326, ErrorCode.InvalidCredentials)]
    [InlineData(1327, ErrorCode.InvalidCredentials)]
    [InlineData(1385, ErrorCode.LogonNotGranted)]
    [InlineData(2, ErrorCode.ExecutableNotFound)]
    [InlineData(3, ErrorCode.ExecutableNotFound)]
    [InlineData(1058, ErrorCode.LogonServiceUnavailable)]
    [InlineData(1062, ErrorCode.LogonServiceUnavailable)]
    [InlineData(5, ErrorCode.LaunchFailed)]
    public void Launch_WindowsFailure_PreservesAuthenticationAndLaunchErrors(int win32Error, ErrorCode expectedCode)
    {
        _processApi.Error = win32Error;

        var result = _service.Launch(_request);

        result.IsSuccess.Should().BeFalse();
        result.Code.Should().Be(expectedCode);
        result.Win32Error.Should().Be(win32Error);
        result.Message.Should().NotBeNullOrWhiteSpace();
        result.Message.Should().NotContain(_request.Password);
        _processApi.Calls.Should().Be(1);
        _processApi.ClosedHandles.Should().BeEmpty();
    }

    private sealed class FakeProcessCreationApi : IProcessCreationApi
    {
        public int Calls { get; private set; }
        public int Error { get; set; }
        public string? User { get; private set; }
        public string? Domain { get; private set; }
        public string? Password { get; private set; }
        public int LogonFlags { get; private set; }
        public IntPtr Environment { get; private set; } = new(-1);
        public string? ApplicationName { get; private set; }
        public string? CommandLine { get; private set; }
        public string? WorkingDirectory { get; private set; }
        public int StartupInfoSize { get; private set; }
        public List<IntPtr> ClosedHandles { get; } = new();

        public bool CreateProcessWithLogon(
            string user,
            string domain,
            string password,
            int logonFlags,
            string applicationName,
            StringBuilder commandLine,
            int creationFlags,
            IntPtr environment,
            string? workingDirectory,
            ref NativeMethods.STARTUPINFO startupInfo,
            out NativeMethods.PROCESS_INFORMATION processInfo)
        {
            Calls++;
            User = user;
            Domain = domain;
            Password = password;
            LogonFlags = logonFlags;
            Environment = environment;
            ApplicationName = applicationName;
            CommandLine = commandLine.ToString();
            WorkingDirectory = workingDirectory;
            StartupInfoSize = startupInfo.cb;
            processInfo = Error == 0
                ? new NativeMethods.PROCESS_INFORMATION
                {
                    dwProcessId = 4242,
                    hProcess = new IntPtr(101),
                    hThread = new IntPtr(102),
                }
                : default;
            return Error == 0;
        }

        public int GetLastError() => Error;

        public void CloseHandle(IntPtr handle) => ClosedHandles.Add(handle);
    }
}
