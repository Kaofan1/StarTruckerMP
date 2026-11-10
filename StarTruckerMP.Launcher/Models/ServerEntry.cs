using System;

namespace StarTruckerMP.Launcher.Models;

public sealed class ServerEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Local server";

    public string Address { get; set; } = "127.0.0.1";

    public string Port { get; set; } = "7777";

    public override string ToString() => $"{Name} ({Address}:{Port})";
}
