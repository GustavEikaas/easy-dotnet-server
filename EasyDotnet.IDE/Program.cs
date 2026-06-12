using System.Reflection;
using EasyDotnet.IDE.Commands;
using Spectre.Console.Cli;

namespace EasyDotnet.IDE;

static class Program
{
  public static async Task<int> Main(string[] args)
  {
    var app = new CommandApp<RunCommand>();

    Console.ReadLine();

    app.Configure(config =>
    {
      config.SetApplicationName("easydotnet");
      config.SetApplicationVersion(Assembly.GetExecutingAssembly().GetName().Version!.ToString());

      config.AddCommand<GenerateRpcDocsCommand>("generate-rpc-docs")
              .WithDescription("Generate RPC documentation in JSON or Markdown format.");

      config.AddCommand<HealthCheckCommand>("healthcheck")
              .WithDescription("Print machine-readable health information as JSON.");

      config.AddBranch("compat", compat =>
        {
          compat.SetDescription("Run compatibility commands (run, build, test).");

          compat.AddCommand<CompatRunCommand>("run")
           .WithDescription("Build and run a .NET project.");

          compat.AddCommand<CompatRunIisCommand>("run-iis")
           .WithDescription("Build and run a .NET project using IIS Express.");

          compat.AddCommand<CompatBuildCommand>("build")
           .WithDescription("Build a project using MSBuild.");

          compat.AddCommand<CompatTestCommand>("test")
           .WithDescription("Build and run tests using VSTest.");
        });

      config.AddCommand<ProjXLanguageServerCommand>("projx-language-server")
        .WithDescription("Start the ProjX Language Server over stdio.");

      config.AddBranch("roslyn", roslyn =>
          {
            roslyn.SetDescription("Roslyn language server commands.");
            roslyn.AddCommand<RoslynStartCommand>("start")
            .WithDescription("Start the Roslyn Language Server over stdio.");
            roslyn.AddCommand<RoslynExtensionInfoCommand>("extension-info")
            .WithDescription("Print bundled EasyDotnet Roslyn extension metadata as JSON.");
            roslyn.AddCommand<RoslynToolInstallCommand>("install")
            .WithDescription("Install the roslyn-language-server global tool.");
            roslyn.AddCommand<RoslynToolUpdateCommand>("update")
            .WithDescription("Update the roslyn-language-server global tool.");
          });
    });

    return await app.RunAsync(args);
  }
}