using MessagePack;

namespace StarTruckerMP.Shared.Dto;

[MessagePackObject(true)]
public class PlayerDisconnectedDto
{
    public int NetId { get; set; }
}

