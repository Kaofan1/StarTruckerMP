using System;
using HarmonyLib;

namespace StarTruckerMP.Client.Patches;

/// <summary>
/// When the game unloads a sector, the cargo grabbables keep updating for a while after their
/// colliders have already been unloaded, so GrabbableObject.Update throws a NullReferenceException
/// on every frame and floods the log until the main menu cannot finish loading. The exception is
/// swallowed for this one method and reported once, everything else still surfaces normally.
/// </summary>
[HarmonyPatch(typeof(GrabbableObject), "Update")]
internal static class GrabbableUpdate_Patch
{
    private static int _reports;

    private static Exception Finalizer(Exception __exception)
    {
        if (__exception is not NullReferenceException)
            return __exception;

        if (_reports++ == 0)
            App.Log.LogInfo("[Grabbable] A grabbable lost its collider during a scene unload, its update is skipped.");

        return null;
    }
}
