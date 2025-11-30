using System.Threading.Channels;

namespace EasyDotnet.Debugger.Messages;

public interface IMessageChannels
{
  ChannelReader<ProtocolMessage> ClientToProxyReader { get; }
  ChannelWriter<ProtocolMessage> ClientToProxyWriter { get; }

  ChannelReader<ProtocolMessage> DebuggerToProxyReader { get; }
  ChannelWriter<ProtocolMessage> DebuggerToProxyWriter { get; }

  ChannelReader<string> ProxyToClientReader { get; }
  ChannelWriter<string> ProxyToClientWriter { get; }

  ChannelReader<string> ProxyToDebuggerReader { get; }
  ChannelWriter<string> ProxyToDebuggerWriter { get; }

  void CompleteAll();
}

public class MessageChannels : IMessageChannels
{
  private readonly Channel<ProtocolMessage> _clientToProxy;
  private readonly Channel<ProtocolMessage> _debuggerToProxy;
  private readonly Channel<string> _proxyToClient;
  private readonly Channel<string> _proxyToDebugger;

  public MessageChannels()
  {
    _clientToProxy = Channel.CreateUnbounded<ProtocolMessage>(new UnboundedChannelOptions
    {
      SingleReader = true,
      SingleWriter = true
    });

    _debuggerToProxy = Channel.CreateUnbounded<ProtocolMessage>(new UnboundedChannelOptions
    {
      SingleReader = true,
      SingleWriter = true
    });

    _proxyToClient = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
      SingleReader = true,
      SingleWriter = true
    });

    _proxyToDebugger = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
      SingleReader = true,
      SingleWriter = true
    });
  }

  public ChannelReader<ProtocolMessage> ClientToProxyReader => _clientToProxy.Reader;
  public ChannelWriter<ProtocolMessage> ClientToProxyWriter => _clientToProxy.Writer;

  public ChannelReader<ProtocolMessage> DebuggerToProxyReader => _debuggerToProxy.Reader;
  public ChannelWriter<ProtocolMessage> DebuggerToProxyWriter => _debuggerToProxy.Writer;

  public ChannelReader<string> ProxyToClientReader => _proxyToClient.Reader;
  public ChannelWriter<string> ProxyToClientWriter => _proxyToClient.Writer;

  public ChannelReader<string> ProxyToDebuggerReader => _proxyToDebugger.Reader;
  public ChannelWriter<string> ProxyToDebuggerWriter => _proxyToDebugger.Writer;

  public void CompleteAll()
  {
    _clientToProxy.Writer.Complete();
    _debuggerToProxy.Writer.Complete();
    _proxyToClient.Writer.Complete();
    _proxyToDebugger.Writer.Complete();
  }
}