using System;
using System.Net.Sockets;

namespace WMod.Net;

internal sealed class ClientSession : IDisposable
{
    private PeerLink _link;
    private readonly Action<NetMessage> _onMessage;
    private readonly Action<Exception> _onClosed;
    public string RemoteEndpoint => _link?.RemoteEndpoint;

    public ClientSession(Action<NetMessage> onMessage, Action<Exception> onClosed)
    {
        _onMessage = onMessage;
        _onClosed = onClosed;
    }

    public void Connect(string host, int port)
    {
        var tcp = new TcpClient();
        tcp.Connect(host, port);
        tcp.NoDelay = true;
        _link = new PeerLink(tcp, _onMessage, ex =>
        {
            _onClosed?.Invoke(ex);
            _link = null;
        });
        _link.Start();
    }

    public void Send(NetMessage msg) => _link?.Send(msg);

    public void Dispose()
    {
        _link?.Dispose();
        _link = null;
    }
}
