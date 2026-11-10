using HarmonyLib;
using UnityEngine;

namespace StarTruckerMP.Client.Patches;

[HarmonyPatch(typeof(AIVehicleBase), nameof(AIVehicleBase.Start))]
public class AIVehicleTruck_Patch
{
    static GameObject _cachedTruckPrefab;

    public static GameObject GetPrefab() => _cachedTruckPrefab;

    static void Postfix(AIVehicleBase __instance)
    {
        if (__instance.TryCast<AIVehicle_Truck>() != null)
        {
            _cachedTruckPrefab = __instance.gameObject;
        }
    }
}