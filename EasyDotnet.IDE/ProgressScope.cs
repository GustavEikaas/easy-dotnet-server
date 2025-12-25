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
    _client.SendProgress(_token, "begin", title, message);
  }
  public void Report(string message, int? percentage = null) => _ = _client.SendProgress(_token, "report", message: message, percentage: percentage);
  public void Dispose() => _client.SendProgress(_token, "end");
}