using System;
using Newtonsoft.Json;
using WMod.Rules;

namespace WMod.Net;

public class NetMessage
{
    public int fromPlayerId { get; set; } = -1;
    public string Type { get; set; }
    public string Payload { get; set; }
    public long Timestamp { get; set; }

    public static NetMessage Create(string type, string payload = null)
    {
        return new NetMessage
        {
            fromPlayerId = PlayerRegistry.Self.id,
            Type = type,
            Payload = payload,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
    }

    public string ToJson() => JsonConvert.SerializeObject(this);

    public static NetMessage FromJson(string json) => JsonConvert.DeserializeObject<NetMessage>(json);
}
