using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.Options;
using AgentGateway.Core.Options;

namespace AgentGateway.Api.Auth;

/// <summary>
/// Encrypts the data-protection key ring with the gateway's own OAuth certificate (.secrets/oauth.pfx).
/// The framework's CertificateXmlEncryptor names a decryptor that looks the certificate up in the OS
/// certificate store, where a container never has it, so the key ring was unreadable after every restart.
/// This pair keeps the same XML-encryption format but resolves the certificate from the data volume.
/// </summary>
public sealed class CertificateFileXmlEncryptor(X509Certificate2 certificate) : IXmlEncryptor
{
    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        var document = new XmlDocument { XmlResolver = null };
        document.Load(new XElement("root", plaintextElement).CreateReader());
        var element = (XmlElement)document.DocumentElement!.FirstChild!;
        var encrypted = new EncryptedXml().Encrypt(element, certificate);
        return new EncryptedXmlInfo(XElement.Parse(encrypted.GetXml().OuterXml), typeof(CertificateFileXmlDecryptor));
    }
}

/// <summary>Created by the data-protection activator, which passes the application's service provider.</summary>
public sealed class CertificateFileXmlDecryptor(IServiceProvider services) : IXmlDecryptor
{
    public XElement Decrypt(XElement encryptedElement)
    {
        var gateway = services.GetRequiredService<IOptions<GatewayOptions>>().Value;
        using var certificate = OAuthKeyMaterial.Load(gateway.DataPath, services.GetRequiredService<TimeProvider>());
        var document = new XmlDocument { XmlResolver = null };
        document.Load(new XElement("root", encryptedElement).CreateReader());
        new FileCertificateEncryptedXml(document, certificate).DecryptDocument();
        return XElement.Load(document.DocumentElement!.FirstChild!.CreateNavigator()!.ReadSubtree());
    }

    private sealed class FileCertificateEncryptedXml(XmlDocument document, X509Certificate2 certificate) : EncryptedXml(document)
    {
        public override byte[]? DecryptEncryptedKey(EncryptedKey encryptedKey)
        {
            var matches = encryptedKey.KeyInfo.OfType<KeyInfoX509Data>()
                .SelectMany(data => data.Certificates?.OfType<X509Certificate2>() ?? [])
                .Any(candidate => candidate.Thumbprint.Equals(certificate.Thumbprint, StringComparison.OrdinalIgnoreCase));
            if (!matches || encryptedKey.CipherData.CipherValue is not { } cipher)
                throw new CryptographicException("The key was encrypted with a certificate other than .secrets/oauth.pfx.");
            using var rsa = certificate.GetRSAPrivateKey() ?? throw new CryptographicException("The OAuth certificate has no RSA private key.");
            return DecryptKey(cipher, rsa, encryptedKey.EncryptionMethod?.KeyAlgorithm == XmlEncRSAOAEPUrl);
        }
    }
}
