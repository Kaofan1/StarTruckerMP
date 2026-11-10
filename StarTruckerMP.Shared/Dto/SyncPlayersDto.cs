using MessagePack;

namespace StarTruckerMP.Shared.Dto;

[MessagePackObject(true)]
public class SyncPlayersDto
{
    public PlayerSnapshotDto[] Players { get; set; } = [];
}

