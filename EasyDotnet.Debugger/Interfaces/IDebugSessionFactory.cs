using EasyDotnet.Debugger.Messages;
using EasyDotnet.Debugger.Services;

namespace EasyDotnet.Debugger.Interfaces;

public interface IDebugSessionFactory
{
  DebugSession Create(
    Func<InterceptableAttachRequest, IDebuggerProxy, Task<InterceptableAttachRequest>> attachRequestRewriter,
    bool applyValueConverters,
    IVariableLocationResolver? variableLocationResolver = null);
}