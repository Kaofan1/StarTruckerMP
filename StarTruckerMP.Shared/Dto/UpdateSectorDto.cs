using MessagePack;

namespace StarTruckerMP.Shared.Dto;

[MessagePackObject(true)]
public class UpdateSectorDto
{
    public int NetId { get; set; }
    public string Sector { get; set; } = "none";
}

