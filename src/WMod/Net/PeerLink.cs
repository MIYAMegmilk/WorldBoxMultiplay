using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace WMod.Net;

internal sealed class PeerLink : IDisposable
{
    private readonly TcpClient _tcp;
    private readonly Stream _stream;
    private readonly ConcurrentQueue<NetMessage> _outbox = new();
    private readonly ManualResetEventSlim _outboxSignal = new(false);
    private readonly Action<NetMessage> _onMessage;
    private readonly Action<Exception> _onClosed;
    private readonly Thread _readerThread;
    private readonly Thread _writerThread;
    private volatile bool _stopped;
    public string RemoteEndpoint { get; }

    public PeerLink(TcpClient tcp, Action<NetMessage> onMessage, Action<Exception> onClosed)
    {
        _tcp = tcp;
        _stream = tcp.GetStream();
        _onMessage = onMessage;
        _onClosed = onClosed;
        RemoteEndpoint = tcp.Client.RemoteEndPoint?.ToString() ?? "<unknown>";

        _readerThread = new Thread(ReaderLoop) { IsBackground = true, Name = $"WMod-rx-{RemoteEndpoint}" };
        _writerThread = new Thread(WriterLoop) { IsBackground = true, Name = $"WMod-tx-{RemoteEndpoint}" };
    }

    public void Start()
    {
        _readerThread.Start();
        _writerThread.Start();
    }

    public void Send(NetMessage msg)
    {
        if (_stopped) return;
        _outbox.Enqueue(msg);
        _outboxSignal.Set();
    }

    private void ReaderLoop()
    {
        try
        {
            while (!_stopped)
            {
                var msg = Framing.ReadFrame(_stream);
                _onMessage?.Invoke(msg);
            }
        }
        catch (Exception ex)
        {
            if (!_stopped) _onClosed?.Invoke(ex);
        }
        finally
        {
            Dispose();
        }
    }

    private void WriterLoop()
    {
        try
        {
            while (!_stopped)
            {
                _outboxSignal.Wait(250);
                _outboxSignal.Reset();
                while (_outbox.TryDequeue(out var msg))
                {
                    Framing.WriteFrame(_stream, msg);
                }
            }
        }
        catch (Exception ex)
        {
            if (!_stopped) _onClosed?.Invoke(ex);
        }
    }

    public void Dispose()
    {
        if (_stopped) return;
        _stopped = true;
        _outboxSignal.Set();
        try { _stream?.Dispose(); } catch { }
        try { _tcp?.Close(); } catch { }
    }
}
