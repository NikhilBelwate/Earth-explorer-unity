using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine.XR.Management;

public static class EarthAndroidBuild
{
    const string ScenePath = "Assets/EarthExplorer/AREarth.unity";
    const string ApkPath = "Builds/EarthExplorer.apk";
    const string XRSettingsPath = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";
    const string ARCoreLoader = "UnityEngine.XR.ARCore.ARCoreLoader";

    [MenuItem("Tools/Earth Explorer/Configure Android")]
    public static void Configure()
    {
        ConfigurePlayer();
        EnableARCore();
        ConfigureScene();
        AssetDatabase.SaveAssets();
        Debug.Log("Earth Explorer: Android settings configured.");
    }

    [MenuItem("Tools/Earth Explorer/Build Android APK")]
    public static void BuildApk()
    {
        bool succeeded = Build();
        if (!Application.isBatchMode && succeeded) EditorUtility.RevealInFinder(ApkPath);
    }

    // Command line: Unity.exe -batchmode -quit -projectPath <path> -buildTarget Android -executeMethod EarthAndroidBuild.BuildApkBatch
    public static void BuildApkBatch()
    {
        EditorApplication.Exit(Build() ? 0 : 1);
    }

    private static bool Build()
    {
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
        {
            Debug.LogError("Earth Explorer: Android Build Support is not installed for this Unity version.");
            return false;
        }
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android &&
            !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
        {
            Debug.LogError("Earth Explorer: could not switch the active build target to Android.");
            return false;
        }
        Configure();
        EditorUserBuildSettings.buildAppBundle = false;
        Directory.CreateDirectory(Path.GetDirectoryName(ApkPath));
        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = ApkPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.None
        };
        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;
        if (summary.result != BuildResult.Succeeded)
        {
            Debug.LogError("Earth Explorer: Android build " + summary.result + " with " + summary.totalErrors + " error(s).");
            return false;
        }
        Debug.Log("Earth Explorer: built " + Path.GetFullPath(ApkPath) + " (" + (summary.totalSize / (1024f * 1024f)).ToString("F1") + " MB).");
        return true;
    }

    private static void ConfigurePlayer()
    {
        PlayerSettings.productName = "Earth Explorer";
        PlayerSettings.companyName = "Nikhil";
        PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.nikhil.earthexplorer");
        PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
        // ARCore in this project is set up for OpenGLES3 (see README).
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
    }

    private static void EnableARCore()
    {
        if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.settingsKey, out XRGeneralSettingsPerBuildTarget perTarget) || perTarget == null)
        {
            perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            Directory.CreateDirectory(Path.GetDirectoryName(XRSettingsPath));
            AssetDatabase.CreateAsset(perTarget, XRSettingsPath);
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.settingsKey, perTarget, true);
        }
        if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android)) perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
        XRGeneralSettings android = perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
        android.InitManagerOnStart = true;
        if (!XRPackageMetadataStore.IsLoaderAssigned(ARCoreLoader, BuildTargetGroup.Android) &&
            !XRPackageMetadataStore.AssignLoader(android.Manager, ARCoreLoader, BuildTargetGroup.Android))
            throw new System.InvalidOperationException("Could not enable the ARCore loader for Android.");
        EditorUtility.SetDirty(android); EditorUtility.SetDirty(android.Manager); EditorUtility.SetDirty(perTarget);
    }

    // The AR camera was built with headset (<XRHMD>) bindings; phones report pose through HandheldARInputDevice.
    private static void ConfigureScene()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) throw new System.OperationCanceledException("Build cancelled.");
            scene = EditorSceneManager.OpenScene(ScenePath);
        }
        bool changed = false;
        foreach (var driver in Object.FindObjectsByType<UnityEngine.InputSystem.XR.TrackedPoseDriver>(FindObjectsSortMode.None))
        {
            changed |= AddBinding(driver.positionInput, "<HandheldARInputDevice>/devicePosition");
            changed |= AddBinding(driver.rotationInput, "<HandheldARInputDevice>/deviceRotation");
            if (changed) EditorUtility.SetDirty(driver);
        }
        if (changed) EditorSceneManager.SaveScene(scene);
    }

    private static bool AddBinding(UnityEngine.InputSystem.InputActionProperty property, string path)
    {
        var action = property.action;
        if (action == null || property.reference != null) return false;
        foreach (var binding in action.bindings) if (binding.path == path) return false;
        action.AddBinding(path);
        return true;
    }
}
