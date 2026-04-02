namespace EasyDotnet.IDE.Interfaces;

public interface IStartupHookService
{
  StartupHookSession CreateSession(Dictionary<string, string>? baseEnv = null);
}