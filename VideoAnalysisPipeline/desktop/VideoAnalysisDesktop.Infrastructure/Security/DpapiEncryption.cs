using System.Security.Cryptography;
using System.Text;

namespace VideoAnalysisDesktop.Infrastructure.Security;

/// <summary>
/// Encrypts and decrypts data using Windows DPAPI with LocalMachine scope.
/// All users on the machine can decrypt (trusted Azure caller model).
/// Administrators configure secrets; operators only read them.
/// </summary>
public static class DpapiEncryption
{
    public static byte[] Protect(byte[] data)
    {
        return ProtectedData.Protect(data, null, DataProtectionScope.LocalMachine);
    }

    public static byte[] Unprotect(byte[] data)
    {
        return ProtectedData.Unprotect(data, null, DataProtectionScope.LocalMachine);
    }

    public static string ProtectString(string plainText)
    {
        var bytes = Encoding.UTF8.GetBytes(plainText);
        var encrypted = Protect(bytes);
        return Convert.ToBase64String(encrypted);
    }

    public static string UnprotectString(string encryptedBase64)
    {
        var bytes = Convert.FromBase64String(encryptedBase64);
        var decrypted = Unprotect(bytes);
        return Encoding.UTF8.GetString(decrypted);
    }
}
