using EasyDotnet.Debugger.Messages;

namespace EasyDotnet.Debugger.Interfaces;

public interface IDebugSessionFactory
{
  DebugSession Create(
    Func<InterceptableAttachRequest, IDebuggerProxy, Task<InterceptableAttachRequest>> attachRequestRewriter,
    bool applyValueConverters);
}