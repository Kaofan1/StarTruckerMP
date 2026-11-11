using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.Attributes;
using Microsoft.Extensions.Caching.Memory;
using StarTruckerMP.Client.Synchronization;
using StarTruckerMP.Client.UI;
using StarTruckerMP.Shared;
using StarTruckerMP.Shared.Cmd;
using StarTruckerMP.Shared.Dto;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;
using Object = Il2CppSystem.Object;
using Quaternion = UnityEngine.Quaternion;
using SynchronizationContext = Il2CppSystem.Threading.SynchronizationContext;
using Vector3 = UnityEngine.Vector3;

namespace StarTruckerMP.Client.Components;

public class NetworkEventsComponent : MonoBehaviour
{
    private bool _connected;
    private SynchronizationContext _mainThreadContext;

    private void Awake()
    {
        _mainThreadContext = SynchronizationContext.Current;
        DontDestroyOnLoad(gameObject);
        
        Network.OnConnected += HandleConnected;
        Network.OnDisconnected += HandleDisconnected;
        Network.OnPlayerDisconnected += HandlePlayerDisconnected;
        Network.OnPlayerNameUpdate += HandlePlayerNameUpdate;
        Network.OnPlayerSectorUpdate += HandlePlayerSectorUpdate;
        Network.OnPlayerPositionUpdate += HandlePlayerPositionUpdate;
        Network.OnTruckLiveryUpdate += HandleTruckLiveryUpdate;
        Network.OnTrailerUpdate += HandleTrailerUpdate;
        
        App.Log.LogInfo("NetworkEventsComponent Awake and subscribed to network events");
    }

    private IMemoryCache _players = new MemoryCache(new MemoryCacheOptions());

    // The cache cannot be enumerated, so the ids of the remote players are tracked next to it.
    private readonly HashSet<int> _knownIds = new();

    // A name arrives before the sector update that spawns the remote player, so names are kept
    // apart from the objects that only exist while the player shares our sector.
    private readonly Dictionary<int, string> _names = new();

    private class NetPlayer
    {
        public int PlayerId { get; set; }
        public string PlayerName { get; set; }
        public GameObject TruckObj { get; set; }
        public GameObject PlayerObj { get; set; }
        public GameObject SuitObj { get; set; }
        public GameObject TruckLabel { get; set; }
        public GameObject SuitLabel { get; set; }
    }

    [HideFromIl2Cpp]
    private NetPlayer CreateNetPlayer(int netId)
    {
        var player = new NetPlayer
        {
            PlayerId = netId,
            PlayerName = LabelFor(netId)
        };

        var go = GameObject.Find("[Sector]");
        var scene = go?.scene;
        if (scene == null)
        {
            // No sector means the game is at the main menu or still loading, and spawning a remote
            // truck there only leaves broken objects behind for the next scene.
            App.Log.LogWarning($"({netId}) No sector root object, skipping the remote player for now.");
            return null;
        }

        #region Truck setup

        player.TruckObj = TruckFactory.CreatePlayerTruck(0, Vector3.zero, Quaternion.identity);
        if (player.TruckObj == null) App.Log.LogError($"({netId}) Failed to create player truck object");
        App.Log.LogInfo($"({netId}) Created player truck object");

        var rigidbody = player.TruckObj.GetComponent<Rigidbody>();
        // The clone keeps the colliders it was built with, so it is a solid body instead of a
        // texture the local truck can drive through.
        rigidbody.detectCollisions = true;
        DisableInteractions(player.TruckObj);

        #endregion

        #region Player setup

        player.PlayerObj = new GameObject($"RemotePlayer-{netId}");
        App.Log.LogInfo($"({netId}) Created player object");
        SceneManager.MoveGameObjectToScene(player.PlayerObj, scene!.Value);
        App.Log.LogInfo($"({netId}) Moved player object to scene");
        player.PlayerObj.transform.SetParent(null);

        #endregion
        
        #region Player suit setup

        var suit = Instantiate(PlayerState.SpaceSuit, Vector3.zero, Quaternion.identity, player.PlayerObj.transform);
        App.Log.LogInfo($"({netId}) Instantiated player suit");
        player.SuitObj = suit;
        if (PlayerState.SpaceSuitMats != null)
            PlayerState.SpaceSuitMats.CopyTo(suit.GetComponent<MeshRenderer>().materials, 0);
        suit.active = true;
        suit.name = $"ClientSuit-{netId}";
        Destroy(suit.transform.GetComponent<SpaceSuitController>());
        Destroy(suit.transform.GetComponent<CapsuleCollider>());
        Destroy(suit.transform.GetComponent<OutlinableSetterUpper>());
        Destroy(suit.transform.GetComponent<EPOOutline.Outlinable>());
        Destroy(suit.transform.GetComponent<EPOOutline.TargetStateListener>());
        Destroy(suit.transform.GetComponent<MaterialSwitcher>());
        Destroy(suit.transform.GetComponent<InteractTarget>());
        Destroy(suit.transform.GetComponent<DoorController>());
        App.Log.LogInfo($"({netId}) Configured player suit");
        
        #endregion
        
        #region Name labels

        // A label that cannot be built must never stop the remote player from being created.
        try
        {
            player.TruckLabel = Nameplates.CreateTruckLabel(player.TruckObj.transform, player.PlayerName);
            player.SuitLabel = Nameplates.CreateSuitLabel(player.PlayerObj.transform, player.PlayerName);
            App.Log.LogInfo($"({netId}) Name labels created for '{player.PlayerName}'.");
        }
        catch (Exception exception)
        {
            App.Log.LogError($"({netId}) Failed to create the name labels: {exception}");
        }

        #endregion

        App.Log.LogInfo($"Created NetPlayer for netId {netId}");
        
        return player;
    }

    private void RecreateNetPlayerTruck(int netId, int cargoCount)
    {
        if (!_players.TryGetValue(netId, out NetPlayer player))
            return;
        
        // copy actual data
        player.TruckObj.transform.GetPositionAndRotation(out var currentPos, out var currentRot);
        var customizer = player.TruckObj.GetComponent<AIVehicleCustomiser>();
        var liveryId = customizer.m_cabLiveryApplier.CurrentLiveryId ?? customizer.m_cabLiveryApplier.AppliedLiveryId;
        // delete
        player.TruckObj.SetActive(false);
        // DestroyImmediate during a scene change corrupts the game's own object lists and stops the
        // main menu from loading, so the old truck is removed at the end of the frame instead.
        Destroy(player.TruckObj);
        // recreate with same data
        player.TruckObj = TruckFactory.CreatePlayerTruck(cargoCount, currentPos, currentRot);
        customizer = player.TruckObj.GetComponent<AIVehicleCustomiser>();
        customizer.AssignCabLivery(liveryId, 0f);
        var rigidbody = player.TruckObj.GetComponent<Rigidbody>();
        rigidbody.detectCollisions = true;
        DisableInteractions(player.TruckObj);
        // The rebuilt truck is a new object, so its label has to follow the new transform.
        Nameplates.SetTarget(player.TruckLabel, player.TruckObj.transform);
        App.Log.LogInfo($"Recreated truck for player {netId} with cargo count {cargoCount}");
    }

    /// <summary>
    /// A remote truck must not be interactive. Its own colliders are kept so it stays a solid body
    /// the local truck cannot drive through, and only the behaviours are switched off: destroying
    /// the colliders used to leave the game's grabbable components pointing at dead objects, which
    /// spammed a NullReferenceException every frame and eventually killed the process.
    /// </summary>
    private static void DisableInteractions(GameObject truck)
    {
        if (truck == null)
            return;

        // The clone is a full player truck: seats, brakes, scanners, grabbables, ambient audio and
        // the truck itself all keep running their Update loops and expect the real player, which
        // flooded the log with exceptions every frame and kept the main menu from loading. Only the
        // network controller is allowed to tick.
        foreach (var behaviour in truck.GetComponentsInChildren<Behaviour>(true))
        {
            if (behaviour == null)
                continue;

            if (behaviour.TryCast<TruckControllerComponent>() != null)
                continue;

            behaviour.enabled = false;
        }

        var colliders = 0;
        foreach (var collider in truck.GetComponentsInChildren<Collider>(true))
        {
            if (collider != null && collider.enabled)
                colliders++;
        }

        App.Log.LogInfo($"Remote truck keeps {colliders} collider(s) enabled.");
    }

    [HideFromIl2Cpp]
    private void HandleTruckLiveryUpdate(UpdateLiveryDto liveryDto)
    {
        PostToMainThread(() =>
        {
            if (!_players.TryGetValue(liveryDto.NetId, out NetPlayer player)) return;
            
            var customiser = player.TruckObj.GetComponentInChildren<AIVehicleCustomiser>(true);
            customiser.AssignCabLivery(liveryDto.Livery, 0f);
        });
    }

    [HideFromIl2Cpp]
    private void HandlePlayerPositionUpdate(UpdatePositionDto positionDto)
    {
        PostToMainThread(() =>
        {
            if (!_players.TryGetValue(positionDto.NetId, out NetPlayer player)) return;

            if (positionDto.IsTruck && player.TruckObj != null)
            {
                var controller = player.TruckObj.GetComponent<TruckControllerComponent>();
                if (controller != null)
                    controller.ApplyNetworkState(
                        Vec(positionDto.Position),
                        Quat(positionDto.Rotation),
                        Vec(positionDto.Velocity)
                        );
                else App.Log.LogError("TruckControllerComponent is NULL");
            }
            else if (!positionDto.IsTruck && player.PlayerObj != null)
            {
                player.PlayerObj.transform.SetPositionAndRotation(
                    Vec(positionDto.Position),
                    Quat(positionDto.Rotation)
                );
                var rigid = player.PlayerObj.transform.GetComponent<Rigidbody>();
                if (rigid != null)
                {
                    rigid.velocity = Vec(positionDto.Velocity);
                    rigid.angularVelocity = Vec(positionDto.AngVel);
                }
            }

            // A player sitting in the truck is shown by the truck label, so the suit label is hidden
            // to keep the cabin from carrying two names on top of each other.
            if (positionDto.IsTruck)
                Nameplates.SetVisible(player.SuitLabel, !positionDto.InSeat);
        });
    }

    /// <summary>
    /// A callback posted to the Unity main thread must never throw: an exception escaping it
    /// reaches il2cpp_unhandled_exception and takes the whole process down.
    /// </summary>
    [HideFromIl2Cpp]
    private void PostToMainThread(Action action)
    {
        _mainThreadContext.Post(new Action<Object>(_ =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                App.Log.LogError("Network event handler failed: " + exception);
            }
        }), null);
    }

    /// <summary>Name and network id shown above a remote player, e.g. "Kaofan #3".</summary>
    private string LabelFor(int netId) =>
        _names.TryGetValue(netId, out var name) && !string.IsNullOrWhiteSpace(name)
            ? $"{name} #{netId}"
            : $"Player #{netId}";

    [HideFromIl2Cpp]
    private void HandlePlayerNameUpdate(int netId, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        PostToMainThread(() =>
        {
            _names[netId] = name;

            if (!_players.TryGetValue(netId, out NetPlayer player))
                return;

            player.PlayerName = LabelFor(netId);
            Nameplates.SetText(player.TruckLabel, player.PlayerName);
            Nameplates.SetText(player.SuitLabel, player.PlayerName);
            App.Log.LogInfo($"Remote player {netId} is now shown as '{player.PlayerName}'.");
        });
    }

    private static Vector3 Vec(global::StarTruckerMP.Shared.Vector3 vec) => new(vec.X, vec.Y, vec.Z);
    
    private static Quaternion Quat(global::StarTruckerMP.Shared.Quaternion quat) => new(quat.X, quat.Y, quat.Z, quat.W);
    
    [HideFromIl2Cpp]
    private void HandleTrailerUpdate(UpdateTrailerDto trailerDto)
    {
        PostToMainThread(() =>
        {
            if (!_players.TryGetValue(trailerDto.NetId, out NetPlayer player)) return;

            App.Log.LogInfo($"Trailer update info for player {trailerDto.NetId}: TrailerCount=[{trailerDto.TrailerCount}], LiveryId='{trailerDto.LiveryId}', CargoTypeId='{trailerDto.CargoTypeId}'");
            
            RecreateNetPlayerTruck(trailerDto.NetId, trailerDto.TrailerCount);

            if (trailerDto.TrailerCount == 0) return; // nothing to do
            
            var existingSlots = player.TruckObj.GetComponentsInChildren<AIVehicleContainerSlot>(true);
            
            foreach (var slot in existingSlots)
            {
                if (slot == null)
                {
                    App.Log.LogInfo($"Tried to spawn container for {trailerDto.NetId} but no AIVehicleContainerSlot found in truck hierarchy");
                    return;
                }
                if (CargoMetadataProvider.instance == null)
                {
                    App.Log.LogError($"CargoMetadataProvider is null, cannot spawn container for {trailerDto.NetId}");
                    return;
                }
                if (CargoMetadataProvider.instance.cargoCatalogue == null)
                {
                    App.Log.LogError($"CargoCatalogue is null, cannot spawn container for {trailerDto.NetId}");
                    return;
                }

                CargoType cargoType = null;
                if (!string.IsNullOrEmpty(trailerDto.CargoTypeId))
                {
                    if (!CargoMetadataProvider.instance.cargoCatalogue.lookUp.TryGetValue(trailerDto.CargoTypeId, out cargoType))
                        App.Log.LogWarning($"GetById returned null for CargoTypeId '{trailerDto.CargoTypeId}', falling back to index 0");
                }
                cargoType ??= CargoMetadataProvider.instance.cargoCatalogue.GetByIndex(0);
                if (cargoType == null)
                {
                    App.Log.LogError($"Cargo type at index 0 is null, cannot spawn container for {trailerDto.NetId}");
                    return;
                }
                if (cargoType.container == null)
                {
                    App.Log.LogError($"cargoType.container is null for {trailerDto.NetId}, cannot spawn container");
                    return;
                }

                var liveryAssetRef = CustomizationManager.instance.GetLiveryAssetRefFromId(trailerDto.LiveryId);
                if (liveryAssetRef == null)
                    App.Log.LogWarning($"GetLiveryAssetRefFromId returned null for livery ID '{trailerDto.LiveryId}', SpawnContainer may fail");

                App.Log.LogInfo($"Spawning container for {trailerDto.NetId}: cargoType={cargoType}, container={cargoType.container}, liveryAssetRef={liveryAssetRef}");
                
                slot.SpawnContainer(new AIVehicleCustomizationData.CargoContainerData
                {
                    m_container = cargoType.container,
                    m_cargoType = cargoType,
                    m_containerLivery = liveryAssetRef,
                    m_damagePercent = 0f
                });
            }
        });
    }

    [HideFromIl2Cpp]
    private void HandlePlayerSectorUpdate(UpdateSectorDto sectorDto)
    {
        PostToMainThread(() =>
        {
            if (sectorDto.Sector != PlayerState.Sector)
            {
                if (_players.TryGetValue(sectorDto.NetId, out NetPlayer leaving))
                    DestroyNetPlayer(leaving);

                _players.Remove(sectorDto.NetId);
                _knownIds.Remove(sectorDto.NetId);
                App.Log.LogInfo($"Removed player {sectorDto.NetId} from cache due to sector change ({sectorDto.Sector} != {PlayerState.Sector})");
                return;
            }

            lock (sectorDto.NetId.ToString())
            {
                if (string.IsNullOrEmpty(PlayerState.Sector))
                    return;

                // A peer can repeat its sector update, and creating another truck for it every time
                // left orphaned clones behind. An existing remote player is kept as it is.
                if (_players.TryGetValue(sectorDto.NetId, out NetPlayer existing))
                {
                    if (existing.TruckObj != null)
                        return;

                    DestroyNetPlayer(existing);
                    _players.Remove(sectorDto.NetId);
                    _knownIds.Remove(sectorDto.NetId);
                }

                var netPlayer = CreateNetPlayer(sectorDto.NetId);
                if (netPlayer == null)
                    return;

                _players.Set(sectorDto.NetId, netPlayer);
                _knownIds.Add(sectorDto.NetId);
                App.Log.LogInfo($"Added player {sectorDto.NetId} to cache for sector {sectorDto.Sector}");
            }
        });
    }

    private void HandlePlayerDisconnected(int netId)
    {
        PostToMainThread(() =>
        {
            lock (netId.ToString())
            {
                if (!_players.TryGetValue(netId, out NetPlayer player)) return;
                DestroyNetPlayer(player);
                _players.Remove(netId);
                _knownIds.Remove(netId);
            }
        });
    }

    private void HandleDisconnected()
    {
        _connected = false;
    }

    private void HandleConnected(int netId)
    {
        _connected = true;
        Network.SendServerMessage(new UpdateSectorCmd { Sector = PlayerState.Sector }, PacketType.UpdateSector);
        
        // Send livery
        if (PlayerState.Truck != null)
        {
            var truckInfo = Utils.ExtractTruckInfo(PlayerState.Truck);
            if (!string.IsNullOrEmpty(truckInfo.LiveryId))
            {
                Network.SendServerMessage(new UpdateLiveryCmd { Livery = truckInfo.LiveryId }, PacketType.UpdateLivery);
                App.Log.LogInfo($"Sent livery to server: {truckInfo.LiveryId}");
            }
            else
            {
                App.Log.LogWarning("Could not extract livery from player truck, skipping livery update.");
            }
        }
    }

    private int _activeSceneHandle = -1;

    private void Update()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.handle == _activeSceneHandle)
            return;

        _activeSceneHandle = scene.handle;

        // A remote truck that outlives its sector keeps the game's own objects alive with dead
        // references, so every clone is removed as soon as the scene is replaced.
        ClearAllPlayers("the active scene changed");
    }

    private void OnApplicationQuit()
    {
        Plugin.NotifyShuttingDown();
        ClearAllPlayers("the game is quitting");
    }

    /// <summary>
    /// Destroys the truck and the suit of a remote player. Leaving them behind after a scene change
    /// keeps the game's own objects alive with references to objects that no longer exist.
    /// </summary>
    private static void DestroyNetPlayer(NetPlayer player)
    {
        if (player == null)
            return;

        if (player.TruckObj != null)
        {
            player.TruckObj.SetActive(false);
            Destroy(player.TruckObj);
        }

        if (player.PlayerObj != null)
        {
            player.PlayerObj.SetActive(false);
            Destroy(player.PlayerObj);
        }

        if (player.TruckLabel != null)
            Destroy(player.TruckLabel);

        if (player.SuitLabel != null)
            Destroy(player.SuitLabel);
    }

    private void ClearAllPlayers(string reason)
    {
        if (_knownIds.Count == 0)
            return;

        var removed = 0;

        foreach (var netId in new List<int>(_knownIds))
        {
            if (_players.TryGetValue(netId, out NetPlayer player))
            {
                DestroyNetPlayer(player);
                removed++;
            }

            _players.Remove(netId);
        }

        _knownIds.Clear();
        App.Log.LogInfo($"Cleared {removed} remote player(s) because {reason}.");
    }

    private void Unsubscribe()
    {
        Network.OnConnected -= HandleConnected;
        Network.OnDisconnected -= HandleDisconnected;
        Network.OnPlayerDisconnected -= HandlePlayerDisconnected;
        Network.OnPlayerNameUpdate -= HandlePlayerNameUpdate;
        Network.OnPlayerSectorUpdate -= HandlePlayerSectorUpdate;
        Network.OnPlayerPositionUpdate -= HandlePlayerPositionUpdate;
        Network.OnTruckLiveryUpdate -= HandleTruckLiveryUpdate;
        Network.OnTrailerUpdate -= HandleTrailerUpdate;
    }
    
    private void OnDisable()
    {
        Unsubscribe();
    }

    private void OnDestroy()
    {
        ClearAllPlayers("the network component is destroyed");
        Unsubscribe();
    }
}