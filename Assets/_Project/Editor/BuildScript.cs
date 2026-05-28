using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AnimeFighter.Editor
{
    public static class BuildScript
    {
        private const string ScenePath = "Assets/_Project/Scenes/PrototypeArena.unity";
        private const string OutputPath = "Builds/Windows/AnimeFighterPrototype.exe";

        public static void BuildWindows()
        {
            int exitCode = 1;

            try
            {
                Debug.Log("[BuildScript] Starting Windows build.");
                Debug.Log($"[BuildScript] Scene: {ScenePath}");
                Debug.Log($"[BuildScript] Output: {OutputPath}");

                if (!File.Exists(ScenePath))
                    throw new FileNotFoundException("Required build scene was not found.", ScenePath);

                string outputDirectory = Path.GetDirectoryName(OutputPath);
                if (!string.IsNullOrEmpty(outputDirectory))
                    Directory.CreateDirectory(outputDirectory);

                BuildPlayerOptions options = new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = OutputPath,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None
                };

                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;

                if (summary.result == BuildResult.Succeeded)
                {
                    Debug.Log($"[BuildScript] Build succeeded: {summary.totalSize} bytes written to {OutputPath}");
                    exitCode = 0;
                }
                else
                {
                    Debug.LogError($"[BuildScript] Build failed with result {summary.result}. Errors: {summary.totalErrors}, warnings: {summary.totalWarnings}");
                    exitCode = 1;
                }
            }
            catch (Exception exception)
            {
                Debug.LogError("[BuildScript] Build failed with exception:");
                Debug.LogException(exception);
                exitCode = 1;
            }
            finally
            {
                if (Application.isBatchMode)
                    EditorApplication.Exit(exitCode);
            }
        }
    }
}
