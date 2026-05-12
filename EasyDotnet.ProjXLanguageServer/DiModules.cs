using System.IO.Abstractions;
using EasyDotnet.Nuget;
using EasyDotnet.ProjXLanguageServer.Services;
using EasyDotnet.ProjXLanguageServer.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StreamJsonRpc;

namespace EasyDotnet.ProjXLanguageServer;

public static class DiModules
{
  public static ServiceProvider BuildServiceProvider(JsonRpc jsonRpc)
  {
    var services = new ServiceCollection();
    services.AddSingleton(jsonRpc);
    services.AddSingleton<IFileSystem, FileSystem>();
    services.AddSingleton<IDocumentManager, DocumentManager>();
    services.AddSingleton<IUserSecretsResolver, UserSecretsResolver>();
    services.AddSingleton<ICompletionService, CompletionService>();
    services.AddSingleton<IHoverService, HoverService>();
    services.AddSingleton<IDefinitionService, DefinitionService>();
    services.AddSingleton<ISemanticTokensService, SemanticTokensService>();
    services.AddSingleton<ICodeActionService, CodeActionService>();
    services.AddSingleton<IDiagnosticsService, DiagnosticsService>();
    services.AddSingleton<IDiagnosticsPublisher, DiagnosticsPublisher>();
    services.AddSingleton<IFormattingService, FormattingService>();
    services.AddSingleton<ISignatureHelpService, SignatureHelpService>();
    services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
    services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
    services.AddSingleton<INugetSettingsProvider>(sp =>
        new DefaultNugetSettingsProvider(() =>
        {
          var docs = sp.GetRequiredService<IDocumentManager>() as DocumentManager;
          var firstUri = docs?.TryGetAnyDocumentUri();
          return firstUri is { IsFile: true } ? Path.GetDirectoryName(firstUri.LocalPath) : null;
        }));
    services.AddSingleton<INugetSearchService, NugetSearchService>();
    AssemblyScanner.GetControllerTypes().ForEach(x => services.AddTransient(x));

    return services.BuildServiceProvider();
  }
}