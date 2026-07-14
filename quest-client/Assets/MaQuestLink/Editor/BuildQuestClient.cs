using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;

namespace MaQuestLink.QuestClient.Editor
{
    public static class BuildQuestClient
    {
        private const string ScenePath = "Assets/MaQuestLink/Generated/QuestClient.unity";
        private const string DefaultApkPath = "Builds/MaQuestLink.apk";

        [MenuItem("MaQuestLink/Build Quest Client APK")]
        public static void Build()
        {
            ConfigureProject();
            BuildScene();

            var apkPath = GetCommandLineValue("-maquestlinkOutput") ?? DefaultApkPath;
            var absolutePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", apkPath));
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = absolutePath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"MaQuestLink APK build failed: {report.summary.result}, errors={report.summary.totalErrors}");
            }
            Debug.Log($"MAQUESTLINK_APK_BUILT path={absolutePath} bytes={report.summary.totalSize}");
        }

        private static void ConfigureProject()
        {
            PlayerSettings.companyName = "MaQuestLink";
            PlayerSettings.productName = "MaQuestLink";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.maquestlink.questclient");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.GameActivity;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.MTRendering = true;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
                new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 72;
            EnableAndroidOpenXr();
            AssetDatabase.SaveAssets();
        }

        private static void EnableAndroidOpenXr()
        {
            var method = typeof(XRGeneralSettingsPerBuildTarget).GetMethod(
                "GetOrCreate", BindingFlags.Static | BindingFlags.NonPublic);
            var settings = method?.Invoke(null, null) as XRGeneralSettingsPerBuildTarget;
            if (settings == null)
            {
                throw new InvalidOperationException("XR Plug-in Management settings could not be created");
            }
            if (!settings.HasSettingsForBuildTarget(BuildTargetGroup.Android))
            {
                settings.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Android);
            }
            if (!settings.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
            {
                settings.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            }
            var manager = settings.ManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            if (!XRPackageMetadataStore.AssignLoader(
                    manager, "UnityEngine.XR.OpenXR.OpenXRLoader", BuildTargetGroup.Android) &&
                manager.activeLoaders.Count == 0)
            {
                throw new InvalidOperationException("Android OpenXR loader could not be assigned");
            }
            EnableOpenXrFeatures();
        }

        private static void EnableOpenXrFeatures()
        {
            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
            var requiredFeatureIds = new[]
            {
                "com.unity.openxr.feature.metaquest",
                "com.unity.openxr.feature.input.oculustouch",
                "com.unity.openxr.feature.input.metaquestplus",
                "com.unity.openxr.feature.compositionlayers",
            };
            foreach (var featureId in requiredFeatureIds)
            {
                var feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(
                    BuildTargetGroup.Android, featureId);
                if (feature == null)
                {
                    throw new InvalidOperationException($"Required OpenXR feature not found: {featureId}");
                }
                feature.enabled = true;
                EditorUtility.SetDirty(feature);
                if (featureId == "com.unity.openxr.feature.metaquest")
                {
                    ConfigureQuest3Targets(feature);
                }
            }
        }

        private static void ConfigureQuest3Targets(UnityEngine.Object metaQuestFeature)
        {
            var serialized = new SerializedObject(metaQuestFeature);
            var devices = serialized.FindProperty("targetDevices");
            if (devices == null || !devices.isArray)
            {
                throw new InvalidOperationException("Meta Quest targetDevices setting was not found");
            }
            for (var index = 0; index < devices.arraySize; index++)
            {
                var device = devices.GetArrayElementAtIndex(index);
                var name = device.FindPropertyRelative("manifestName")?.stringValue;
                var enabled = device.FindPropertyRelative("enabled");
                if (enabled != null)
                {
                    enabled.boolValue = name == "eureka" || name == "quest3s";
                }
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var rig = new GameObject("OVRCameraRig");
            AddRequiredMetaComponent(rig, "OVRCameraRig");
            AddRequiredMetaComponent(rig, "OVRManager");

            var client = new GameObject("MaQuestLinkClient");
            client.AddComponent<QuestClientController>();
            var screen = new GameObject("ExternalSurfaceStereoScreen");
            screen.transform.SetParent(client.transform, false);
            screen.AddComponent<ExternalSurfacePresenter>();

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new IOException($"Could not save generated scene: {ScenePath}");
            }
        }

        private static void AddRequiredMetaComponent(GameObject target, string typeName)
        {
            Type found = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                found = assembly.GetType(typeName, false);
                if (found != null)
                {
                    break;
                }
            }
            if (found == null || !typeof(Component).IsAssignableFrom(found))
            {
                throw new InvalidOperationException($"Meta XR SDK component not found: {typeName}");
            }
            target.AddComponent(found);
        }

        private static string GetCommandLineValue(string option)
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index + 1 < arguments.Length; index++)
            {
                if (string.Equals(arguments[index], option, StringComparison.Ordinal))
                {
                    return arguments[index + 1];
                }
            }
            return null;
        }
    }
}
