using EasyDotnet.ExternalConsole.Commands;
using Spectre.Console.Cli;

namespace EasyDotnet.ExternalConsole;

static class Program
{
  public static async Task<int> Main(string[] args)
  {
    var app = new CommandApp<DebugCommand>();
    try
    {

      return await app.RunAsync(args);
    }
    catch (Exception e)
    {
      Console.WriteLine(e);
      Console.ReadLine();
      return 5;
    }
  }
}