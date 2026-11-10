using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace StarTruckerMP.Launcher.Services;

/// <summary>
/// Lists the Windows recording devices for the microphone picker. Names come from winmm (MME),
/// which is the same friendly name Windows shows in Sound settings - and the same kind of name
/// the client sees in Unity's device list. When the stored name is not found the client falls
/// back to auto-selection, so a stale entry can never block voice chat.
/// </summary>
internal static class AudioDevices
{
    public static IReadOnlyList<string> CaptureDeviceNames()
    {
        var names = new List<string>();

        try
        {
            var count = waveInGetNumDevs();

            for (uint index = 0; index < count; index++)
            {
                var caps = new WaveInCaps();
                if (waveInGetDevCaps(new UIntPtr(index), ref caps, (uint)Marshal.SizeOf<WaveInCaps>()) != 0)
                    continue;

                var name = caps.Name?.Trim();
                if (string.IsNullOrWhiteSpace(name) || names.Contains(name, StringComparer.OrdinalIgnoreCase))
                    continue;

                names.Add(name);
            }
        }
        catch
        {
            // Audio APIs are best effort: the picker still offers the system default entry.
        }

        return names;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WaveInCaps
    {
        public ushort ManufacturerId;
        public ushort ProductId;
        public uint DriverVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Name;

        public uint Formats;
        public ushort Channels;
        public ushort Reserved;
    }

    [DllImport("winmm.dll")]
    private static extern uint waveInGetNumDevs();

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "waveInGetDevCapsW")]
    private static extern uint waveInGetDevCaps(UIntPtr deviceId, ref WaveInCaps caps, uint size);
}
