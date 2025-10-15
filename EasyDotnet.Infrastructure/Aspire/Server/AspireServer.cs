using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using EasyDotnet.Infrastructure.Aspire.Server.Controllers;
using StreamJsonRpc;

namespace EasyDotnet.Infrastructure.Aspire.Server;

public static class AspireServer
{
  public static X509Certificate2 GenerateSslCert()
  {
    using var rsa = RSA.Create(4096);
    var req = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

    req.CertificateExtensions.Add(
        new X509BasicConstraintsExtension(false, false, 0, false));
    req.CertificateExtensions.Add(
        new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
    req.CertificateExtensions.Add(
        new X509SubjectKeyIdentifierExtension(req.PublicKey, false));

    var sanBuilder = new SubjectAlternativeNameBuilder();
    sanBuilder.AddDnsName("localhost");
    req.CertificateExtensions.Add(sanBuilder.Build());

    var cert = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(1));
    return new X509Certificate2(cert.Export(X509ContentType.Pfx));
  }

  public static (TcpListener, string) CreateListener()
  {

    var listener = new TcpListener(IPAddress.Loopback, 0); // 0 = random free port
    listener.Start();
    var endpoint = $"127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
    Console.WriteLine($"TLS server listening on {endpoint}");
    return (listener, endpoint);
  }

  public static System.Diagnostics.Process StartAspireHost(string endpoint, string token, X509Certificate2 certificate)
  {

    var psi = new ProcessStartInfo
    {
      FileName = "aspire",
      // Arguments = @"run --debug --project C:\Users\Gustav\repo\aspire\aspire.ApiService\aspire.ApiService.csproj",
      Arguments = "run --debug",
      UseShellExecute = false,
      RedirectStandardOutput = true,
      WorkingDirectory = @"C:\Users\Gustav\repo\aspire",
      RedirectStandardError = true
    };

    psi.Environment["ASPIRE_EXTENSION_ENDPOINT"] = endpoint;
    psi.Environment["ASPIRE_EXTENSION_TOKEN"] = token;
    psi.Environment["ASPIRE_EXTENSION_CERT"] = Convert.ToBase64String(certificate.Export(X509ContentType.Cert));
    psi.Environment["ASPIRE_EXTENSION_PROMPT_ENABLED"] = "true";

    var cliProcess = System.Diagnostics.Process.Start(psi) ?? throw new Exception("Failed to start Aspire CLI");
    cliProcess.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Console.WriteLine("[Aspire STDOUT] " + e.Data); };
    cliProcess.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Console.Error.WriteLine("[Aspire STDERR] " + e.Data); };
    cliProcess.BeginOutputReadLine();
    cliProcess.BeginErrorReadLine();
    return cliProcess;
  }


  public static JsonRpc CreateAspireServer(SslStream stream)
  {
    var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(stream, stream));
    rpc.TraceSource.Switch.Level = SourceLevels.All;
    rpc.TraceSource.Listeners.Add(new ConsoleTraceListener());

    rpc.AddLocalRpcTarget(new AppHostController());
    rpc.AddLocalRpcTarget(new CapabilitiesController());
    // rpc.AddLocalRpcTarget(new DebuggingController());
    rpc.AddLocalRpcTarget(new DisplayController());
    rpc.AddLocalRpcTarget(new EditorController());
    rpc.AddLocalRpcTarget(new LoggingController());
    rpc.AddLocalRpcTarget(new PromptController());
    // rpc.StartListening();
    return rpc;
  }
}