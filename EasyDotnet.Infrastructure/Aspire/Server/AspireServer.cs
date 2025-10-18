using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Infrastructure.Aspire.Server.Controllers;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace EasyDotnet.Infrastructure.Aspire.Server;

public class AspireServerContext
{
  public required JsonRpc RpcServer { get; init; }
  public required DcpServer DcpServer { get; init; }
  public required System.Diagnostics.Process AspireCliProcess { get; init; }
  public required X509Certificate2 Certificate { get; init; }
  public required string Token { get; init; }
}

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


  public static async Task<AspireServerContext> CreateAndStartAsync(
      string projectPath,
      INetcoreDbgService netcoreDbgService,
      IMsBuildService msBuildService,
      ILogger<DcpServer> dcpLogger,
      ILogger<DebuggingController> logger2,
      CancellationToken cancellationToken = default)
  {
    // 1. Generate certificate and token for RPC server
    var cert = GenerateSslCert();
    var token = Guid.NewGuid().ToString("N");

    // 2. Create TCP listener for RPC server
    var (listener, rpcEndpoint) = CreateListener();

    // 3. Start DCP HTTP server
    var dcpServer = await DcpServer.CreateAsync(dcpLogger, netcoreDbgService, msBuildService, cancellationToken);
    Console.WriteLine($"DCP server listening on port {dcpServer.Port}");

    // 4. Start Aspire CLI process
    var aspireProcess = StartAspireCliProcess(projectPath, rpcEndpoint, token, cert, dcpServer);

    // 5. Accept RPC connection from Aspire CLI
    Console.WriteLine("Waiting for Aspire CLI to connect to RPC server...");
    var client = await listener.AcceptTcpClientAsync(cancellationToken);
    Console.WriteLine($"Aspire CLI connected from {client.Client.RemoteEndPoint}");

    // 6. Setup SSL stream
    var ssl = new SslStream(client.GetStream(), false);
    await ssl.AuthenticateAsServerAsync(
        cert,
        clientCertificateRequired: false,
        enabledSslProtocols: System.Security.Authentication.SslProtocols.Tls12,
        checkCertificateRevocation: false
    );
    Console.WriteLine("SSL authentication completed with Aspire CLI");

    // 7. Create RPC server
    var rpcServer = CreateAspireServer(ssl, dcpServer, netcoreDbgService, msBuildService, logger2);
    rpcServer.StartListening();
    Console.WriteLine("RPC server listening");

    return new AspireServerContext
    {
      RpcServer = rpcServer,
      DcpServer = dcpServer,
      AspireCliProcess = aspireProcess,
      Certificate = cert,
      Token = token
    };
  }

  private static System.Diagnostics.Process StartAspireCliProcess(
    string projectPath,
    string rpcEndpoint,
    string token,
    X509Certificate2 certificate, DcpServer dcpServer)
  {
    var psi = new ProcessStartInfo
    {
      FileName = "aspire",
      Arguments = $"run --project \"{projectPath}\"",
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      WorkingDirectory = Path.GetDirectoryName(projectPath)
    };

    // These tell Aspire CLI to connect to our RPC server
    psi.Environment["ASPIRE_EXTENSION_ENDPOINT"] = rpcEndpoint;
    psi.Environment["ASPIRE_EXTENSION_TOKEN"] = token;
    psi.Environment["ASPIRE_EXTENSION_CERT"] = Convert.ToBase64String(certificate.Export(X509ContentType.Cert));
    psi.Environment["ASPIRE_EXTENSION_PROMPT_ENABLED"] = "true";

    psi.Environment["DEBUG_SESSION_PORT"] = $"localhost:{dcpServer.Port}";
    psi.Environment["DEBUG_SESSION_TOKEN"] = dcpServer.Token;
    psi.Environment["DEBUG_SESSION_CERTIFICATE"] = dcpServer.CertificateBase64;

    var runSessionInfo = new
    {
      supported_launch_configurations = new[] { "project" }
    };
    psi.Environment["DEBUG_SESSION_INFO"] = JsonSerializer.Serialize(runSessionInfo);

    Console.WriteLine($"Starting Aspire CLI with DCP server at localhost:{dcpServer.Port}");

    var cliProcess = System.Diagnostics.Process.Start(psi) ?? throw new Exception("Failed to start Aspire CLI");

    cliProcess.OutputDataReceived += (_, e) =>
    {
      if (!string.IsNullOrEmpty(e.Data))
        Console.WriteLine("[Aspire CLI] " + e.Data);
    };

    cliProcess.ErrorDataReceived += (_, e) =>
    {
      if (!string.IsNullOrEmpty(e.Data))
        Console.Error.WriteLine("[Aspire CLI] " + e.Data);
    };

    cliProcess.BeginOutputReadLine();
    cliProcess.BeginErrorReadLine();

    return cliProcess;
  }


  public static JsonRpc CreateAspireServer(SslStream stream, DcpServer server, INetcoreDbgService netcoreDbgService, IMsBuildService msBuildService, ILogger<DebuggingController> logger)
  {
    var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(stream, stream));
    rpc.TraceSource.Switch.Level = SourceLevels.All;
    rpc.TraceSource.Listeners.Add(new ConsoleTraceListener());

    rpc.AddLocalRpcTarget(new AppHostController(server));
    rpc.AddLocalRpcTarget(new CapabilitiesController());
    rpc.AddLocalRpcTarget(new DebuggingController(netcoreDbgService, msBuildService, logger, server));
    rpc.AddLocalRpcTarget(new DisplayController());
    rpc.AddLocalRpcTarget(new EditorController());
    rpc.AddLocalRpcTarget(new LoggingController());
    rpc.AddLocalRpcTarget(new PromptController());
    // rpc.StartListening();
    return rpc;
  }
}