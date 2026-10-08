using System.Security.Cryptography;
using System.Text;

namespace TelegramGateway.Core.Security;

public static class ApiKeyComparison
{
    public static bool Matches(string supplied, string expected)
    {
        // Comparing fixed-size hashes avoids a length-dependent credential comparison.
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(suppliedHash, expectedHash);
    }
}
