namespace EasyDotnet.IDE.Interfaces;

public interface IAspireService
{
  public Task<string[]> GetExecutableIntegrations(string appHost, CancellationToken cancellationToken);
}
