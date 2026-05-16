using System.IO.Abstractions;
using EasyDotnet.Nuget;
using EasyDotnet.ProjXLanguageServer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyDotnet.ProjXLanguageServer;

public static class ProjXLanguageServerServiceCollectionExtensions
{
  public static IServiceCollection AddProjXLanguageServerServices(this IServiceCollection services)
  {
    services.TryAddSingleton<IFileSystem, FileSystem>();
    services.TryAddSingleton<IDocumentManager, DocumentManager>();
    services.TryAddSingleton<IProjXWorkspaceContext, ProjXWorkspaceContext>();
    services.TryAddSingleton<IProjXDocumentTextProvider, ProjXDocumentTextProvider>();
    services.TryAddSingleton<IProjXMsBuildPropertyProvider, UnavailableProjXMsBuildPropertyProvider>();
    services.TryAddSingleton<IProjXWorkspaceHierarchyService, ProjXWorkspaceHierarchyService>();
    services.TryAddSingleton<ICentralPackageVersionService, CentralPackageVersionService>();
    services.TryAddSingleton<IPackageReferenceEditPlanner, PackageReferenceEditPlanner>();
    services.TryAddSingleton<IUserSecretsResolver, UserSecretsResolver>();
    services.TryAddSingleton<ICompletionService, CompletionService>();
    services.TryAddSingleton<IHoverService, HoverService>();
    services.TryAddSingleton<IDefinitionService, DefinitionService>();
    services.TryAddSingleton<ISemanticTokensService, SemanticTokensService>();
    services.TryAddSingleton<ICodeActionService, CodeActionService>();
    services.TryAddSingleton<IDiagnosticsService, DiagnosticsService>();
    services.TryAddSingleton<IDiagnosticsPublisher, DiagnosticsPublisher>();
    services.TryAddSingleton<IFormattingService, FormattingService>();
    services.TryAddSingleton<ISignatureHelpService, SignatureHelpService>();
    services.TryAddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
    services.TryAddSingleton(typeof(ILogger<>), typeof(Logger<>));
    services.TryAddSingleton<INugetSettingsProvider>(sp =>
        new DefaultNugetSettingsProvider(() =>
        {
          var docs = sp.GetRequiredService<IDocumentManager>() as DocumentManager;
          var firstUri = docs?.TryGetAnyDocumentUri();
          return firstUri is { IsFile: true } ? Path.GetDirectoryName(firstUri.LocalPath) : null;
        }));
    services.TryAddSingleton<INugetSearchService, NugetSearchService>();

    return services;
  }
}