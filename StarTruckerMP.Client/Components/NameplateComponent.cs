using TMPro;
using UnityEngine;

namespace StarTruckerMP.Client.Components;

/// <summary>
/// A world-space label that follows a remote player or their truck. It turns towards the camera
/// and fades out with distance, so a name is readable next to the other player and disappears
/// once they drive away.
/// </summary>
public class NameplateComponent : MonoBehaviour
{
    private Transform _target;
    private TMP_Text _text;
    private Camera _camera;
    private float _fallbackHeight;
    private float _anchorHeight;
    private float _margin;
    private float _nextBoundsRefresh;
    private float _fullDistance;
    private float _hiddenDistance;
    private float _worldHeight = 1f;

    private bool _scaled;
    private bool _suspended;
    private bool _visible = true;

    public void Configure(Transform target, float fallbackHeight, float margin, float fullDistance, float hiddenDistance, float worldHeight)
    {
        _target = target;
        _fallbackHeight = fallbackHeight;
        _anchorHeight = fallbackHeight;
        _margin = margin;
        _fullDistance = fullDistance;
        _hiddenDistance = hiddenDistance;
        _worldHeight = worldHeight;
        _nextBoundsRefresh = 0f;
        _text = GetComponent<TMP_Text>();
        _scaled = false;
    }

    /// <summary>Points the label at another object, used when a remote truck is rebuilt.</summary>
    public void SetTarget(Transform target)
    {
        _target = target;
        _nextBoundsRefresh = 0f;
    }

    /// <summary>
    /// Measures the rendered model and returns how far above its origin the top of the object sits,
    /// so the label clears the cab instead of ending up inside the truck.
    /// </summary>
    private void RefreshAnchorHeight()
    {
        // The model does not change while it drives, so measuring a couple of times per second is
        // enough and keeps the per-frame cost of the label tiny.
        _nextBoundsRefresh = Time.time + 2f;

        if (_target == null)
            return;

        var top = float.MinValue;

        foreach (var renderer in _target.GetComponentsInChildren<Renderer>(true))
        {
            if (!Synchronization.TruckFactory.IsBodyMesh(renderer))
                continue;

            var rendererTop = renderer.bounds.max.y;
            if (rendererTop > top)
                top = rendererTop;
        }

        if (top == float.MinValue)
        {
            _anchorHeight = _fallbackHeight;
            return;
        }

        _anchorHeight = Mathf.Max(top - _target.position.y + _margin, _fallbackHeight);
    }

    /// <summary>Hides the label without removing it, for states where another label already shows the name.</summary>
    public void SetSuspended(bool suspended)
    {
        _suspended = suspended;
    }

    private void LateUpdate()
    {
        if (_target == null)
        {
            // The truck or suit this label belonged to was destroyed.
            Destroy(gameObject);
            return;
        }

        if (_camera == null)
            _camera = Camera.main;

        if (Time.time >= _nextBoundsRefresh)
            RefreshAnchorHeight();

        transform.position = _target.position + Vector3.up * _anchorHeight;

        if (_camera == null)
        {
            ApplyVisibility(1f);
            return;
        }

        var toCamera = transform.position - _camera.transform.position;
        var distance = toCamera.magnitude;

        // The text is readable from the front only, so it is turned to look away from the camera.
        if (distance > 0.1f)
            transform.rotation = Quaternion.LookRotation(toCamera, Vector3.up);

        var visibility = Mathf.InverseLerp(_hiddenDistance, _fullDistance, distance);
        ApplyVisibility(visibility);

        if (!_scaled)
            ApplyScale(distance, visibility);
    }

    /// <summary>
    /// TextMeshPro sizes glyphs in points, so the label is scaled once to a fixed world height and
    /// stays readable regardless of the font size the game's font asset is set up with.
    /// </summary>
    private void ApplyScale(float distance, float visibility)
    {
        if (_text == null)
            return;

        var boundsHeight = _text.textBounds.size.y;
        if (boundsHeight <= 0.0001f)
            return;

        var factor = _worldHeight / boundsHeight;
        if (factor <= 0f || float.IsNaN(factor) || float.IsInfinity(factor))
            return;

        transform.localScale = transform.localScale * factor;
        _scaled = true;

        App.Log.LogInfo($"[Nameplates] '{_text.text}' is {_worldHeight:0.##} units tall, " +
                        $"{_anchorHeight:0.#} units above its target, {distance:0.#} units from the camera, " +
                        $"visibility {visibility:0.##}.");
    }

    private void ApplyVisibility(float visibility)
    {
        if (_text == null)
            return;

        var show = !_suspended && visibility > 0.01f;
        _text.alpha = show ? visibility : 0f;

        if (_visible == show)
            return;

        _visible = show;
        _text.enabled = show;
    }
}
