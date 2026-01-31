using EasyDotnet.Debugger.Interfaces;
using EasyDotnet.Debugger.Messages;
using EasyDotnet.Debugger.Services;
using EasyDotnet.Debugger.Session;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Debugger;

public class DebugSessionFactory(ILoggerFactory loggerFactory) : IDebugSessionFactory
{
  public DebugSession Create(
    Func<InterceptableAttachRequest, Task<InterceptableAttachRequest>> attachRequestRewriter,
    bool applyValueConverters)
  {
    var valueConverterService = new ValueConverterService(
      loggerFactory.CreateLogger<ValueConverterService>(),
      loggerFactory);

    var tcpServer = new TcpDebugServer(
      loggerFactory.CreateLogger<TcpDebugServer>());

    var processHost = new DebuggerProcessHost(
      loggerFactory.CreateLogger<DebuggerProcessHost>());

    var clientInterceptor = new ClientMessageInterceptor(
      loggerFactory.CreateLogger<ClientMessageInterceptor>(),
      valueConverterService,
      attachRequestRewriter);

    DebugSessionCoordinator? coordinator = null;

    var debuggerInterceptor = new DebuggerMessageInterceptor(
      loggerFactory.CreateLogger<DebuggerMessageInterceptor>(),
      valueConverterService,
      applyValueConverters,
      (int processId) => coordinator?.NotifyDebugeeProcessStarted(processId));

    coordinator = new DebugSessionCoordinator(
     loggerFactory.CreateLogger<DebugSessionCoordinator>(),
     tcpServer,
     processHost,
     clientInterceptor,
     debuggerInterceptor,
     loggerFactory.CreateLogger<DebuggerProxy>());

    return new DebugSession(coordinator);
  }
}