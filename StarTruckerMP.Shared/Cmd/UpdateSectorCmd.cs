using MessagePack;

namespace StarTruckerMP.Shared.Cmd
{
    [MessagePackObject]
    public class UpdateSectorCmd
    {
        [Key(0)]
        public string Sector { get; set; } = "none";
    }
}