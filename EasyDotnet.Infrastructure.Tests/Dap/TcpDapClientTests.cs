using System.Net;
using EasyDotnet.Infrastructure.Dap;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyDotnet.Infrastructure.Tests.Dap;

public class TcpDapClientTests
{

  [Test]
  public async Task StartAndConnect_ThrowsTimeoutException_WhenNoClientConnects()
  {
    var client = new TcpDapClient(
        NullLogger<TcpDapClient>.Instance,
        _ => { }
    );

    var ex = await Assert.ThrowsAsync<TimeoutException>(async () =>
        await client.StartAndConnect(
            IPAddress.Any,
            8086,
            () => Task.CompletedTask,
            TimeSpan.FromSeconds(5)
        ));
  }
}
