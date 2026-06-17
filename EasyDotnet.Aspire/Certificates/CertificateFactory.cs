using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace EasyDotnet.Aspire.Certificates;

/// <summary>
/// Generated DCP session credentials: the bearer token DCP must present and the
/// self-signed TLS certificate (with the base64 DER form for
/// <c>DEBUG_SESSION_SERVER_CERTIFICATE</c>).
/// </summary>
public sealed record DcpCredentials(X509Certificate2 ServerCertificate, string CertificateBase64, string Token);

/// <summary>
/// Mints the per-session TLS cert + bearer token DCP uses to reach the IDE
/// endpoint. The cert MUST carry a Subject Alternative Name of <c>localhost</c>
/// (CN alone is rejected by DCP, with no http/ws fallback). The base64 form is
/// the DER encoding, matching the reference extension's <c>x509.raw</c>.
/// </summary>
public static class CertificateFactory
{
  public static DcpCredentials Create()
  {
    using var rsa = RSA.Create(2048);
    var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

    var san = new SubjectAlternativeNameBuilder();
    san.AddDnsName("localhost");
    request.CertificateExtensions.Add(san.Build());

    using var ephemeral = request.CreateSelfSigned(
        DateTimeOffset.UtcNow.AddDays(-1),
        DateTimeOffset.UtcNow.AddYears(1));

    // Re-import via PFX so the key is usable by Kestrel across platforms.
    var pfx = ephemeral.Export(X509ContentType.Pfx);
    var serverCert = new X509Certificate2(pfx);

    var certBase64 = Convert.ToBase64String(ephemeral.Export(X509ContentType.Cert));
    var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    return new DcpCredentials(serverCert, certBase64, token);
  }
}