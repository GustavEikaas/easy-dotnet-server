using System.Reflection;
using System;
using System.Threading.Tasks;
using EasyDotnet.IDE.Commands;
using Spectre.Console.Cli;
using System.Linq;

namespace EasyDotnet.IDE;

static class Program
{
  public static async Task<int> Main(string[] args)
  {
    var app = new CommandApp<RunCommand>();
    Console.WriteLine(string.Join(',', args));
    Console.WriteLine("=== Raw Arguments ===");
    for (var i = 0; i < args.Length; i++)
    {
      Console.WriteLine($"Arg[{i}]: '{args[i]}'");
      Console.WriteLine($"  Length: {args[i].Length}");
      Console.WriteLine($"  Bytes: {string.Join(" ", args[i].Select(c => ((int)c).ToString("X2")))}");
    }
    app.Configure(config =>
    {
      config.SetApplicationName("easydotnet");
      config.SetApplicationVersion(Assembly.GetExecutingAssembly().GetName().Version!.ToString());

      config.AddCommand<GenerateRpcDocsCommand>("generate-rpc-docs")
              .WithDescription("Generate RPC documentation in JSON or Markdown format.");

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

      config.AddBranch("roslyn", roslyn =>
          {
            roslyn.SetDescription("Roslyn language server commands.");
            roslyn.AddCommand<RoslynStartCommand>("start")
            .WithDescription("Start the Roslyn Language Server over stdio.");
          });
    });

    return await app.RunAsync(args);
  }
}