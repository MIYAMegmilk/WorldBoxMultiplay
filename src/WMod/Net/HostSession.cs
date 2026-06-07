using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace WMod.Net;

internal sealed class HostSession : IDisposable
{
    private readonly TcpListener _listener;
    private readonly Thread _acceptThread;
    private readonly List<PeerLink> _peers = new();
    private readonly object _peersLock = new();
    private readonly Action<NetMessage> _onMessage;
    private readonly Action<string> _onPeerJoin;
    private readonly Action<string, Exception> _onPeerLeave;
    private volatile bool _stopped;
    public int Port { get; }
    public string BoundEndpoint => _listener?.LocalEndpoint?.ToString();

    public HostSession(int port, Action<NetMessage> onMessage, Action<string> onPeerJoin, Action<string, Exception> onPeerLeave)
    {
        Port = port;
        _onMessage = onMessage;
        _onPeerJoin = onPeerJoin;
        _onPeerLeave = onPeerLeave;
        _listener = new TcpListener(IPAddress.Any, port);
        _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "WMod-accept" };
    }

    public void Start()
    {
        _listener.Start();
        _acceptThread.Start();
    }

    public int PeerCount
    {
        get { lock (_peersLock) return _peers.Count; }
    }

    public void Broadcast(NetMessage msg)
    {
        lock (_peersLock)
        {
            foreach (var p in _peers) p.Send(msg);
        }
    }

    private void AcceptLoop()
    {
        try
        {
            while (!_stopped)
            {
                var tcp = _listener.AcceptTcpClient();
                tcp.NoDelay = true;
                string remote = tcp.Client.RemoteEndPoint?.ToString() ?? "<?>";
                PeerLink link = null;
                link = new PeerLink(
                    tcp,
                    _onMessage,
                    ex =>
                    {
                        lock (_peersLock) _peers.Remove(link);
                        _onPeerLeave?.Invoke(remote, ex);
                    });
                lock (_peersLock) _peers.Add(link);
                link.Start();
                _onPeerJoin?.Invoke(remote);
            }
        }
        catch (Exception ex)
        {
            if (!_stopped) _onPeerLeave?.Invoke("<listener>", ex);
        }
    }

    public void Dispose()
    {
        if (_stopped) return;
        _stopped = true;
        try { _listener.Stop(); } catch { }
        lock (_peersLock)
        {
            foreach (var p in _peers) p.Dispose();
            _peers.Clear();
        }
    }
}
