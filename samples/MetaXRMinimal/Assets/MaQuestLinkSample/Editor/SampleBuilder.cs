using System;
using System.IO;
using System.Reflection;
using MaQuestLink.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;

namespace MaQuestLink.Sample.Editor
{
    public static class SampleBuilder
    {
        public const string ScenePath = "Assets/MaQuestLinkSample/Scenes/Minimal.unity";

        public static void ConfigureAndBuild()
        {
            ConfigureStandaloneOpenXr();
            BuildScene();
            Directory.CreateDirectory(OpenXRLayerInstaller.ProjectStateDirectory);
            DeleteIfPresent(OpenXRLayerInstaller.LogPath);
            DeleteIfPresent(OpenXRLayerInstaller.StatusPath);
            var manifest = OpenXRLayerInstaller.RegisterLayer();
            Debug.Log($"MAQUESTLINK_SAMPLE_READY scene={ScenePath} manifest={manifest}");
        }

        private static void ConfigureStandaloneOpenXr()
        {
            var method = typeof(XRGeneralSettingsPerBuildTarget).GetMethod(
                "GetOrCreate", BindingFlags.Static | BindingFlags.NonPublic);
            var settings = method?.Invoke(null, null) as XRGeneralSettingsPerBuildTarget;
            if (settings == null)
            {
                throw new InvalidOperationException("XR Plug-in Management settings could not be created");
            }
            if (!settings.HasSettingsForBuildTarget(BuildTargetGroup.Standalone))
            {
                settings.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Standalone);
            }
            if (!settings.HasManagerSettingsForBuildTarget(BuildTargetGroup.Standalone))
            {
                settings.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Standalone);
            }
            var manager = settings.ManagerSettingsForBuildTarget(BuildTargetGroup.Standalone);
            if (!XRPackageMetadataStore.AssignLoader(
                    manager, "UnityEngine.XR.OpenXR.OpenXRLoader", BuildTargetGroup.Standalone) &&
                manager.activeLoaders.Count == 0)
            {
                throw new InvalidOperationException("Standalone OpenXR loader could not be assigned");
            }

            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Standalone);
            foreach (var featureId in new[]
                     {
                         "com.unity.openxr.feature.input.oculustouch",
                         "com.unity.openxr.feature.metaquest",
                     })
            {
                var feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(
                    BuildTargetGroup.Standalone, featureId);
                if (feature != null)
                {
                    feature.enabled = true;
                    EditorUtility.SetDirty(feature);
                }
            }
            AssetDatabase.SaveAssets();
        }

        private static void BuildScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var rigObject = new GameObject("OVRCameraRig");
            rigObject.AddComponent<OVRManager>();
            var rig = rigObject.AddComponent<OVRCameraRig>();
            rig.EnsureGameObjectIntegrity();
            AddGrabber(rig.leftControllerAnchor, OVRInput.Controller.LTouch, "LeftGrabber");
            AddGrabber(rig.rightControllerAnchor, OVRInput.Controller.RTouch, "RightGrabber");

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "GrabbableCube";
            cube.transform.position = new Vector3(0f, 1.25f, 1.25f);
            cube.transform.localScale = Vector3.one * 0.25f;
            var body = cube.AddComponent<Rigidbody>();
            body.useGravity = false;
            var grabbable = cube.AddComponent<OVRGrabbable>();
            var grabbableProperties = new SerializedObject(grabbable);
            var grabPoints = grabbableProperties.FindProperty("m_grabPoints");
            grabPoints.arraySize = 1;
            grabPoints.GetArrayElementAtIndex(0).objectReferenceValue = cube.GetComponent<BoxCollider>();
            grabbableProperties.ApplyModifiedPropertiesWithoutUndo();

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = Vector3.one * 0.5f;

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new IOException("Sample scene could not be saved");
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        private static void AddGrabber(Transform anchor, OVRInput.Controller controller, string name)
        {
            var hand = new GameObject(name);
            hand.transform.SetParent(anchor, false);
            var rigidbody = hand.AddComponent<Rigidbody>();
            rigidbody.useGravity = false;
            rigidbody.isKinematic = true;

            var volume = hand.AddComponent<SphereCollider>();
            volume.radius = 0.12f;
            volume.isTrigger = true;
            var grabber = hand.AddComponent<OVRGrabber>();
            var properties = new SerializedObject(grabber);
            properties.FindProperty("m_gripTransform").objectReferenceValue = hand.transform;
            var volumes = properties.FindProperty("m_grabVolumes");
            volumes.arraySize = 1;
            volumes.GetArrayElementAtIndex(0).objectReferenceValue = volume;
            properties.FindProperty("m_controller").intValue = (int)controller;
            properties.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void DeleteIfPresent(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
