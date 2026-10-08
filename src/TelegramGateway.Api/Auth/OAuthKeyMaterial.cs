using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace TelegramGateway.Api.Auth;

public static class OAuthKeyMaterial
{
    public static X509Certificate2 Load(string dataPath, TimeProvider time)
    {
        var directory = Path.Combine(dataPath, ".secrets");
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var path = Path.Combine(directory, "oauth.pfx");
        if (!File.Exists(path))
        {
            using var rsa = RSA.Create(3072);
            var request = new CertificateRequest("CN=TelegramGateway OAuth", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            var now = time.GetUtcNow();
            using var certificate = request.CreateSelfSigned(now.AddDays(-1), now.AddYears(5));
            // ADR 0002: stable private keys keep token validation and antiforgery working after restart.
            var fileOptions = new FileStreamOptions
            {
                Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None
            };
            if (!OperatingSystem.IsWindows()) fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using var file = new FileStream(path, fileOptions);
            file.Write(certificate.Export(X509ContentType.Pfx));
            file.Flush(true);
        }
        return X509CertificateLoader.LoadPkcs12FromFile(path, null, X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
    }
}
