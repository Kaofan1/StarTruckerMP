using MessagePack;

namespace StarTruckerMP.Shared.Dto
{
    [MessagePackObject]
    public class VoiceDto
    {
        [Key(0)]
        public int NetId { get; set; }

        [Key(1)]
        public byte[] OpusData { get; set; } = [];
    }
}


