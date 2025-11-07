using System.Reflection;
using System.Threading.Tasks;
using EasyDotnet.IDE.Commands;
using Spectre.Console.Cli;

namespace EasyDotnet.IDE;

static class Program
{
  public static async Task<int> Main(string[] args)
  {
    var app = new CommandApp<RunCommand>();

    app.Configure(config =>
    {
      config.SetApplicationName("easydotnet");
      config.AddCommand<GenerateRpcDocsCommand>("generate-rpc-docs")
              .WithDescription("Generate RPC documentation in JSON or Markdown format.");
      config.SetApplicationVersion(Assembly.GetExecutingAssembly().GetName().Version!.ToString());
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
    });

    return await app.RunAsync(args);
  }

}