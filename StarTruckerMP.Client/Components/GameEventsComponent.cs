using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using StarTruckerMP.Client.Synchronization;
using StarTruckerMP.Client.UI;
using StarTruckerMP.Shared;
using StarTruckerMP.Shared.Cmd;
using UnityEngine;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

namespace StarTruckerMP.Client.Components;

public class GameEventsComponent : MonoBehaviour
{
    public static event Action<string> ArrivedAtSector;
    
    private void Awake()
    {
        App.Log.LogInfo("GameEventsComponent Awake");
        DontDestroyOnLoad(gameObject);
        OverlayManager.MessageReceived += (type, data) =>
        {
            switch (type)
            {
                case "inspectObjectExtra":
                    App.Log.LogInfo($"[Object Inspector] request extra data for object {data}");
                    GetMoreData(data);
                    break;
                case "overlayLoaded":
                    // The page asks for the current state as soon as it mounts, because the
                    // overlay process usually starts long before the browser finishes loading.
                    ReportOverlayState();
                    break;
                case "overlayModeChanged":
                    // The overlay window closes the panel with Escape, so its own mode change has to
                    // reach the page too, otherwise the panel would stay on screen.
                    ReportOverlayMode();
                    break;
            }
        };

        Network.OnConnected += netId => Connected();
        Network.OnDisconnected += () => ReportOverlaySession(connected: false);
    }

    private bool _loaded;
    
    private GameObject _player;
    private GameObject _playerCam;
    private Rigidbody _playerRigid;
    private PlayerLocation _playerLocation;

    private GameObject _truck;
    private Rigidbody _truckRigid;
    private StarTruck _starTruck;
    
    private void OnArrivedAtSector(Il2CppSystem.Object sender, Il2CppSystem.EventArgs args)
    {
        PlayerState.Sector = GameObject.Find("[Sector]").scene.name;
        ArrivedAtSector?.Invoke(PlayerState.Sector);
        
        // This event also happens when the player loads a saved game, so the references are kept
        // while they are alive. Going back to the main menu destroys those objects, and the next
        // load has to resolve them again instead of reusing dead il2cpp pointers.
        if (_loaded && _player != null && _truck != null) return;
        _loaded = true;

        _player = GameObject.FindGameObjectWithTag("Player");
        PlayerState.Player = _player;
        _playerCam = GameObject.FindGameObjectWithTag("MainCamera");
        _playerRigid = _player?.GetComponent<Rigidbody>();
        _playerLocation = _player?.GetComponent<PlayerLocation>();
        
        _truck = GameObject.Find("StarTruck(Clone)"); // clone?
        if (_truck == null)
        {
            App.Log.LogWarning("[GameEvents] No truck in this scene yet, waiting for the next arrival.");
            _loaded = false;
            return;
        }

        if (_truck.GetComponent<CbRadioPttComponent>() == null)
            _truck.AddComponent<CbRadioPttComponent>();

        PlayerState.Truck = _truck;
        _truckRigid = _truck?.GetComponent<Rigidbody>();
        var truckInterior = _truck?.transform.Find("Interior");
        var root = truckInterior?.Find("SpaceSuit_Root");
        var suit = root?.Find("SpaceSuit");
        if (suit && suit.childCount > 0) 
            PlayerState.SpaceSuit = suit.GetChild(0).gameObject;

        _starTruck = _truck.GetComponentInChildren<StarTruck>();
        var connector = _starTruck?.maglockConnector;
        if (connector != null)
        {
            connector.hitchControl.onTriggered += new System.Action<Il2CppSystem.Object, Il2CppSystem.EventArgs>((s, e) =>
            {
                var current = _starTruck?.maglockConnector;
                if (current == null) return;

                if (current.hitchedCargo)
                    OnHitchCargo(current.hitchedCargo);
                else
                    OnUnhitchCargo();
            });
        }
        else
        {
            App.Log.LogWarning("[GameEvents] The truck has no maglock connector, cargo updates are disabled until the next arrival.");
        }

        if (!_pptRunning) PlayerPositionThread();
        if (!_tptRunning) TruckPositionThread();
    }

    private float _nextFontFallbackCheck;

    private static void ReportOverlayState()
    {
        ReportOverlayMode();
        ReportOverlaySession(Network.NetId != -1);
    }

    private static void ReportOverlayMode() =>
        OverlayManager.PostMessage("overlayModeChanged", new { interactive = OverlayManager.IsInteractiveMode });

    private static void ReportOverlaySession(bool connected) =>
        OverlayManager.PostMessage("session", new
        {
            connected,
            server = $"{App.ServerAddress.Value}:{App.ServerPort.Value}",
            netId = connected ? Network.NetId : -1
        });

    private void Connected()
    {
        ReportOverlaySession(connected: true);

        // The handshake can finish before the truck exists (or with a connector that has no
        // cargo attached yet), so every step is optional here.
        var connector = _starTruck?.maglockConnector;
        if (connector is null || !connector.hitched)
            return;

        var cargo = connector.hitchedCargo;
        if (cargo is null)
            return;

        OnHitchCargo(cargo);
    }

    private void OnUnhitchCargo()
    {
        App.Log.LogInfo("Unhitched cargo");
        // we unhitched a cargo, we need to notify the server
        Network.SendServerMessage(new UpdateTrailerCmd()
        {
            TrailerCount = 0,
            LiveryId = null
        }, PacketType.UpdateTrailer);
    }

    private void OnHitchCargo(CargoContainer cargo)
    {
        if (cargo is null)
        {
            App.Log.LogWarning("Hitched cargo event without a cargo container, skipping.");
            return;
        }

        // A cargo that is still being initialised has no damage applier or record yet. Throwing
        // here used to abort the whole OnConnected list, which also killed the sector and livery
        // updates the server needs, so nothing is allowed to escape this method.
        try
        {
            App.Log.LogInfo("Hitched cargo");

            var trailersCount = _starTruck?.HitchedTrailersCount ?? 0;
            var damageApplier = cargo.damageApplier;
            var livery = damageApplier is null
                ? null
                : damageApplier.CurrentLiveryId ?? damageApplier.AppliedLiveryId;

            if (string.IsNullOrEmpty(livery))
                App.Log.LogError("Couldn't retrieve livery for hitched cargo, sending null");

            var cargoType = cargo.cargoRecord?.cargoType;
            string cargoTypeId = null;

            var catalogue = CargoMetadataProvider.instance?.cargoCatalogue;
            if (cargoType is not null && catalogue is not null)
            {
                foreach (var kvp in catalogue.lookUp)
                {
                    if (kvp.value._displayNameId == cargoType._displayNameId)
                    {
                        cargoTypeId = kvp.key;
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(cargoTypeId))
                App.Log.LogError("Couldn't retrieve cargoTypeId for hitched cargo, sending null");

            App.Log.LogInfo($"Cargo data: {trailersCount}, {livery}, {cargoTypeId}");

            Network.SendServerMessage(new UpdateTrailerCmd()
            {
                TrailerCount = trailersCount,
                LiveryId = livery,
                CargoTypeId = cargoTypeId
            }, PacketType.UpdateTrailer);
        }
        catch (Exception exception)
        {
            App.Log.LogError("Could not report the hitched cargo: " + exception.Message);
        }
    }

    private bool _subscribedToSectorArrival = false;
    
    private void Update()
    {
        if (SectorPersistence.instance && !_subscribedToSectorArrival)
        {
            SectorPersistence.instance.onArrivedAtSector.onTriggered +=
                new System.Action<Il2CppSystem.Object, Il2CppSystem.EventArgs>(OnArrivedAtSector);
            _subscribedToSectorArrival = true;
        }
        
        // TODO: maybe this component not exists?
        if (PlayerState.SpaceSuitMats == null &&  PlayerState.SpaceSuit != null)
            PlayerState.SpaceSuitMats = PlayerState.SpaceSuit.GetComponent<MeshRenderer>()?.materials.ToArray();
        
        // Real overlay hotkeys: F2 toggles UI mode and Esc closes it.
        // Font assets are loaded by the game after the plugin starts, so the fallback is added
        // once they appear instead of only at start up.
        if (!Patches.FontFallback.Installed && Time.unscaledTime >= _nextFontFallbackCheck)
        {
            _nextFontFallbackCheck = Time.unscaledTime + 3f;
            Patches.FontFallback.TryInstall();
        }

        // F2 opens the panel only; Escape is the key that closes it again.
        if (Input.GetKeyDown(KeyCode.F2) && !OverlayManager.IsInteractiveMode)
        {
            OverlayManager.SetInteractiveMode(true);
            App.Log.LogInfo("[Overlay] F2 => open panel");
            ReportOverlayMode();
        }

        if (OverlayManager.IsInteractiveMode && Input.GetKeyDown(KeyCode.Escape))
        {
            App.Log.LogInfo("[Overlay] Esc => interactive mode OFF (click-through ON)");
            OverlayManager.SetInteractiveMode(false);
            ReportOverlayMode();
        }

        if (Input.GetKeyDown(KeyCode.F3))
        {
            // Raycast
            if (Camera.main)
            {
                var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out var hit))
                {
                    App.Log.LogInfo("[Object Inspector] object hit!");
                    // we hit something
                    var objectData = new ObjectData();
                    
                    var go = hit.transform.gameObject;
                    objectData.Name = go.name;
                    objectData.Type = $"{go.GetType().FullName}";
                    objectData.Go = go;
                    AddChildComponents(objectData, go);
                    
                    OverlayManager.PostMessage("inspectObject", objectData);

                    _inspectedObject = objectData;

                    void AddChildComponents(ObjectData data, GameObject obj, int depth = 0)
                    {
                        if (depth > 3) return;

                        // components of this obj
                        foreach (var component in obj.GetComponents<Component>())
                        {
                            var compData = new ObjectData()
                            {
                                Name = component.name,
                                Type = component.GetIl2CppType().FullName,
                                Go = component.gameObject
                            };
                            data.Children.Add(compData);
                        }

                        // childs of this obj
                        for (int i = 0; i < obj.transform.childCount; i++)
                        {
                            var child = obj.transform.GetChild(i).gameObject;
                            var childData = new ObjectData()
                            {
                                Name = child.name,
                                Type = child.GetIl2CppType().FullName,
                                Go = child
                            };
                            AddChildComponents(childData, child, depth + 1);
                            data.Children.Add(childData);
                        }
                    }
                }
            }
        }

        if (Input.GetKeyDown(KeyCode.F8))
        {
            App.Log.LogInfo("[Overlay] running diagnostics page");
            OverlayManager.RunDiagnostics();
        }
    }

    private class ObjectData
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "n/a";
        public string Type { get; set; } = "unk";
        public List<ObjectData> Children { get; set; } = [];
        
        [NotMapped]
        [JsonIgnore]
        internal GameObject Go { get; set; } 
    }

    private class ObjectExtraData
    {
        public string K { get; set; }
        public string V { get; set; }
    }

    private ObjectData _inspectedObject;

    private void GetMoreData(string id)
    {
        App.Log.LogInfo($"[Object Inspector] GetMoreData for object with id {id}");
        if (_inspectedObject == null) return;

        // search in all tree an object with that id
        var selected = FindInside(_inspectedObject, id);
        if (selected == null)
        {
            App.Log.LogInfo($"[Object Inspector] object with id {id} not found in the inspected tree");
            return;
        }
        if (selected.Go == null)
        {
            App.Log.LogInfo($"[Object Inspector] object with id {id} has no GameObject reference");
            return;
        }

        var data = new List<ObjectExtraData>();
        
        switch (selected.Type)
        {
            case "Transform":
            {
                data.Add(new ObjectExtraData { K = "position", V = selected.Go.transform.localPosition.ToString() });
                data.Add(new ObjectExtraData { K = "rotation", V = selected.Go.transform.localEulerAngles.ToString() });
                data.Add(new ObjectExtraData { K = "scale", V = selected.Go.transform.localScale.ToString() });
                break;
            }
            case "Rigidbody":
            {
                var rigidbody = selected.Go.GetComponent<Rigidbody>();
                if (rigidbody == null) break;
                data.Add(new ObjectExtraData { K = "mass", V = rigidbody.mass.ToString(CultureInfo.InvariantCulture) });
                data.Add(new ObjectExtraData { K = "drag", V = rigidbody.drag.ToString(CultureInfo.InvariantCulture) });
                data.Add(new ObjectExtraData { K = "angularDrag", V = rigidbody.angularDrag.ToString(CultureInfo.InvariantCulture) });
                data.Add(new ObjectExtraData { K = "useGravity", V = rigidbody.useGravity.ToString() });
                data.Add(new ObjectExtraData { K = "isKinematic", V = rigidbody.isKinematic.ToString() });
                break;
            }
            case "Collider":
            {
                var collider = selected.Go.GetComponent<Collider>();
                if (collider == null) break;
                data.Add(new ObjectExtraData { K = "enabled", V = collider.enabled.ToString() });
                data.Add(new ObjectExtraData { K = "isTrigger", V = collider.isTrigger.ToString() });
                data.Add(new ObjectExtraData { K = "material", V = collider.material?.name ?? "null" });
                break;
            }
            case "MeshRenderer":
            {
                var mr = selected.Go.GetComponent<MeshRenderer>();
                if (mr == null) break;
                data.Add(new ObjectExtraData { K = "enabled", V = mr.enabled.ToString() });
                data.Add(new ObjectExtraData { K = "castShadows", V = mr.shadowCastingMode.ToString()});
                data.Add(new ObjectExtraData { K = "material", V = mr.material?.name ?? "null"});
                break;
            }
        }
        
        OverlayManager.PostMessage("inspectObjectExtra", data);
        App.Log.LogInfo($"[Object Inspector] sent extra data for object with id {id}");
        App.Log.LogInfo(data);
        
        return;

        ObjectData FindInside(ObjectData oData, string innerId)
        {
            if (oData.Id == innerId) return oData;
            
            foreach (var objectData in oData.Children)
            {
                if (objectData.Id == innerId)
                    return objectData;
                if (FindInside(objectData, innerId) is {} found)
                    return found;
            }

            return null;
        }
    }
    
    private CancellationTokenSource _cts = new();
    private const float ThresholdChange = 0.1f;
    
    #region Player Location updates
    
    private bool _pptRunning = false;

    /// <summary>
    /// Unity isn't thread-safe, so we will update the position outside the main thread
    /// to avoid stuck the Update thread
    /// </summary>
    /// <returns></returns>
    private void PlayerPositionThread()
    {
        if (_pptRunning) return;
        
        var ct = _cts.Token;
        Plugin.StartAttachedThread(() =>
        {
            _pptRunning = true;
            
            App.Log.LogInfo("PlayerPositionThread started");
            
            var lastPosition = Vector3.zero;
            while (!ct.IsCancellationRequested)
            {
                if (Network.NetId == -1)
                {
                    ct.WaitHandle.WaitOne(15);
                    continue;
                }
                
                var player = _player;
                var rigid = _playerRigid;
                if (player == null || rigid == null)
                {
                    ct.WaitHandle.WaitOne(250);
                    continue;
                }

                try
                {
                    player.transform.GetPositionAndRotation(out var position, out var rotation);
                    if (Vector3.Distance(lastPosition, position) > ThresholdChange)
                    {
                        Network.SendServerMessage(new UpdatePositionCmd
                        {
                            Position = ConvertToSharedVector3(position),
                            Rotation = ConvertToSharedQuaternion(rotation),
                            Velocity = ConvertToSharedVector3(rigid.velocity),
                            AngVel = ConvertToSharedVector3(rigid.angularVelocity),
                            IsTruck = false,
                            InSeat = false
                        }, PacketType.UpdatePosition);
                    }
                }
                catch (Exception exception)
                {
                    // Touching a destroyed il2cpp object from this thread used to take the whole
                    // game down; the objects are resolved again on the next sector arrival.
                    App.Log.LogWarning("[Player] Position update skipped: " + exception.Message);
                    ct.WaitHandle.WaitOne(250);
                    continue;
                }

                ct.WaitHandle.WaitOne(15);
            }
            
            _pptRunning = false;
        });
    }
    
    #endregion

    #region Truck Location updates
    
    private bool _tptRunning = false;
    
    private void TruckPositionThread()
    {
        if (_tptRunning) return;
        
        var ct = _cts.Token;
        Plugin.StartAttachedThread(() =>
        {
            _tptRunning = true;
            
            App.Log.LogInfo("TruckPositionThread started");
            
            var lastPosition = Vector3.zero;
            while (!ct.IsCancellationRequested)
            {
                if (Network.NetId == -1)
                {
                    ct.WaitHandle.WaitOne(15);
                    continue;
                }
                
                var truck = _truck;
                var rigid = _truckRigid;
                if (truck == null || rigid == null)
                {
                    ct.WaitHandle.WaitOne(250);
                    continue;
                }

                try
                {
                    truck.transform.GetPositionAndRotation(out var position, out var rotation);
                    if (Vector3.Distance(lastPosition, position) > ThresholdChange)
                    {
                        Network.SendServerMessage(new UpdatePositionCmd
                        {
                            Position = ConvertToSharedVector3(position),
                            Rotation = ConvertToSharedQuaternion(rotation),
                            Velocity = ConvertToSharedVector3(rigid.velocity),
                            AngVel = ConvertToSharedVector3(rigid.angularVelocity),
                            IsTruck = true,
                            InSeat = false
                        }, PacketType.UpdatePosition);
                    }
                }
                catch (Exception exception)
                {
                    App.Log.LogWarning("[Truck] Position update skipped: " + exception.Message);
                    ct.WaitHandle.WaitOne(250);
                    continue;
                }

                ct.WaitHandle.WaitOne(15);
            }
            
            _tptRunning = false;
        });
    }

    #endregion
    
    private StarTruckerMP.Shared.Vector3 ConvertToSharedVector3(Vector3 unityVector)
    {
        return new StarTruckerMP.Shared.Vector3
        {
            X = unityVector.x,
            Y = unityVector.y,
            Z = unityVector.z
        };
    }

    private StarTruckerMP.Shared.Quaternion ConvertToSharedQuaternion(Quaternion unityQuaternion)
    {
        return new Shared.Quaternion()
        {
            X = unityQuaternion.x,
            Y = unityQuaternion.y,
            Z = unityQuaternion.z,
            W = unityQuaternion.w
        };
    }

    #region Recycle
    
    private void OnDestroy()
    {
        _cts.Cancel();
    }

    private void OnDisable()
    {
        if (!_cts.IsCancellationRequested)
            _cts.Cancel();
    }
    
    #endregion
}