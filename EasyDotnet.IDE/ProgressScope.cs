using System;
using EasyDotnet.Application.Interfaces;

namespace EasyDotnet.IDE;

public sealed class ProgressScope : IDisposable
{
  private readonly IClientService _client;
  private readonly string _token;

  public ProgressScope(IClientService client, string title, string message)
  {
    _client = client;
    _token = Guid.NewGuid().ToString();
    _client.SendProgressStart(_token, title, message);
  }
  public void Report(string message, int? percentage = null) => _ = _client.SendProgressUpdate(_token, message: message, percentage: percentage);
  public void Dispose() => _client.SendProgressEnd(_token);
}