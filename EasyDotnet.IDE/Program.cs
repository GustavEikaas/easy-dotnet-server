using System.Reflection;
using System.Threading.Tasks;

using EasyDotnet.IDE.Commands;
using Spectre.Console.Cli;

class Program
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

    });

    return await app.RunAsync(args);
  }

}