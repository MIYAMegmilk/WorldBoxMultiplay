using System;
using System.IO;
using System.Net;
using System.Text;

namespace WMod.Net;

internal static class Framing
{
    private const int MaxFrameBytes = 64 * 1024 * 1024; // 64 MiB — accommodates compressed save snapshots

    public static void WriteFrame(Stream stream, NetMessage msg)
    {
        var json = msg.ToJson();
        var payload = Encoding.UTF8.GetBytes(json);
        var lenPrefix = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(payload.Length));
        stream.Write(lenPrefix, 0, 4);
        stream.Write(payload, 0, payload.Length);
        stream.Flush();
    }

    public static NetMessage ReadFrame(Stream stream)
    {
        var lenBuf = ReadExactly(stream, 4);
        var len = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(lenBuf, 0));
        if (len < 0 || len > MaxFrameBytes)
            throw new InvalidDataException($"WMod: invalid frame length {len}");
        var payload = ReadExactly(stream, len);
        var json = Encoding.UTF8.GetString(payload);
        return NetMessage.FromJson(json);
    }

    private static byte[] ReadExactly(Stream stream, int count)
    {
        var buf = new byte[count];
        var read = 0;
        while (read < count)
        {
            var n = stream.Read(buf, read, count - read);
            if (n <= 0) throw new EndOfStreamException("WMod: peer closed");
            read += n;
        }
        return buf;
    }
}
