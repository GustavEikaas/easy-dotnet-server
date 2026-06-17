using System.Security.Cryptography.X509Certificates;
using EasyDotnet.Aspire.Certificates;

namespace EasyDotnet.Aspire.Tests;

public class CertificateFactoryTests
{
  [Test]
  public async Task Create_produces_cert_with_localhost_san()
  {
    var credentials = CertificateFactory.Create();

    var sanExtension = credentials.ServerCertificate.Extensions
        .OfType<X509SubjectAlternativeNameExtension>()
        .SingleOrDefault();

    await Assert.That(sanExtension).IsNotNull();
    await Assert.That(sanExtension!.EnumerateDnsNames()).Contains("localhost");
  }

  [Test]
  public async Task Create_produces_nonempty_token_and_base64_cert()
  {
    var credentials = CertificateFactory.Create();

    await Assert.That(credentials.Token).IsNotNullOrEmpty();
    await Assert.That(Convert.FromBase64String(credentials.CertificateBase64).Length).IsGreaterThan(0);
  }
}