using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Doffy.Editor
{
    public static class TeleopBuild
    {
        private const string Scene = "Assets/Scenes/Teleoperation.unity";
        private const string AndroidIdentifier = "org.airolab.doffy.bracelet";
        private const string QuestApk = "Builds/AIRO_Doffy.apk";
        private const string MetaAndroidIdentifier = "com.AIROLab.AIRODOFFY";
        private const string MetaUpdateApk = "Builds/meta-update-v0.9.7/AIRO_Doffy_meta_v0.9.7.apk";
        private const string MetaArm64Apk = "Builds/meta-update-v0.9.7-arm64-code18/AIRO_Doffy_meta_v0.9.7_arm64.apk";
        private const string ProtectedRefactoredApk = "AIRO_Doffy_refactored.apk";
        private const string ProtectedRingSnapshot = "wrist-ring-v1";

        [MenuItem("Tools/DOFFY/Validate scene")]
        public static void ValidateScene()
        {
            var scene = EditorSceneManager.OpenScene(Scene);
            int missing = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Transform item in root.GetComponentsInChildren<Transform>(true))
                    missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(item.gameObject);
            if (missing != 0) throw new InvalidOperationException($"Scene contains {missing} missing scripts.");
            if (UnityEngine.Object.FindAnyObjectByType<AppManager>() == null)
                throw new InvalidOperationException("Scene is missing its AppManager.");
            if (UnityEngine.Object.FindAnyObjectByType<OVRCameraRig>() == null)
                throw new InvalidOperationException("Scene is missing its Meta XR rig.");
            Debug.Log("DOFFY scene validation passed.");
        }

        [MenuItem("Tools/DOFFY/Build Quest APK")]
        public static void BuildQuest()
        {
            BuildQuestApk(AndroidIdentifier, QuestApk);
        }

        [MenuItem("Tools/DOFFY/Build Meta update APK")]
        public static void BuildMetaUpdate()
        {
            BuildMetaUpdateApk(MetaUpdateApk, 16, false);
        }

        [MenuItem("Tools/DOFFY/Build Meta ARM64-only update APK")]
        public static void BuildMetaUpdateArm64Only()
        {
            BuildMetaUpdateApk(MetaArm64Apk, 18, true);
        }

        private static void BuildMetaUpdateApk(string apkPath, int versionCode, bool excludeArm32Assets)
        {
            apkPath = ProjectBuildPath(apkPath);
            string localIdentifier = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            int localVersionCode = PlayerSettings.Android.bundleVersionCode;
            MetaArm64AssetFilter.Enabled = excludeArm32Assets;
            MetaArm64AssetFilter.BackupDirectory = Path.GetDirectoryName(apkPath);
            try
            {
                BuildQuestApk(MetaAndroidIdentifier, apkPath, versionCode);
            }
            finally
            {
                try
                {
                    MetaArm64AssetFilter.RestoreGeneratedArchive();
                }
                finally
                {
                    MetaArm64AssetFilter.Enabled = false;
                    PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, localIdentifier);
                    PlayerSettings.Android.bundleVersionCode = localVersionCode;
                }
            }
        }

        private static void BuildQuestApk(string identifier, string apkPath, int versionCode = 16)
        {
            ValidateScene();
            PlayerSettings.companyName = "AIRO Lab";
            PlayerSettings.productName = "AIRO Doffy";
            PlayerSettings.bundleVersion = "0.9.7";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, identifier);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.bundleVersionCode = versionCode;
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Scene, true) };
            string output = ProjectBuildPath(apkPath);
            string outputName = Path.GetFileName(output);
            if (string.Equals(outputName, ProtectedRefactoredApk, StringComparison.OrdinalIgnoreCase) ||
                output.IndexOf(ProtectedRingSnapshot, StringComparison.OrdinalIgnoreCase) >= 0)
                throw new InvalidOperationException("Refusing to build over a protected wrist-ring APK or snapshot.");

            string directory = Path.GetDirectoryName(output);
            Directory.CreateDirectory(directory);
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = output,
                target = BuildTarget.Android,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"Quest build failed: {report.summary.result}, {report.summary.totalErrors} errors.");
            Debug.Log("DOFFY Quest build succeeded: " + output);
        }

        private static string ProjectBuildPath(string relativePath)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", relativePath));
        }
    }
}
