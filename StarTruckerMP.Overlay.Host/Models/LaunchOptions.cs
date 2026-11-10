namespace StarTruckerMP.Overlay.Host.Models;

internal sealed record LaunchOptions(nint GameWindowHandle, int GameProcessId, bool IgnoreCertificateErrors);

