using System;
using HarmonyLib;

namespace StarTruckerMP.Client.Patches;

/// <summary>
/// When a sector unloads, the truck and its cargo keep updating for a while after the game has
/// already unloaded their colliders and the player they point at. Every one of those updates then
/// throws a NullReferenceException on every frame, and the flood of stack traces keeps the main
/// menu from finishing its load. Only a NullReferenceException from these known update methods is
/// swallowed, each one is reported once, and every other exception still surfaces normally.
/// </summary>
internal static class UnloadNoise
{
    private static readonly System.Collections.Generic.HashSet<string> Reported = new();

    public static Exception Filter(string method, Exception exception)
    {
        if (!IsMissingReference(exception))
            return exception;

        if (Reported.Add(method))
            App.Log.LogInfo($"[Grabbable] {method} lost a reference during a scene unload, its update is skipped.");

        return null;
    }

    /// <summary>
    /// A null reference thrown inside il2cpp code reaches managed code wrapped in an
    /// Il2CppException instead of a plain NullReferenceException, so the whole chain has to be
    /// inspected before deciding that this is the known unload noise.
    /// </summary>
    private static bool IsMissingReference(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is NullReferenceException)
                return true;

            var message = current.Message;
            if (message != null && message.Contains("System.NullReferenceException", StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}

[HarmonyPatch(typeof(GrabbableObject), "Update")]
internal static class GrabbableUpdate_Patch
{
    private static Exception Finalizer(Exception __exception) => UnloadNoise.Filter("GrabbableObject.Update", __exception);
}

[HarmonyPatch(typeof(StarTruck), "Update")]
internal static class StarTruckUpdate_Patch
{
    private static Exception Finalizer(Exception __exception) => UnloadNoise.Filter("StarTruck.Update", __exception);
}

[HarmonyPatch(typeof(TruckSeat), "Update")]
internal static class TruckSeatUpdate_Patch
{
    private static Exception Finalizer(Exception __exception) => UnloadNoise.Filter("TruckSeat.Update", __exception);
}

[HarmonyPatch(typeof(EmergencyBrakeToggleSwitch), "Update")]
internal static class EmergencyBrakeUpdate_Patch
{
    private static Exception Finalizer(Exception __exception) => UnloadNoise.Filter("EmergencyBrakeToggleSwitch.Update", __exception);
}

[HarmonyPatch(typeof(SalvageScanner), "LateUpdate")]
internal static class SalvageScannerUpdate_Patch
{
    private static Exception Finalizer(Exception __exception) => UnloadNoise.Filter("SalvageScanner.LateUpdate", __exception);
}

[HarmonyPatch(typeof(TruckSystemAmbientAudio), "LateUpdate")]
internal static class TruckAmbientAudioUpdate_Patch
{
    private static Exception Finalizer(Exception __exception) => UnloadNoise.Filter("TruckSystemAmbientAudio.LateUpdate", __exception);
}
