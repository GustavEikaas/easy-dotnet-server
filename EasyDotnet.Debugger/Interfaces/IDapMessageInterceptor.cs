using EasyDotnet.Debugger.Messages;

namespace EasyDotnet.Debugger.Interfaces;

public interface IDapMessageInterceptor
{
  Task<ProtocolMessage?> InterceptAsync(
    ProtocolMessage message,
    IDebuggerProxy proxy,
    CancellationToken cancellationToken);
}