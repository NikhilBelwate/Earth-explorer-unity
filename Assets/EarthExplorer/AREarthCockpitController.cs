using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

public sealed class AREarthCockpitController : MonoBehaviour
{
    [System.Serializable]
    public sealed class Location
    {
        public string name;
        public float latitude;
        public float longitude;
        [TextArea] public string details;
    }

    [Header("References")]
    [SerializeField] private Transform _earthSphere;
    [SerializeField] private Camera _arCamera;
    [SerializeField] private Camera _cockpitCamera;
    [SerializeField] private UnityEngine.XR.ARFoundation.ARSession _arSession;
    [SerializeField] private UnityEngine.XR.ARFoundation.ARRaycastManager _raycastManager;
    [SerializeField] private UnityEngine.XR.ARFoundation.ARPlaneManager _planeManager;
    [SerializeField] private TMPro.TMP_FontAsset _font;
    [SerializeField] private TMPro.TextMeshProUGUI _statusText;
    [SerializeField] private TMPro.TextMeshProUGUI _detailsText;
    [SerializeField] private GameObject _detailsPanel;

    [Header("Cockpit Orbit")]
    [SerializeField] private float _orbitSpeed = 0.18f;
    [Tooltip("Starting camera distance from the Earth centre (meters).")]
    [SerializeField] private float _distanceToEarth = 1.5f;
    [SerializeField] private float _smoothTime = 0.14f;
    [SerializeField] private float _pitchLimit = 75f;
    [SerializeField] private bool _useDeviceTilt = true;
    [SerializeField] private float _tiltSensitivity = 0.65f;
    [SerializeField] private float _tiltLimit = 35f;

    [Header("Zoom (camera distance in Earth radii)")]
    [SerializeField] private float _closestZoomRadii = 1.15f;
    [SerializeField] private float _farthestZoomRadii = 12f;
    [Tooltip("Distance ratio applied per zoom button / key press.")]
    [SerializeField] private float _zoomStep = 1.25f;
    [Tooltip("Distance ratio applied per mouse wheel notch.")]
    [SerializeField] private float _scrollZoomStep = 1.12f;
    [SerializeField] private bool _showZoomButtons = true;

    [Header("Scale (sphere diameter in meters)")]
    [SerializeField] private float _minimumScale = 0.2f;
    [SerializeField] private float _maximumScale = 2f;
    [SerializeField] private float _initialScale = 0.5f;
    [SerializeField] private float _pinOffset = 0.025f;
    [SerializeField] private float _longitudeOffset;

    [Header("Geographic locations")]
    [SerializeField] private Location[] _locations =
    {
        new Location { name = "New York", latitude = 40.7128f, longitude = -74.006f, details = "United States\nA global center for culture, finance and the arts." },
        new Location { name = "Tokyo", latitude = 35.6762f, longitude = 139.6503f, details = "Japan\nJapan's capital blends historic temples with modern urban life." },
        new Location { name = "London", latitude = 51.5074f, longitude = -0.1278f, details = "United Kingdom\nHistoric capital on the River Thames." },
        new Location { name = "Mumbai", latitude = 19.076f, longitude = 72.8777f, details = "India\nCoastal metropolis and a major center of Indian cinema and commerce." }
    };

    private readonly List<UnityEngine.XR.ARFoundation.ARRaycastHit> _hits = new List<UnityEngine.XR.ARFoundation.ARRaycastHit>();
    private readonly List<RectTransform> _pins = new List<RectTransform>();
    private readonly HashSet<int> _blockedFingers = new HashSet<int>();
    private readonly List<RaycastResult> _uiHits = new List<RaycastResult>();
    private InputAction _mousePress;
    private InputAction _mouseDelta;
    private InputAction _scroll;
    private InputAction _attitude;
    private InputAction _zoomInKey;
    private InputAction _zoomOutKey;
    private GameObject _zoomControls;
    private bool _mouseDragging;
    private bool _placed;
    private bool _mobileAR;
    private bool _hasTiltReference;
    private Quaternion _tiltReference;
    private Vector2 _tilt;
    private Vector2 _targetOrbit = new Vector2(-90f, 12f);
    private Vector2 _orbit;
    private Vector2 _orbitVelocity;
    private float _targetScale;
    private float _scaleVelocity;
    private float _targetDistance;
    private float _distance;
    private float _distanceVelocity;
    private float _lastPinchDistance;
    private bool _pinching;

    public Transform EarthSphere => _earthSphere;
    public bool IsPlaced => _placed;
    public int PinCount => _pins.Count;
    // 0 = farthest, 1 = closest.
    public float ZoomLevel
    {
        get
        {
            float radius = _targetScale * 0.5f;
            return Mathf.InverseLerp(radius * _farthestZoomRadii, MinimumDistance(radius), _targetDistance);
        }
    }

    private void Awake()
    {
        if (_earthSphere == null || _arCamera == null || _cockpitCamera == null || _font == null)
        {
            Debug.LogError("Earth Explorer requires Earth, AR camera, cockpit camera and font references.", this);
            enabled = false;
            return;
        }
        _mobileAR = Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.IPhonePlayer;
        _targetScale = _initialScale;
        _earthSphere.localScale = Vector3.one * _initialScale;
        _orbit = _targetOrbit;
        _targetDistance = ClampDistance(_distanceToEarth, _initialScale);
        _distance = _targetDistance;
        if (_arSession != null) _arSession.enabled = _mobileAR;
        if (_planeManager != null) _planeManager.enabled = _mobileAR;
        if (_raycastManager != null) _raycastManager.enabled = _mobileAR;
        _placed = !_mobileAR;
        _earthSphere.gameObject.SetActive(_placed);
        if (_detailsPanel != null) _detailsPanel.SetActive(false);
        BuildPins();
        BuildZoomButtons();
        // isMobilePlatform is also true for WebGL running in a phone browser.
        if (_mobileAR) SetStatus("Move device to scan a surface. Tap a plane to place Earth.");
        else if (Application.isMobilePlatform) SetStatus("EARTH EXPLORER  |  Drag to orbit - Pinch to zoom - Tap a city");
        else SetStatus("EARTH EXPLORER  |  Drag to orbit - Scroll or +/- to zoom - Click a city");
        _mousePress = new InputAction("Orbit press", InputActionType.Button, "<Mouse>/leftButton");
        _mouseDelta = new InputAction("Orbit delta", InputActionType.Value, "<Mouse>/delta");
        _scroll = new InputAction("Zoom", InputActionType.Value, "<Mouse>/scroll");
        _attitude = new InputAction("Device tilt", InputActionType.Value, "<AttitudeSensor>/attitude");
        _zoomInKey = new InputAction("Zoom in", InputActionType.Button);
        _zoomInKey.AddBinding("<Keyboard>/equals"); _zoomInKey.AddBinding("<Keyboard>/numpadPlus");
        _zoomOutKey = new InputAction("Zoom out", InputActionType.Button);
        _zoomOutKey.AddBinding("<Keyboard>/minus"); _zoomOutKey.AddBinding("<Keyboard>/numpadMinus");
        _mousePress.performed += OnMouseDown;
        _mousePress.canceled += OnMouseUp;
        _mouseDelta.performed += OnMouseDelta;
        _scroll.performed += OnScroll;
        _attitude.performed += OnAttitude;
        _zoomInKey.performed += OnZoomInKey;
        _zoomOutKey.performed += OnZoomOutKey;
    }

    private void OnEnable()
    {
        if (_mousePress == null) return;
        EnhancedTouchSupport.Enable();
        Touch.onFingerDown += OnFingerDown;
        Touch.onFingerMove += OnFingerMove;
        Touch.onFingerUp += OnFingerUp;
        _mousePress.Enable(); _mouseDelta.Enable(); _scroll.Enable(); _attitude.Enable(); _zoomInKey.Enable(); _zoomOutKey.Enable();
        if (_mobileAR && _useDeviceTilt && AttitudeSensor.current != null) InputSystem.EnableDevice(AttitudeSensor.current);
    }

    private void OnDisable()
    {
        if (_mousePress == null) return;
        Touch.onFingerDown -= OnFingerDown;
        Touch.onFingerMove -= OnFingerMove;
        Touch.onFingerUp -= OnFingerUp;
        EnhancedTouchSupport.Disable();
        _mousePress.Disable(); _mouseDelta.Disable(); _scroll.Disable(); _attitude.Disable(); _zoomInKey.Disable(); _zoomOutKey.Disable();
        _blockedFingers.Clear(); _pinching = false; _mouseDragging = false;
    }

    private void OnDestroy()
    {
        _mousePress?.Dispose(); _mouseDelta?.Dispose(); _scroll?.Dispose(); _attitude?.Dispose(); _zoomInKey?.Dispose(); _zoomOutKey?.Dispose();
    }

    private bool OverUI(Vector2 position)
    {
        if (EventSystem.current == null) return false;
        var pointer = new PointerEventData(EventSystem.current) { position = position };
        _uiHits.Clear(); EventSystem.current.RaycastAll(pointer, _uiHits);
        return _uiHits.Count > 0;
    }

    private void OnMouseDown(InputAction.CallbackContext context)
    {
        if (Mouse.current == null) return;
        Vector2 position = Mouse.current.position.ReadValue();
        _mouseDragging = !OverUI(position);
        if (!_placed && _mouseDragging) TryPlace(position);
    }
    private void OnMouseUp(InputAction.CallbackContext context) { _mouseDragging = false; }
    private void OnMouseDelta(InputAction.CallbackContext context) { if (_mouseDragging && _placed) OrbitBy(context.ReadValue<Vector2>()); }
    private void OnScroll(InputAction.CallbackContext context)
    {
        if (!_placed || Mouse.current == null || OverUI(Mouse.current.position.ReadValue())) return;
        // Input System reports +-1 per notch by default (+-120 with platform-specific scroll); clamp so both behave alike.
        float notches = Mathf.Clamp(context.ReadValue<Vector2>().y, -1f, 1f);
        if (notches != 0f) ZoomByRatio(Mathf.Pow(_scrollZoomStep, notches));
    }
    private void OnZoomInKey(InputAction.CallbackContext context) { if (_placed) ZoomIn(); }
    private void OnZoomOutKey(InputAction.CallbackContext context) { if (_placed) ZoomOut(); }
    private void OnFingerDown(Finger finger)
    {
        if (OverUI(finger.currentTouch.screenPosition)) _blockedFingers.Add(finger.index);
        if (!_placed && !_blockedFingers.Contains(finger.index)) TryPlace(finger.currentTouch.screenPosition);
        _pinching = false;
    }
    private void OnFingerUp(Finger finger) { _blockedFingers.Remove(finger.index); _pinching = false; }
    private void OnFingerMove(Finger finger)
    {
        if (!_placed || _blockedFingers.Contains(finger.index)) return;
        var touches = Touch.activeTouches;
        if (touches.Count == 1) { _pinching = false; OrbitBy(finger.currentTouch.delta); }
        else if (touches.Count == 2 && !_blockedFingers.Contains(touches[0].finger.index) && !_blockedFingers.Contains(touches[1].finger.index))
        {
            float distance = Vector2.Distance(touches[0].screenPosition, touches[1].screenPosition);
            if (_pinching && _lastPinchDistance > 1f) ZoomByRatio(distance / _lastPinchDistance);
            _lastPinchDistance = distance; _pinching = true;
        }
    }
    private void OnAttitude(InputAction.CallbackContext context)
    {
        if (!_mobileAR || !_useDeviceTilt || !_placed) return;
        Quaternion attitude = context.ReadValue<Quaternion>();
        if (!_hasTiltReference) { _tiltReference = attitude; _hasTiltReference = true; }
        Vector3 euler = (Quaternion.Inverse(_tiltReference) * attitude).eulerAngles;
        _tilt = new Vector2(Mathf.Clamp(Mathf.DeltaAngle(0f, euler.y), -_tiltLimit, _tiltLimit), Mathf.Clamp(Mathf.DeltaAngle(0f, euler.x), -_tiltLimit, _tiltLimit)) * _tiltSensitivity;
    }

    public void OrbitBy(Vector2 pixelDelta)
    {
        // Slow the orbit down near the surface so a drag moves the view by a similar amount at every zoom level.
        float radius = _earthSphere.localScale.x * 0.5f;
        float speed = _orbitSpeed * Mathf.Clamp((_distance - radius) / Mathf.Max(0.0001f, _distanceToEarth - radius), 0.15f, 1f);
        _targetOrbit.x += pixelDelta.x * speed;
        _targetOrbit.y = Mathf.Clamp(_targetOrbit.y - pixelDelta.y * speed, -_pitchLimit, _pitchLimit);
    }
    public void ScaleByRatio(float ratio)
    {
        if (ratio <= 0f || float.IsNaN(ratio) || float.IsInfinity(ratio)) return;
        _targetScale = Mathf.Clamp(_targetScale * ratio, _minimumScale, _maximumScale);
    }
    // ratio > 1 moves the camera closer (zoom in), ratio < 1 moves it away (zoom out).
    public void ZoomByRatio(float ratio)
    {
        if (ratio <= 0f || float.IsNaN(ratio) || float.IsInfinity(ratio)) return;
        _targetDistance = ClampDistance(_targetDistance / ratio, _targetScale);
    }
    public void ZoomIn() { ZoomByRatio(_zoomStep); }
    public void ZoomOut() { ZoomByRatio(1f / _zoomStep); }
    private float MinimumDistance(float radius) { return radius * _closestZoomRadii + _cockpitCamera.nearClipPlane; }
    private float ClampDistance(float distance, float scale)
    {
        float radius = scale * 0.5f;
        float minimum = MinimumDistance(radius);
        return Mathf.Clamp(distance, minimum, Mathf.Max(minimum, radius * _farthestZoomRadii));
    }
    public void RecenterTilt() { _hasTiltReference = false; _tilt = Vector2.zero; }

    private void TryPlace(Vector2 screenPosition)
    {
        if (_raycastManager == null || !_raycastManager.Raycast(screenPosition, _hits, UnityEngine.XR.ARSubsystems.TrackableType.PlaneWithinPolygon)) return;
        PlaceAt(_hits[0].pose.position + Vector3.up * (_initialScale * 0.5f + 0.1f));
        // Keep the globe fixed in AR world coordinates. Never write the tracked camera transform.
        if (_planeManager != null)
        {
            foreach (var plane in _planeManager.trackables) plane.gameObject.SetActive(false);
            _planeManager.enabled = false;
        }
    }
    public void PlaceAt(Vector3 position)
    {
        _earthSphere.position = position;
        _placed = true;
        _earthSphere.gameObject.SetActive(true);
        RecenterTilt();
        if (_zoomControls != null) _zoomControls.SetActive(true);
        SetStatus("Drag or tilt to orbit - Pinch to zoom - Tap a city for details");
    }

    private void LateUpdate()
    {
        if (!_placed) return;
        float scale = Mathf.SmoothDamp(_earthSphere.localScale.x, _targetScale, ref _scaleVelocity, _smoothTime);
        _earthSphere.localScale = Vector3.one * scale;
        Vector2 target = _targetOrbit + _tilt;
        target.y = Mathf.Clamp(target.y, -_pitchLimit, _pitchLimit);
        _orbit = Vector2.SmoothDamp(_orbit, target, ref _orbitVelocity, _smoothTime);
        _targetDistance = ClampDistance(_targetDistance, _targetScale);
        _distance = Mathf.SmoothDamp(_distance, _targetDistance, ref _distanceVelocity, _smoothTime);
        float distance = Mathf.Max(_distance, MinimumDistance(scale * 0.5f));
        _cockpitCamera.transform.position = _earthSphere.position + Quaternion.Euler(_orbit.y, _orbit.x, 0f) * Vector3.back * distance;
        _cockpitCamera.transform.LookAt(_earthSphere.position, Vector3.up);
        foreach (RectTransform pin in _pins)
        {
            Vector3 normal = (pin.position - _earthSphere.position).normalized;
            Vector3 toCamera = _cockpitCamera.transform.position - pin.position;
            bool visible = Vector3.Dot(normal, toCamera.normalized) > 0.12f;
            pin.gameObject.SetActive(visible);
            if (visible) pin.rotation = Quaternion.LookRotation(pin.position - _cockpitCamera.transform.position, _cockpitCamera.transform.up);
        }
    }

    // Measured Unity sphere UVs: Greenwich (+X) is U=0.5; 90E (+Z) is U=0.75.
    public static Vector3 LatitudeLongitudeToDirection(float latitude, float longitude)
    {
        float lat = latitude * Mathf.Deg2Rad;
        float lon = longitude * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
    }

    private void BuildPins()
    {
        foreach (Location location in _locations)
        {
            var go = new GameObject(location.name + "Pin", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.GraphicRaycaster));
            go.layer = _earthSphere.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(_earthSphere, false);
            rect.localPosition = LatitudeLongitudeToDirection(location.latitude, location.longitude + _longitudeOffset) * (0.5f + _pinOffset);
            rect.localScale = Vector3.one * 0.0018f;
            rect.sizeDelta = new Vector2(160f, 42f);
            var canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = _cockpitCamera;
            var image = go.AddComponent<UnityEngine.UI.Image>(); image.color = new Color(0.025f, 0.11f, 0.18f, 0.95f);
            var button = go.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image;
            Location selected = location; button.onClick.AddListener(() => ShowLocation(selected));
            var textObject = new GameObject("Label", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
            textObject.layer = go.layer; textObject.transform.SetParent(rect, false);
            var text = textObject.GetComponent<TMPro.TextMeshProUGUI>();
            text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one; text.rectTransform.sizeDelta = Vector2.zero;
            text.font = _font; text.fontSize = 22f; text.enableAutoSizing = false; text.alignment = TMPro.TextAlignmentOptions.Center;
            text.color = new Color(0.6f, 0.95f, 1f); text.text = "\u2022 " + location.name; text.raycastTarget = false;
            _pins.Add(rect);
        }
    }
    // Built at runtime on the HUD canvas so the existing scene needs no rebuild.
    private void BuildZoomButtons()
    {
        Canvas hud = _statusText != null ? _statusText.canvas : null;
        if (!_showZoomButtons || hud == null) return;
        _zoomControls = new GameObject("Zoom Controls", typeof(RectTransform));
        var rect = (RectTransform)_zoomControls.transform;
        rect.SetParent(hud.rootCanvas.transform, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = new Vector2(-40f, 0f); rect.sizeDelta = new Vector2(120f, 270f);
        BuildZoomButton("Zoom In", "+", rect, new Vector2(0f, 70f), ZoomIn);
        BuildZoomButton("Zoom Out", "-", rect, new Vector2(0f, -70f), ZoomOut);
        _zoomControls.SetActive(_placed);
    }
    private void BuildZoomButton(string name, string label, RectTransform parent, Vector2 position, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchoredPosition = position; rect.sizeDelta = new Vector2(120f, 120f);
        var image = go.GetComponent<UnityEngine.UI.Image>(); image.color = new Color(0.025f, 0.11f, 0.18f, 0.95f);
        var button = go.GetComponent<UnityEngine.UI.Button>(); button.targetGraphic = image; button.onClick.AddListener(onClick);
        var textObject = new GameObject("Label", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
        textObject.transform.SetParent(rect, false);
        var text = textObject.GetComponent<TMPro.TextMeshProUGUI>();
        text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one; text.rectTransform.sizeDelta = Vector2.zero;
        text.font = _font; text.fontSize = 72f; text.enableAutoSizing = false; text.alignment = TMPro.TextAlignmentOptions.Center;
        text.color = new Color(0.6f, 0.95f, 1f); text.text = label; text.raycastTarget = false;
    }
    public void ShowLocation(Location location)
    {
        if (_detailsText != null) _detailsText.text = location.name + "\n" + location.latitude.ToString("F4") + "°, " + location.longitude.ToString("F4") + "°\n\n" + location.details;
        if (_detailsPanel != null) _detailsPanel.SetActive(true);
    }
    public void CloseDetails() { if (_detailsPanel != null) _detailsPanel.SetActive(false); }
    private void SetStatus(string message) { if (_statusText != null) _statusText.text = message; }
}
