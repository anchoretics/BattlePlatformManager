using BPM.Application.Models;
using BPM.Shared;

namespace BPM.Application.Abstractions;

/// <summary>Windows 凭据存取。唯一允许接触密码的持久化通道。</summary>
public interface ICredentialService
{
    Result Save(string credentialKey, string userName, string password);

    Result<StoredCredential> Get(string credentialKey);

    Result Delete(string credentialKey);

    bool Exists(string credentialKey);
}
