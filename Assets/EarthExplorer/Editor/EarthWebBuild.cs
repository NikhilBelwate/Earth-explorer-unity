using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.Build.Reporting;

public static class EarthWebBuild
{
    const string ScenePath = "Assets/EarthExplorer/AREarth.unity";
    // Committed to git and published to GitHub Pages by .github/workflows/pages.yml.
    const string OutputPath = "docs";
    const string Template = "PROJECT:EarthExplorer";

    [MenuItem("Tools/Earth Explorer/Build WebGL")]
    public static void BuildWeb()
    {
        bool succeeded = Build();
        if (!Application.isBatchMode && succeeded) EditorUtility.RevealInFinder(Path.Combine(OutputPath, "index.html"));
    }

    // Command line: Unity.exe -batchmode -quit -projectPath <path> -buildTarget WebGL -executeMethod EarthWebBuild.BuildWebBatch
    public static void BuildWebBatch()
    {
        EditorApplication.Exit(Build() ? 0 : 1);
    }

    private static bool Build()
    {
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
        {
            Debug.LogError("Earth Explorer: WebGL Build Support is not installed for this Unity version.");
            return false;
        }
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL &&
            !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL))
        {
            Debug.LogError("Earth Explorer: could not switch the active build target to WebGL.");
            return false;
        }
        Configure();
        CleanOutput();
        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = OutputPath,
            target = BuildTarget.WebGL,
            targetGroup = BuildTargetGroup.WebGL,
            options = BuildOptions.None
        };
        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;
        if (summary.result != BuildResult.Succeeded)
        {
            Debug.LogError("Earth Explorer: WebGL build " + summary.result + " with " + summary.totalErrors + " error(s).");
            return false;
        }
        File.WriteAllText(Path.Combine(OutputPath, ".nojekyll"), "");
        Debug.Log("Earth Explorer: built " + Path.GetFullPath(OutputPath) + " (" + (summary.totalSize / (1024f * 1024f)).ToString("F1") + " MB).");
        return true;
    }

    private static void Configure()
    {
        PlayerSettings.productName = "Earth Explorer";
        PlayerSettings.companyName = "Nikhil";
        PlayerSettings.WebGL.template = Template;
        // GitHub Pages cannot send Content-Encoding headers for Unity's pre-compressed files,
        // so the loader decompresses them in JavaScript instead.
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.WebGL.nameFilesAsHashes = false;
        PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
        PlayerSettings.runInBackground = false;
    }

    // Remove only what a previous Unity WebGL build wrote, never anything else in docs/.
    private static void CleanOutput()
    {
        Directory.CreateDirectory(OutputPath);
        foreach (string folder in new[] { "Build", "TemplateData", "StreamingAssets" })
        {
            string path = Path.Combine(OutputPath, folder);
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        string index = Path.Combine(OutputPath, "index.html");
        if (File.Exists(index)) File.Delete(index);
    }
}
