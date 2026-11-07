using ModelContextProtocol;

namespace EasyDotnet.IDE.Mcp;

/// <summary>
/// The MCP SDK replaces a failing tool's exception with a generic message unless it is an
/// <see cref="McpException"/>. Tools run their work through here so the agent sees why a call
/// failed (unknown id, no solution, operation in progress) and can correct the next call.
/// </summary>
internal static class McpErrors
{
  public static async Task<string> SurfaceAsync(Func<Task<string>> work)
  {
    try
    {
      return await work();
    }
    catch (Exception ex) when (ex is not (McpException or OperationCanceledException))
    {
      throw new McpException(ex.Message, ex);
    }
  }

  public static string Surface(Func<string> work)
  {
    try
    {
      return work();
    }
    catch (Exception ex) when (ex is not McpException)
    {
      throw new McpException(ex.Message, ex);
    }
  }
}