namespace EasyDotnet.Infrastructure.Dap;

/// <summary>
/// Represents an exception that occurs during the processing of a DAP (Debug Adapter Protocol) message
/// and encapsulates the information necessary to send a properly formatted DAP error response to the client.
/// </summary>
public sealed class DapException : Exception
{
  public string Command { get; }
  public int Seq { get; }
  public int RequestSeq { get; }
  public DapErrorMessage DapErrorMessage { get; }

  public DapException(string command, int seq, int requestSeq, string message)
      : base(message)
  {
    Command = command;
    Seq = seq;
    RequestSeq = requestSeq;
    DapErrorMessage = new DapErrorMessage(command, seq, requestSeq, message);
  }

  public DapException(string command, int seq, int requestSeq, string message, Exception inner)
      : base(message, inner)
  {
    Command = command;
    Seq = seq;
    RequestSeq = requestSeq;
    DapErrorMessage = new DapErrorMessage(command, seq, requestSeq, message);
  }
}
