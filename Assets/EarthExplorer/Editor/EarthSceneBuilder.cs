using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class EarthSceneBuilder
{
    public static async void DownloadMaps()
    {
        Directory.CreateDirectory("Assets/EarthExplorer/Textures");
        using (var client = new System.Net.Http.HttpClient())
        {
            client.Timeout = System.TimeSpan.FromSeconds(45);
            foreach (string map in new[] { "daymap", "nightmap" })
            {
                try
                {
                    byte[] bytes = await client.GetByteArrayAsync("https://www.solarsystemscope.com/textures/download/2k_earth_" + map + ".jpg");
                    File.WriteAllBytes("Assets/EarthExplorer/Textures/Earth_" + map + ".jpg", bytes);
                    Debug.Log("Downloaded Earth " + map);
                }
                catch (System.Exception e) { Debug.LogError("Earth map download failed: " + e.Message); }
            }
        }
        AssetDatabase.Refresh();
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/EarthExplorer/Earth.mat");
        if (material != null)
        {
            material.SetTexture("_DayMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/EarthExplorer/Textures/Earth_daymap.jpg"));
            material.SetTexture("_NightMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/EarthExplorer/Textures/Earth_nightmap.jpg"));
            EditorUtility.SetDirty(material); AssetDatabase.SaveAssets();
        }
    }

    [MenuItem("Tools/Earth Explorer/Create AR Earth Scene")]
    public static void Build()
    {
        const string scenePath = "Assets/EarthExplorer/AREarth.unity";
        if (File.Exists(scenePath)) { EditorSceneManager.OpenScene(scenePath); return; }
        Directory.CreateDirectory("Assets/EarthExplorer");
        foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            var importer = assembly.GetType("TMPro.TMP_PackageResourceImporter");
            var method = importer?.GetMethod("ImportResources", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (method != null)
            {
                var parameters = method.GetParameters();
                if (parameters.Length == 3) method.Invoke(null, new object[] { true, false, false });
                else if (parameters.Length == 2) method.Invoke(null, new object[] { true, false });
                break;
            }
        }
        AssetDatabase.Refresh();
        TMPro.TMP_FontAsset font = null;
        foreach (string guid in AssetDatabase.FindAssets("t:TMP_FontAsset"))
        {
            var candidate = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
            if (candidate != null && candidate.name.Contains("LiberationSans")) { font = candidate; break; }
        }
        if (font == null)
        {
            var source = Font.CreateDynamicFontFromOSFont("Arial", 90);
            font = TMPro.TMP_FontAsset.CreateFontAsset(source);
            AssetDatabase.CreateAsset(font, "Assets/EarthExplorer/EarthFont.asset");
            AssetDatabase.AddObjectToAsset(font.material, font);
            foreach (var atlas in font.atlasTextures) AssetDatabase.AddObjectToAsset(atlas, font);
        }
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        int globeLayer = 30;
        var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tags.FindProperty("layers");
        while (globeLayer > 7 && !string.IsNullOrEmpty(layers.GetArrayElementAtIndex(globeLayer).stringValue)) globeLayer--;
        if (globeLayer <= 7) throw new System.InvalidOperationException("No free layer for isolated cockpit rendering.");
        layers.GetArrayElementAtIndex(globeLayer).stringValue = "EarthCockpit"; tags.ApplyModifiedProperties();

        var sessionObject = new GameObject("AR Session");
        var session = sessionObject.AddComponent<UnityEngine.XR.ARFoundation.ARSession>();
        session.enabled = false;
        var originObject = new GameObject("XR Origin (Mobile AR)");
        var origin = originObject.AddComponent<Unity.XR.CoreUtils.XROrigin>();
        var offset = new GameObject("Camera Offset"); offset.transform.SetParent(originObject.transform, false);
        origin.CameraFloorOffsetObject = offset;
        var arObject = new GameObject("AR Camera", typeof(Camera), typeof(AudioListener)); arObject.tag = "MainCamera";
        arObject.transform.SetParent(offset.transform, false);
        var arCamera = arObject.GetComponent<Camera>(); arCamera.nearClipPlane = 0.01f; arCamera.farClipPlane = 100f;
        arCamera.cullingMask = ~(1 << globeLayer); arCamera.clearFlags = CameraClearFlags.SolidColor; arCamera.backgroundColor = new Color(0.006f, 0.012f, 0.028f);
        origin.Camera = arCamera;
        arObject.AddComponent<UnityEngine.XR.ARFoundation.ARCameraManager>();
        arObject.AddComponent<UnityEngine.XR.ARFoundation.ARCameraBackground>();
        var driver = arObject.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
        var positionAction = new UnityEngine.InputSystem.InputAction("Position", UnityEngine.InputSystem.InputActionType.Value, "<XRHMD>/centerEyePosition");
        positionAction.AddBinding("<HandheldARInputDevice>/devicePosition");
        var rotationAction = new UnityEngine.InputSystem.InputAction("Rotation", UnityEngine.InputSystem.InputActionType.Value, "<XRHMD>/centerEyeRotation");
        rotationAction.AddBinding("<HandheldARInputDevice>/deviceRotation");
        driver.positionInput = new UnityEngine.InputSystem.InputActionProperty(positionAction);
        driver.rotationInput = new UnityEngine.InputSystem.InputActionProperty(rotationAction);
        driver.trackingStateInput = new UnityEngine.InputSystem.InputActionProperty(new UnityEngine.InputSystem.InputAction("Tracking", UnityEngine.InputSystem.InputActionType.Value, "<XRHMD>/trackingState"));
        var planes = originObject.AddComponent<UnityEngine.XR.ARFoundation.ARPlaneManager>();
        planes.requestedDetectionMode = UnityEngine.XR.ARSubsystems.PlaneDetectionMode.Horizontal;
        planes.enabled = false;
        var raycasts = originObject.AddComponent<UnityEngine.XR.ARFoundation.ARRaycastManager>(); raycasts.enabled = false;

        var cockpitObject = new GameObject("Cockpit Orbit Camera", typeof(Camera));
        var cockpit = cockpitObject.GetComponent<Camera>(); cockpit.nearClipPlane = 0.01f; cockpit.farClipPlane = 100f; cockpit.fieldOfView = 40f; cockpit.cullingMask = 1 << globeLayer;
        cockpit.transform.position = new Vector3(0f, 0.3f, -1.5f); cockpit.transform.LookAt(Vector3.zero);
        var arData = arObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        var cockpitData = cockpitObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        cockpitData.renderType = UnityEngine.Rendering.Universal.CameraRenderType.Overlay;
        var cockpitSettings = new SerializedObject(cockpitData);
        cockpitSettings.FindProperty("m_ClearDepth").boolValue = true;
        cockpitSettings.ApplyModifiedPropertiesWithoutUndo();
        arData.cameraStack.Add(cockpit);

        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere); sphere.name = "EarthSphere"; sphere.layer = globeLayer; sphere.transform.localScale = Vector3.one * 0.5f;
        var material = new Material(Shader.Find("EarthExplorer/DayNight"));
        material.SetTexture("_DayMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/EarthExplorer/Textures/Earth_daymap.jpg"));
        material.SetTexture("_NightMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/EarthExplorer/Textures/Earth_nightmap.jpg"));
        AssetDatabase.CreateAsset(material, "Assets/EarthExplorer/Earth.mat"); sphere.GetComponent<Renderer>().sharedMaterial = material;
        sphere.GetComponent<SphereCollider>().radius = 0.5f;

        var events = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
        events.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>().AssignDefaultActions();
        var canvasObject = new GameObject("Earth HUD", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>(); scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1080f, 1920f); scaler.matchWidthOrHeight = 0.5f;
        var header = Text("Instructions", canvasObject.transform, font, new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(980f, 110f), 30f);
        header.text = "EARTH EXPLORER\nDrag to orbit - Scroll or +/- to zoom - Click a city";
        var panel = new GameObject("City Details", typeof(RectTransform), typeof(UnityEngine.UI.Image)); panel.transform.SetParent(canvasObject.transform, false);
        var panelRect = (RectTransform)panel.transform; panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0f); panelRect.pivot = new Vector2(0.5f, 0f); panelRect.anchoredPosition = new Vector2(0f, 50f); panelRect.sizeDelta = new Vector2(960f, 400f);
        panel.GetComponent<UnityEngine.UI.Image>().color = new Color(0.015f, 0.05f, 0.09f, 0.97f);
        var details = Text("Details", panel.transform, font, new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(860f, 300f), 30f);
        details.alignment = TMPro.TextAlignmentOptions.TopLeft;
        var close = Text("Close", panel.transform, font, new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(250f, 60f), 26f);
        close.text = "CLOSE"; close.raycastTarget = true; var closeButton = close.gameObject.AddComponent<UnityEngine.UI.Button>(); closeButton.targetGraphic = close;
        var manager = new GameObject("Earth Cockpit Manager").AddComponent<AREarthCockpitController>();
        var so = new SerializedObject(manager);
        so.FindProperty("_earthSphere").objectReferenceValue = sphere.transform;
        so.FindProperty("_arCamera").objectReferenceValue = arCamera;
        so.FindProperty("_cockpitCamera").objectReferenceValue = cockpit;
        so.FindProperty("_arSession").objectReferenceValue = session;
        so.FindProperty("_planeManager").objectReferenceValue = planes;
        so.FindProperty("_raycastManager").objectReferenceValue = raycasts;
        so.FindProperty("_font").objectReferenceValue = font;
        so.FindProperty("_statusText").objectReferenceValue = header;
        so.FindProperty("_detailsText").objectReferenceValue = details;
        so.FindProperty("_detailsPanel").objectReferenceValue = panel;
        so.ApplyModifiedPropertiesWithoutUndo();
        UnityEditor.Events.UnityEventTools.AddPersistentListener(closeButton.onClick, manager.CloseDetails);
        panel.SetActive(false);
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
        PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });
        File.WriteAllText("Assets/EarthExplorer/README.txt", "Earth Explorer\nEarth day/night maps: Solar System Scope, https://www.solarsystemscope.com/textures/ , CC BY 4.0 https://creativecommons.org/licenses/by/4.0/\nDesktop: drag to orbit, scroll / +,- keys / on-screen buttons to zoom, click cities. Mobile: scan horizontal plane, tap to place, drag/tilt to orbit, pinch or on-screen buttons to zoom.\nVirtual cockpit camera is rendered as an overlay: the tracked AR camera is NEVER moved. This is a stylized cockpit view, not a physically registered camera orbit.\nAndroid module is required via Unity Hub; enable ARCore in XR Plug-in Management and run Project Validation. Use OpenGLES3, IL2CPP, ARM64, minimum API 24.\n");
        EditorSceneManager.SaveScene(scene, scenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("Created " + scenePath);
    }

    private static TMPro.TextMeshProUGUI Text(string name, Transform parent, TMPro.TMP_FontAsset font, Vector2 anchor, Vector2 position, Vector2 size, float fontSize)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TMPro.TextMeshProUGUI)); go.transform.SetParent(parent, false);
        var text = go.GetComponent<TMPro.TextMeshProUGUI>(); text.font = font; text.fontSize = fontSize; text.enableAutoSizing = false; text.color = new Color(0.65f, 0.95f, 1f); text.alignment = TMPro.TextAlignmentOptions.Center; text.raycastTarget = false;
        text.rectTransform.anchorMin = text.rectTransform.anchorMax = anchor; text.rectTransform.pivot = new Vector2(0.5f, 0.5f); text.rectTransform.anchoredPosition = position; text.rectTransform.sizeDelta = size;
        return text;
    }
}
