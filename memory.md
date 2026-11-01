# Earth Explorer — Project Memory

Reference notes for working on this Unity project. Keep this file up to date when the architecture changes.

## Layout
- Unity project root: `My project/` (the sibling `Earth Explorer/` folder only holds `Logs`/`Temp`, no project).
- Editor: **Unity 6000.6.7f1**, URP 17.6, Input System 1.20.1 (`activeInputHandler: 1` = new Input System only),
  AR Foundation / ARCore 6.6.2.
- Main scene: `Assets/EarthExplorer/AREarth.unity` (note: Build Settings still list only `Assets/Scenes/SampleScene.unity`).

| File | Role |
|------|------|
| `Assets/EarthExplorer/AREarthCockpitController.cs` | The only runtime script. Input, orbit camera, zoom, city pins, HUD. |
| `Assets/EarthExplorer/Editor/EarthSceneBuilder.cs` | Menu `Tools/Earth Explorer/Create AR Earth Scene`. Builds the scene once (returns early if the scene already exists, so builder changes do NOT affect the existing scene). |
| `Assets/EarthExplorer/EarthDayNight.shader`, `Earth.mat` | Day/night Earth shader + material (`_DayMap`, `_NightMap`). |
| `Assets/EarthExplorer/Textures/` | 2k Earth day/night maps (Solar System Scope, CC BY 4.0). |
| `Assets/EarthExplorer/README.txt` | User-facing notes (written by the builder). |

## Scene structure (AREarth.unity)
- **AR Session**, **XR Origin (Mobile AR)** → Camera Offset → **AR Camera** (base URP camera, `MainCamera`,
  culls OUT the `EarthCockpit` layer). ARPlaneManager + ARRaycastManager on the origin.
- **Cockpit Orbit Camera**: URP *Overlay* camera stacked on the AR camera, FOV 40, renders ONLY the `EarthCockpit` layer.
  This is a virtual camera — the controller moves it freely; the tracked AR camera is never written to.
- **EarthSphere**: Unity sphere primitive (diameter = localScale, mesh radius 0.5), layer `EarthCockpit`.
- **EventSystem** (InputSystemUIInputModule), **Earth HUD** (Screen Space Overlay canvas, reference 1080x1920):
  `Instructions` (status text), `City Details` panel with `Details` text and `Close` button.
- **Earth Cockpit Manager**: holds `AREarthCockpitController` with all references wired.

## Controller behaviour (AREarthCockpitController)
- Desktop (non Android/iOS): globe is placed immediately. Mobile: tap an AR plane to place.
- Orbit: mouse drag / one-finger drag → `_targetOrbit` (yaw, pitch clamped by `_pitchLimit`); optional device tilt on mobile.
  Camera = `earth.position + Euler(pitch, yaw) * back * distance`, then `LookAt(earth)`; all smoothed with SmoothDamp.
- City pins: world-space canvases parented to the sphere at lat/lon (`LatitudeLongitudeToDirection`), hidden on the far side,
  billboarded to the cockpit camera. Click → `ShowLocation` fills the details panel.
- `OverUI(position)` blocks drags/zoom that start over UI (pins, buttons, panel).
- Inputs are created in code (`new InputAction(...)`) in `Awake`, enabled/disabled in `OnEnable/OnDisable`, disposed in `OnDestroy`.
- Touch uses EnhancedTouch (`Touch.onFingerDown/Move/Up`).

## Zoom (added 2026-11-01)
- Zoom = cockpit camera **distance** to the Earth centre (`_targetDistance`, smoothed into `_distance`).
- Limits are in Earth radii so they follow the sphere scale: `_closestZoomRadii` (near surface) … `_farthestZoomRadii`.
  Start distance = `_distanceToEarth`.
- Inputs: mouse scroll, pinch (2 fingers), keyboard `+`/`=`/numpad `+` and `-`/numpad `-`, on-screen `+` / `−` buttons
  (created at runtime on the HUD canvas by `BuildZoomButtons`, so no scene rebuild is needed).
- Public API: `ZoomIn()`, `ZoomOut()`, `ZoomByRatio(ratio)` (ratio > 1 zooms in), `ZoomLevel` (0 = farthest, 1 = closest).
- Orbit speed is scaled down when close to the surface so dragging stays controllable.

## Android APK build (added 2026-11-01)
- Requires Unity Hub module **Android Build Support** (+ OpenJDK, Android SDK & NDK) for 6000.6.7f1 — installed.
- `Assets/EarthExplorer/Editor/EarthAndroidBuild.cs`:
  - `Tools/Earth Explorer/Configure Android` — product "Earth Explorer", id `com.nikhil.earthexplorer`, IL2CPP, ARM64,
    min API 26, OpenGLES3, Build Settings scene list = `AREarth.unity` only, creates
    `Assets/XR/XRGeneralSettingsPerBuildTarget.asset` and enables the **ARCore loader for Android**, adds
    `<HandheldARInputDevice>/devicePosition|deviceRotation` bindings to the AR camera TrackedPoseDriver (scene had only `<XRHMD>`).
  - `Tools/Earth Explorer/Build Android APK` — configure + build `Builds/EarthExplorer.apk` (debug-signed).
  - Command line (editor must be closed for this project):
    `Unity.exe -batchmode -quit -projectPath "<My project>" -buildTarget Android -executeMethod EarthAndroidBuild.BuildApkBatch -logFile Logs/android-build.log`
- Release/Play Store needs a keystore (Player Settings → Publishing Settings); not configured.

## WebGL demo + GitHub Pages (added 2026-11-01)
- `Assets/EarthExplorer/Editor/EarthWebBuild.cs`: `Tools/Earth Explorer/Build WebGL` (batch: `EarthWebBuild.BuildWebBatch`,
  `-buildTarget WebGL`). Output `docs/` (committed). Gzip + **decompression fallback** (GitHub Pages can't send
  Content-Encoding headers), template `PROJECT:EarthExplorer`, writes `docs/.nojekyll`. Only cleans Build/, TemplateData/,
  StreamingAssets/ and index.html inside docs/.
- Index page template: `Assets/WebGLTemplates/EarthExplorer/` (`index.html` with Unity `{{{ }}}`/`#if` tags,
  `TemplateData/style.css`): header + full-screen button, loading bar, error overlay, controls help (mouse vs touch), credit.
- On WebGL the controller behaves like desktop (`_mobileAR` false → globe placed immediately, AR disabled).
  `Application.isMobilePlatform` picks the "Pinch to zoom" status text in phone browsers.
- `.github/workflows/pages.yml`: on push to `main` touching `docs/**` (or manual run) → upload `docs/` → deploy-pages.
  Repo setting needed once: Settings → Pages → Source = GitHub Actions.
- Git: repo root is `My project/`, remote `origin` = github.com/NikhilBelwate/Earth-explorer-2026-10-31_17-38-33 (`main`).
  Demo URL: https://nikhilbelwate.github.io/Earth-explorer-2026-10-31_17-38-33/

## Gotchas
- Batch builds launched from this agent's sandboxed shell die silently (Unity can't write `%LOCALAPPDATA%\Unity\Caches`);
  run them unsandboxed or from the editor menu.
- Only one Unity instance can open the project at a time (Android and WebGL builds must run one after another).
- **Do not re-add `com.unity.ai.inference`** (removed 2026-11-01): it depends on App UI (`com.unity.dt.app-ui`), whose
  prebuilt `libAppUITextMateLib.a` was compiled with an older Emscripten (`_emval_take_value`, `_emval_as`) and breaks the
  WebGL link in Unity 6000.6. `-lembind` doesn't help and excluding the .a fails because App UI's C# still calls it.
- The Unity batch exit code is what matters; a PowerShell `Start-Process -Wait` wrapper exits 0 unless it `exit $p.ExitCode`.
- Last verified WebGL build: `docs/` ≈ 19 MB (wasm 12.8 MB, data 6.3 MB, gzip); globe, zoom buttons and city pins tested in browser.
- `InputAction.AddBinding` is an extension method — needs `using UnityEngine.InputSystem;`.
- Type-check without Unity: `dotnet build Assembly-CSharp.csproj` (new files only appear in the csproj after Unity regenerates it).
- **Scroll values**: Input System 1.20 defaults to `ScrollDeltaBehavior.UniformAcrossAllPlatforms` → scroll is ±1 per notch,
  not ±120. The old `exp(y * 0.001)` scaling therefore did ~nothing (this was why "zoom" did not work). Clamp to [-1, 1]
  so both modes behave the same.
- Sphere primitive radius = `localScale * 0.5`. Keep the camera outside: min distance > radius + nearClipPlane.
- The scene builder does not rebuild an existing scene; anything the scene must have at runtime should be created/wired in
  the controller or the scene edited directly.
- The project folder is not a git repository.
