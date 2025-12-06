namespace EasyDotnet.Aspire.Server;

public class DcpServerOptions
{
  /// <summary>
  /// Port to listen on (0 for auto-assign)
  /// </summary>
  public int Port { get; set; } = 0;

  /// <summary>
  /// Custom authentication token (null to generate)
  /// </summary>
  public string? Token { get; set; }

  /// <summary>
  /// Supported DCP protocol versions
  /// </summary>
  public string[] SupportedProtocols { get; set; } =
      ["2024-03-03", "2024-04-23", "2025-10-01"];

  /// <summary>
  /// Supported launch configuration types
  /// </summary>
  public string[] SupportedLaunchConfigurations { get; set; } =
      ["project", "prompting", "baseline.v1", "secret-prompts.v1",
       "ms-dotnettools.csharp", "devkit", "ms-dotnettools.csdevkit"];
}