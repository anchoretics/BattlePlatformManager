using BPM.Application.Abstractions;
using BPM.Application.Models;
using BPM.Domain.Models;
using BPM.Shared;

namespace BPM.Application.Tests;

/// <summary>内存版配置存储。</summary>
public class FakeConfigService : IConfigService
{
    public Config Config { get; set; } = new();
    public Result? NextLoadFailure { get; set; }

    public Result<Config> Load() =>
        NextLoadFailure is null ? Result<Config>.Ok(Config) : Result<Config>.Fail(NextLoadFailure);

    public Result Save(Config config)
    {
        Config = config;
        return Result.Ok();
    }

    public Result Export(string targetPath) => Result.Ok();

    public Result<Config> Import(string sourcePath) => Result<Config>.Ok(Config);
}

/// <summary>内存版凭据存储。</summary>
public class FakeCredentialService : ICredentialService
{
    public Dictionary<string, StoredCredential> Store { get; } = new();

    public Result Save(string credentialKey, string userName, string password)
    {
        Store[credentialKey] = new StoredCredential(userName, password);
        return Result.Ok();
    }

    public Result<StoredCredential> Get(string credentialKey) =>
        Store.TryGetValue(credentialKey, out var credential)
            ? Result<StoredCredential>.Ok(credential)
            : Result<StoredCredential>.Fail(ErrorCode.CredentialNotFound, "未找到凭据。");

    public Result Delete(string credentialKey)
    {
        Store.Remove(credentialKey);
        return Result.Ok();
    }

    public bool Exists(string credentialKey) => Store.ContainsKey(credentialKey);
}

/// <summary>记录调用参数的启动器桩。</summary>
public class FakeProcessLauncher : IProcessLauncher
{
    public LaunchRequest? LastRequest { get; private set; }
    public Result<int>? NextResult { get; set; }

    public Result<int> Launch(LaunchRequest request)
    {
        LastRequest = request;
        return NextResult ?? Result<int>.Ok(4242);
    }
}
