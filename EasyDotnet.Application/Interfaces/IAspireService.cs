namespace EasyDotnet.Application.Interfaces;

public interface IAspireService
{
  public Task<string[]> GetExecutableIntegrations(string appHost, CancellationToken cancellationToken);
}