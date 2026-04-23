using System.Diagnostics;
using System.IO.Abstractions;
using System.Reflection;
using System.Runtime.InteropServices;
using DotNetOutdated.Core.Services;
using EasyDotnet.Debugger;
using EasyDotnet.IDE.AppWrapper;
using EasyDotnet.IDE.BuildHost;
using EasyDotnet.IDE.DebuggerStrategies;
using EasyDotnet.IDE.Editor;
using EasyDotnet.IDE.EntityFramework;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Logging;
using EasyDotnet.IDE.NewFile;
using EasyDotnet.IDE.PackageManager;
using EasyDotnet.IDE.Picker;
using EasyDotnet.IDE.ProcessExecution;
using EasyDotnet.IDE.ProjectReference.Services;
using EasyDotnet.IDE.Services;
using EasyDotnet.IDE.Settings;
using EasyDotnet.IDE.Solution.Services;
using EasyDotnet.IDE.StartupHook;
using EasyDotnet.IDE.TemplateEngine.PostActionHandlers;
using EasyDotnet.IDE.TestRunner.Adapters;
using EasyDotnet.IDE.TestRunner.Adapters.MTP.RPC;
using EasyDotnet.IDE.TestRunner.Dispatch;
using EasyDotnet.IDE.TestRunner.Executor;
using EasyDotnet.IDE.TestRunner.Lock;
using EasyDotnet.IDE.TestRunner.Registry;
using EasyDotnet.IDE.TestRunner.Service;
using EasyDotnet.IDE.TestRunner.Store;
using EasyDotnet.IDE.Utils;
using EasyDotnet.IDE.Workspace.Services;
using EasyDotnet.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using StreamJsonRpc;

namespace EasyDotnet;

public static class DiModules
{
  public static ServiceProvider BuildServiceProvider(JsonRpc jsonRpc, SourceLevels levels)
  {
    var services = new ServiceCollection();

    var logLevelState = new LogLevelState(levels);
    ConfigureLogging(logLevelState);

    services.AddLogging(builder =>
        {
          builder.ClearProviders();
          builder.AddSerilog(Log.Logger, dispose: true);
        });

    services.AddMemoryCache();
    services.AddHttpClient();
    services.AddSingleton(jsonRpc);
    services.AddSingleton<DbContextCache>();
    services.AddSingleton(logLevelState);
    services.AddSingleton<IClientService, ClientService>();
    services.AddSingleton<IVisualStudioLocator, VisualStudioLocator>();
    services.AddSingleton<IFileSystem, FileSystem>();
    services.AddSingleton<RoslynService>();
    services.AddSingleton<ISolutionService, SolutionService>();
    services.AddSingleton<IProcessQueue, ProcessQueue>();
    services.AddSingleton<TemplateEngineService>();
    services.AddSingleton<IDebugSessionManager, DebugSessionManager>();
    services.AddSingleton<IDebugOrchestrator, DebugOrchestrator>();
    services.AddSingleton<IAppPathsService, AppPathsService>();
    services.AddSingleton<UpdateCheckerService>();
    services.AddSingleton<SettingsFileResolver>();
    services.AddSingleton<SettingsSerializer>();
    services.AddSingleton<SettingsService>();
    services.AddSingleton<SettingsGarbageCollector>();
    services.AddSingleton<IEditorProcessManagerService, EditorProcessManagerService>();
    services.AddSingleton<IEditorService, EditorService>();
    services.AddSingleton<IPickerScopeRegistry, PickerScopeRegistry>();
    services.AddSingleton<PickerScopeFactory>();
    services.AddSingleton<IPickerService, PickerService>();
    services.AddSingleton<IDebugStrategyFactory, DebugStrategyFactory>();
    services.AddSingleton<AppWrapperManager>();
    services.AddSingleton<IAppWrapperManager>(sp => sp.GetRequiredService<AppWrapperManager>());
    services.AddSingleton<AppWrapperPipeListener>();
    services.AddSingleton<BuildHostFactory>();
    services.AddSingleton<IBuildHostManager, BuildHostManager>();
    services.AddSingleton<WorkspaceBuildHostManager>();
    services.AddSingleton<ProjectEvaluationCache>();

    services.AddSingleton<TestRunnerService>();
    services.AddSingleton<WorkspaceService>();
    services.AddSingleton<WorkspaceProjectResolver>();
    services.AddSingleton<WorkspaceBuildService>();
    services.AddSingleton<WorkspaceRestoreService>();
    services.AddSingleton<WorkspaceTestService>();
    services.AddSingleton<WorkspacePreBuildService>();
    services.AddSingleton<WorkspaceSessionRegistry>();
    services.AddSingleton<WorkspaceDebugAttachService>();
    services.AddSingleton<NodeRegistry>();
    services.AddSingleton<StatusDispatcher>();
    services.AddSingleton<DetailStore>();
    services.AddSingleton<BuildErrorStore>();
    services.AddSingleton<GlobalOperationLock>();
    services.AddSingleton<OperationExecutor>();
    services.AddSingleton<AdapterResolver>();
    services.AddTransient<VsTestAdapter>();
    services.AddSingleton<Func<VsTestAdapter>>(x => () => x.GetRequiredService<VsTestAdapter>());
    services.AddSingleton<MtpAdapter>();
    services.AddSingleton<MtpClientFactory>();

    services.AddTransient<IProgressScopeFactory, ProgressScopeFactory>();
    services.AddTransient<IStartupHookService, StartupHookService>();
    services.AddTransient<IMsBuildService, MsBuildService>();
    services.AddTransient<NewFileService>();
    services.AddTransient<UserSecretsService>();
    services.AddTransient<EntityFrameworkService>();
    services.AddTransient<ILaunchProfileService, LaunchProfileService>();
    services.AddTransient<INotificationService, NotificationService>();
    services.AddTransient<NugetService>();
    services.AddPackageManager();
    services.AddTransient<OutdatedService>();
    services.AddTransient<GlobalJsonService>();

    services.AddTransient<ProjectReferenceService>();
    services.AddTransient<SolutionManagementService>();
    services.AddTransient<PostActionProcessor>();
    services.AddTransient<IPostActionHandler, RunScriptPostActionHandler>();
    services.AddTransient<IPostActionHandler, OpenFilePostActionHandler>();
    services.AddTransient<IPostActionHandler, ChangeFilePermissionsPostActionHandler>();
    services.AddTransient<IPostActionHandler, DisplayManualInstructionsPostActionHandler>();
    services.AddTransient<IPostActionHandler, RestoreNugetPackagesPostActionHandler>();
    services.AddTransient<IPostActionHandler, AddProjectsToSolutionFilePostActionHandler>();
    services.AddTransient<IPostActionHandler, AddPropertyToExistingJsonFilePostActionHandler>();
    services.AddTransient<IPostActionHandler, AddReferenceToProjectFilePostActionHandler>();

    //Dotnet oudated
    services.AddSingleton<IProjectAnalysisService, ProjectAnalysisService>();
    services.AddSingleton<IDotNetRunner, DotNetRunner>();
    services.AddSingleton<IDependencyGraphService, DependencyGraphService>();
    services.AddSingleton<IDotNetRestoreService, DotNetRestoreService>();
    services.AddSingleton<INuGetPackageInfoService, NuGetPackageInfoService>();
    services.AddSingleton<INuGetPackageResolutionService, NuGetPackageResolutionService>();

    services.AddDebugger();

    AssemblyScanner.GetControllerTypes().ForEach(x => services.AddTransient(x));

    var serviceProvider = services.BuildServiceProvider();

    var logger = serviceProvider.GetRequiredService<ILogger<JsonRpc>>();
    jsonRpc.TraceSource.Switch.Level = levels;
    logLevelState.LevelChanged += l => jsonRpc.TraceSource.Switch.Level = l;
    jsonRpc.TraceSource.Listeners.Clear();
    jsonRpc.TraceSource.Listeners.Add(new JsonRpcLogger(logger));

    var appWrapperListener = serviceProvider.GetRequiredService<AppWrapperPipeListener>();
    _ = appWrapperListener.StartAsync(CancellationToken.None);

    return serviceProvider;
  }

  private static void ConfigureLogging(LogLevelState state)
  {
    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.ControlledBy(state.Switch)
        .WriteTo.Console()
        .WriteTo.Sink(state.RingSink)
        .CreateLogger();
    WriteLogHeader();
  }

  private static void WriteLogHeader()
  {
    var logger = Log.Logger ?? throw new InvalidOperationException("Serilog logger is not initialized.");
    var process = Process.GetCurrentProcess();

    logger.Debug("============================================================");
    logger.Debug(" [EasyDotnet] Host Server Log");
    logger.Debug("============================================================");
    logger.Debug($"Timestamp      : {DateTime.UtcNow:O} (UTC)");
    logger.Debug($"ProcessId      : {Environment.ProcessId}");
    logger.Debug($"Process Name   : {process.ProcessName}");
    logger.Debug($"Machine Name   : {Environment.MachineName}");
    logger.Debug($"User           : {Environment.UserName}");
    logger.Debug($"OS Version     : {Environment.OSVersion}");
    logger.Debug($"OS Arch        : {RuntimeInformation.OSArchitecture}");
    logger.Debug($"Process Arch   : {RuntimeInformation.ProcessArchitecture}");
    logger.Debug($"Framework      : {RuntimeInformation.FrameworkDescription}");
    logger.Debug($"CPU Count      : {Environment.ProcessorCount}");
    logger.Debug($"Working Set    : {process.WorkingSet64 / 1024 / 1024} MB");
    logger.Debug($"Current Dir    : {Environment.CurrentDirectory}");
    logger.Debug($"Server Version : {Assembly.GetExecutingAssembly().GetName().Version}");
    logger.Debug("============================================================");
    logger.Debug("");
  }
}