using EasyDotnet.ExternalConsole.Commands;
using Spectre.Console.Cli;

namespace EasyDotnet.ExternalConsole;

static class Program
{
  public static async Task<int> Main(string[] args)
  {
    var app = new CommandApp<DebugCommand>();
    return await app.RunAsync(args);
  }
}