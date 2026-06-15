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
    DebuggerProxyFeatures features,
    IVariableLocationResolver? variableLocationResolver = null)
  {
    var applyValueConverters = features.SupportsValueConverters;

    var valueConverterService = new ValueConverterService(
      loggerFactory.CreateLogger<ValueConverterService>(),
      loggerFactory);

    var tcpServer = new TcpDebugServer(
      loggerFactory.CreateLogger<TcpDebugServer>());

    var processHost = new DebuggerProcessHost(
      loggerFactory.CreateLogger<DebuggerProcessHost>());

    DebugSessionCoordinator? coordinator = null;

    var frameSourceTracker = features.DecorateVariableLocations && variableLocationResolver is not null
      ? new FrameSourceTracker()
      : null;

    var clientInterceptor = new ClientMessageInterceptor(
      loggerFactory.CreateLogger<ClientMessageInterceptor>(),
      valueConverterService,
      (a) => attachRequestRewriter(a, coordinator!.Proxy!),
      processId => coordinator?.NotifyDebugeeProcessStarted(processId),
      () => coordinator?.NotifyConfigurationDone(),
      features.RewriteEvaluateAssignments,
      frameSourceTracker);

    var debuggerInterceptor = new DebuggerMessageInterceptor(
      loggerFactory.CreateLogger<DebuggerMessageInterceptor>(),
      valueConverterService,
      applyValueConverters,
      features.AdvertiseCompletions,
      processId => coordinator?.NotifyDebugeeProcessStarted(processId),
      signal => coordinator?.NotifyDebugStartSignal(signal),
      () => coordinator?.NotifyDebuggerConfigurationDone(),
      (command, message) => coordinator?.NotifyDebugSessionStartFailed(command, message),
      frameSourceTracker,
      features.DecorateVariableLocations ? variableLocationResolver : null);

    coordinator = new DebugSessionCoordinator(
     loggerFactory.CreateLogger<DebugSessionCoordinator>(),
     tcpServer,
     processHost,
     clientInterceptor,
     debuggerInterceptor,
     features.EmitTelemetry,
     loggerFactory.CreateLogger<DebuggerProxy>());

    return new DebugSession(coordinator);
  }
}