using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using BPM.Application.Abstractions;
using BPM.Application.Models;
using BPM.Shared;
using Microsoft.Extensions.Logging;

namespace BPM.Infrastructure.Win32;

/// <summary>Windows Credential Manager 封装(CredWriteW / CredReadW / CredDeleteW / CredFree)。</summary>
[SupportedOSPlatform("windows")]
public class CredentialManagerService : ICredentialService
{
    private readonly ILogger<CredentialManagerService> _logger;

    public CredentialManagerService(ILogger<CredentialManagerService> logger)
    {
        _logger = logger;
    }

    public Result Save(string credentialKey, string userName, string password)
    {
        var blob = IntPtr.Zero;
        try
        {
            var passwordBytes = System.Text.Encoding.Unicode.GetBytes(password);
            blob = Marshal.AllocHGlobal(passwordBytes.Length);
            Marshal.Copy(passwordBytes, 0, blob, passwordBytes.Length);

            var credential = new NativeMethods.CREDENTIALW
            {
                Type = NativeMethods.CRED_TYPE_GENERIC,
                TargetName = credentialKey,
                UserName = userName,
                CredentialBlob = blob,
                CredentialBlobSize = (uint)passwordBytes.Length,
                Persist = NativeMethods.CRED_PERSIST_LOCAL_MACHINE,
            };

            if (!NativeMethods.CredWriteW(ref credential, 0))
            {
                var error = Marshal.GetLastWin32Error();
                _logger.LogError("CredWriteW 失败,键 {CredentialKey},Win32Error {Win32Error}", credentialKey, error);
                return Result.Fail(ErrorCode.CredentialWriteFailed, "写入 Windows 凭据失败。", error);
            }

            Array.Clear(passwordBytes);
            return Result.Ok();
        }
        finally
        {
            if (blob != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(blob);
            }
        }
    }

    public Result<StoredCredential> Get(string credentialKey)
    {
        var credentialPtr = IntPtr.Zero;
        try
        {
            if (!NativeMethods.CredReadW(credentialKey, NativeMethods.CRED_TYPE_GENERIC, 0, out credentialPtr))
            {
                var error = Marshal.GetLastWin32Error();
                return error == NativeMethods.ERROR_NOT_FOUND
                    ? Result<StoredCredential>.Fail(
                        ErrorCode.CredentialNotFound, "未找到已保存的密码,请重新编辑账户并输入密码。", error)
                    : Result<StoredCredential>.Fail(
                        ErrorCode.Unexpected, "读取 Windows 凭据失败。", error);
            }

            var credential = Marshal.PtrToStructure<NativeMethods.CREDENTIALW>(credentialPtr);
            var password = credential.CredentialBlobSize > 0
                ? Marshal.PtrToStringUni(credential.CredentialBlob, (int)(credential.CredentialBlobSize / 2))
                : string.Empty;

            return Result<StoredCredential>.Ok(new StoredCredential(credential.UserName ?? string.Empty, password ?? string.Empty));
        }
        finally
        {
            if (credentialPtr != IntPtr.Zero)
            {
                NativeMethods.CredFree(credentialPtr);
            }
        }
    }

    public Result Delete(string credentialKey)
    {
        if (NativeMethods.CredDeleteW(credentialKey, NativeMethods.CRED_TYPE_GENERIC, 0))
        {
            return Result.Ok();
        }

        var error = Marshal.GetLastWin32Error();
        if (error == NativeMethods.ERROR_NOT_FOUND)
        {
            return Result.Ok();
        }

        _logger.LogError("CredDeleteW 失败,键 {CredentialKey},Win32Error {Win32Error}", credentialKey, error);
        return Result.Fail(ErrorCode.Unexpected, "删除 Windows 凭据失败。", error);
    }

    public bool Exists(string credentialKey)
    {
        if (!NativeMethods.CredReadW(credentialKey, NativeMethods.CRED_TYPE_GENERIC, 0, out var ptr))
        {
            return false;
        }

        NativeMethods.CredFree(ptr);
        return true;
    }
}
