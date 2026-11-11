using System;
using MessagePack;

namespace StarTruckerMP.Shared.Cmd;

[MessagePackObject(true)]
public class ProtocolHelloCmd
{
    public ushort ProtocolVersion { get; set; } = NetProtocol.CurrentVersion;

    /// <summary>
    /// Name the player chose to be seen as, shown above their suit and truck by the other clients.
    /// Empty when the client has no name, in which case the server assigns one.
    /// </summary>
    public string PlayerName { get; set; } = string.Empty;

    /// <summary>
    /// 32-byte X25519 ephemeral public key of the client.
    /// Sent in clear during the handshake so the server can derive the shared session key.
    /// </summary>
    public byte[] ClientPublicKey { get; set; } = Array.Empty<byte>();
}

