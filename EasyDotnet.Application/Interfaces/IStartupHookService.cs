namespace EasyDotnet.Application.Interfaces;

public interface IStartupHookService
{
  StartupHookSession CreateSession(Dictionary<string, string>? baseEnv = null);
}