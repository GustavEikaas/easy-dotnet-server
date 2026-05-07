using System.Reflection;

namespace EasyDotnet.RoslynLanguageServices;

/// <summary>
/// Reflection-only bridge to the headless Roslyn LSP server's <c>IClientLanguageServerManager</c>
/// so we can send custom LSP requests to the editor without taking a hard reference on the
/// internal <c>Microsoft.CodeAnalysis.LanguageServer</c> assembly.
/// </summary>
internal static class LspClientBridge
{
    private static readonly Lazy<(object Manager, MethodInfo SendRequestAsync)?> s_handle = new(Resolve);

    public static async Task<TResponse?> SendRequestAsync<TParams, TResponse>(
        string method, TParams @params, CancellationToken cancellationToken)
    {
        var handle = s_handle.Value
            ?? throw new InvalidOperationException("LanguageServerHost.Instance not initialized; cannot send LSP requests.");

        var typed = handle.SendRequestAsync.MakeGenericMethod(typeof(TParams), typeof(TResponse));
        var task = (Task<TResponse>)typed.Invoke(handle.Manager, [method, @params!, cancellationToken])!;
        return await task.ConfigureAwait(false);
    }

    private static (object, MethodInfo)? Resolve()
    {
        var lsAsm = AppDomain.CurrentDomain
            .GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Microsoft.CodeAnalysis.LanguageServer");
        if (lsAsm is null)
            return null;

        var hostType = lsAsm.GetType("Microsoft.CodeAnalysis.LanguageServer.LanguageServer.LanguageServerHost");
        var instance = hostType?.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null);
        if (instance is null)
            return null;

        var managerIface = lsAsm.GetType("Microsoft.CodeAnalysis.LanguageServer.IClientLanguageServerManager");
        if (managerIface is null)
            return null;

        var getService = hostType!.GetMethod("GetRequiredLspService", BindingFlags.Public | BindingFlags.Instance)!
            .MakeGenericMethod(managerIface);
        var manager = getService.Invoke(instance, null);
        if (manager is null)
            return null;

        // Task<TResponse> SendRequestAsync<TParams, TResponse>(string, TParams, CancellationToken)
        var sendRequestAsync = managerIface.GetMethods()
            .Single(m => m.Name == "SendRequestAsync"
                && m.IsGenericMethodDefinition
                && m.GetGenericArguments().Length == 2
                && m.GetParameters().Length == 3);

        return (manager, sendRequestAsync);
    }
}
