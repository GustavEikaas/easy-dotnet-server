using EasyDotnet.Debugger.Interfaces;
using EasyDotnet.Debugger.Messages;
using EasyDotnet.Debugger.Services;
using EasyDotnet.Debugger.Session;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Debugger;

public class DebugSessionFactory(ILoggerFactory loggerFactory) : IDebugSessionFactory
{
  public DebugSession Create(
    Func<InterceptableAttachRequest, IDebuggerProxy, Task<InterceptableAttachRequest>> attachRequestRewriter,
    bool applyValueConverters)
  {
    var valueConverterService = new ValueConverterService(
      loggerFactory.CreateLogger<ValueConverterService>(),
      loggerFactory);

    var tcpServer = new TcpDebugServer(
      loggerFactory.CreateLogger<TcpDebugServer>());

    var processHost = new DebuggerProcessHost(
      loggerFactory.CreateLogger<DebuggerProcessHost>());

    DebugSessionCoordinator? coordinator = null;

    var clientInterceptor = new ClientMessageInterceptor(
      loggerFactory.CreateLogger<ClientMessageInterceptor>(),
      valueConverterService,
      (a) => attachRequestRewriter(a, coordinator!.Proxy!),
      processId => coordinator?.NotifyDebugeeProcessStarted(processId),
      () => coordinator?.NotifyConfigurationDone());

    var debuggerInterceptor = new DebuggerMessageInterceptor(
      loggerFactory.CreateLogger<DebuggerMessageInterceptor>(),
      valueConverterService,
      applyValueConverters,
      processId => coordinator?.NotifyDebugeeProcessStarted(processId));

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