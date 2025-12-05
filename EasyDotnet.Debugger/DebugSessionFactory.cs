using EasyDotnet.Debugger.Interfaces;
using EasyDotnet.Debugger.Services;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Debugger;

public class DebugSessionFactory(ILoggerFactory loggerFactory) : IDebugSessionFactory
{
  public DebugSession Create() => new(
      loggerFactory.CreateLogger<DebugSession>(),
      loggerFactory.CreateLogger<DebuggerProxy>(),
      loggerFactory.CreateLogger<ValueConverterService>()
  );
}