using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace ProdvisorAi.Demo.Services;

/// <summary>
/// Encrypts secrets (the customer's OpenAI API key) before they are written to the database.
///
/// - On Windows: DPAPI (<see cref="ProtectedData"/>) - the key material is managed by Windows itself,
///   so a copied database file is useless on another machine.
/// - Elsewhere (developer laptops on macOS/Linux): ASP.NET Core Data Protection with keys stored in App_Data/keys.
///
/// The stored value is prefixed with the method used ("dpapi:" / "dp:") so it can always be decrypted correctly.
/// </summary>
public sealed class SecretProtector
{
    const string DpapiPrefix = "dpapi:";
    const string DataProtectionPrefix = "dp:";

    // Additional entropy ties the ciphertext to this application.
    static readonly byte[] Entropy = "ProdvisorAi.Demo/openai-api-key"u8.ToArray();

    readonly IDataProtector _protector;
    readonly bool _currentUserScope;

    public SecretProtector(IDataProtectionProvider provider, IConfiguration config)
    {
        _protector = provider.CreateProtector("ProdvisorAi.Demo.Secrets");
        _currentUserScope = string.Equals(config["Security:DpapiScope"], "CurrentUser", StringComparison.OrdinalIgnoreCase);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    DataProtectionScope Scope => _currentUserScope ? DataProtectionScope.CurrentUser : DataProtectionScope.LocalMachine;

    public string Protect(string plainText)
    {
        if (OperatingSystem.IsWindows())
        {
            var cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(plainText), Entropy, Scope);
            return DpapiPrefix + Convert.ToBase64String(cipher);
        }
        return DataProtectionPrefix + _protector.Protect(plainText);
    }

    public string Unprotect(string stored)
    {
        if (stored.StartsWith(DpapiPrefix, StringComparison.Ordinal))
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("This secret was encrypted with Windows DPAPI and can only be read on Windows.");
            var plain = ProtectedData.Unprotect(Convert.FromBase64String(stored[DpapiPrefix.Length..]), Entropy, Scope);
            return Encoding.UTF8.GetString(plain);
        }
        if (stored.StartsWith(DataProtectionPrefix, StringComparison.Ordinal))
            return _protector.Unprotect(stored[DataProtectionPrefix.Length..]);

        throw new InvalidOperationException("Unknown secret format.");
    }

    /// <summary>"sk-proj-abc...wxyz" -> "sk-...wxyz". The only representation of the key that ever leaves the server.</summary>
    public static string Mask(string key) =>
        key.Length <= 8 ? "****" : $"{key[..3]}...{key[^4..]}";
}
