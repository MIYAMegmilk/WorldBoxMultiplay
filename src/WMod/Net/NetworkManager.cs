using System;
using System.Collections.Concurrent;

namespace WMod.Net;

[Flags]
internal enum NetRole { None = 0, Host = 1, Client = 2 }

internal static class NetworkManager
{
    public const int DefaultPort = 7777;

    public static NetRole Role { get; private set; } = NetRole.None;
    private static HostSession _host;
    private static ClientSession _client;

    private static readonly ConcurrentQueue<NetMessage> _inbox = new();
    public static event Action<NetMessage> OnMessage;

    public static void StartHost(int port = DefaultPort)
    {
        StopHost();
        _host = new HostSession(
            port,
            onMessage: m => _inbox.Enqueue(m),
            onPeerJoin: remote => _inbox.Enqueue(NetMessage.Create("_SYS", $"peer joined: {remote}")),
            onPeerLeave: (remote, ex) => _inbox.Enqueue(NetMessage.Create("_SYS", $"peer left: {remote} ({ex?.Message})")));
        _host.Start();
        Role |= NetRole.Host;
    }

    public static void ConnectClient(string host, int port = DefaultPort)
    {
        StopClient();
        _client = new ClientSession(
            onMessage: m => _inbox.Enqueue(m),
            onClosed: ex => { _inbox.Enqueue(NetMessage.Create("_SYS", $"disconnected: {ex?.Message}")); Role &= ~NetRole.Client; });
        _client.Connect(host, port);
        Role |= NetRole.Client;
    }

    public static void Send(NetMessage msg)
    {
        if ((Role & NetRole.Client) != 0) _client?.Send(msg);
        else if ((Role & NetRole.Host) != 0) _host?.Broadcast(msg);
    }

    public static void Shutdown() { StopClient(); StopHost(); }

    private static void StopHost()
    {
        _host?.Dispose(); _host = null;
        Role &= ~NetRole.Host;
    }

    private static void StopClient()
    {
        _client?.Dispose(); _client = null;
        Role &= ~NetRole.Client;
    }

    public static void DrainInbox()
    {
        while (_inbox.TryDequeue(out var msg))
        {
            try { OnMessage?.Invoke(msg); } catch { }
        }
    }

    public static string StatusLine()
    {
        var parts = new System.Collections.Generic.List<string>();
        if ((Role & NetRole.Host) != 0) parts.Add($"Host@{_host?.BoundEndpoint} peers={_host?.PeerCount}");
        if ((Role & NetRole.Client) != 0) parts.Add($"Client->{_client?.RemoteEndpoint}");
        return parts.Count == 0 ? "Disconnected" : string.Join(" | ", parts);
    }
}
