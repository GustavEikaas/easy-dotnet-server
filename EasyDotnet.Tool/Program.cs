using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using EasyDotnet;
using EasyDotnet.Utils;

class Program
{
  private const int MaxPipeNameLength = 104;

  public static async Task<int> Main(string[] args)
  {
    HostDirectoryUtil.HostDirectory = Directory.GetCurrentDirectory();
    if (args.Contains("-v"))
    {
      var assembly = Assembly.GetExecutingAssembly();
      var version = assembly.GetName().Version;
      Console.WriteLine($"Assembly Version: {version}");
      return 0;
    }

    if (args.Contains("--generate-rpc-docs"))
    {
      var doc = RpcDocGenerator.GenerateJsonDoc();
      File.WriteAllText("./rpcDoc.json", doc);
      return 0;
    }

    if (args.Contains("--generate-rpc-docs-md"))
    {
      var md = RpcDocGenerator.GenerateMarkdownDoc().Replace("\r\n", "\n").Replace("\r", "\n");
      File.WriteAllText("./rpcDoc.md", md);
      return 0;
    }


    await StartServerAsync();

    return 0;
  }

  private static async Task StartServerAsync()
  {
    var PipeName = GeneratePipeName();

    var clientId = 0;
    while (true)
    {
      var stream = new NamedPipeServerStream(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
      Console.WriteLine($"Named pipe server started: {PipeName}");
      await stream.WaitForConnectionAsync();
      _ = RespondToRpcRequestsAsync(stream, ++clientId);
    }
  }

  private static string GeneratePipeName()
  {
    var pipePrefix = "CoreFxPipe_";
    var pipeName = "EasyDotnet_" + Regex.Replace(Convert.ToBase64String(Guid.NewGuid().ToByteArray()), "[/+=]", "");
    var maxNameLength = MaxPipeNameLength - Path.GetTempPath().Length - pipePrefix.Length - 1;
    return pipeName[..Math.Min(pipeName.Length, maxNameLength)];
  }

  private static async Task RespondToRpcRequestsAsync(Stream stream, int clientId)
  {
    var rpc = JsonRpcServerBuilder.Build(stream, stream);
    rpc.StartListening();
    await rpc.Completion;
    await Console.Error.WriteLineAsync($"Connection #{clientId} terminated.");
  }
}