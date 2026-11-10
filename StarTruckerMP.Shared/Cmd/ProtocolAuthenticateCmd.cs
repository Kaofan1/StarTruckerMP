using MessagePack;

namespace StarTruckerMP.Shared.Cmd;

[MessagePackObject]
public class ProtocolAuthenticateCmd
{
    [Key(0)]
    public string Token { get; set; }
}