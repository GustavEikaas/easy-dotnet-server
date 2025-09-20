using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EasyDotnet.Controllers;
using EasyDotnet.Services;

namespace EasyDotnet.Utils;

public static class AssemblyScanner
{
  public static List<Type> GetControllerTypes() => [.. new List<Assembly>() { Assembly.GetExecutingAssembly() }
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsClass && !t.IsAbstract && typeof(BaseController).IsAssignableFrom(t))];

  public static List<Type> GetNotificationDispatchers() => [.. new List<Assembly>() { Assembly.GetExecutingAssembly() }
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsClass && !t.IsAbstract && typeof(INotificationService).IsAssignableFrom(t))];
}