using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.XR.Management;

namespace MetalQuestLink.Editor
{
    [InitializeOnLoad]
    public static class StandaloneOpenXRConfigurator
    {
        public const string OpenXRLoaderType = "UnityEngine.XR.OpenXR.OpenXRLoader";
        public const string OculusTouchFeatureId = "com.unity.openxr.feature.input.oculustouch";
        public const string MetaXRFeatureSetId = "com.meta.openxr.featureset.metaxr";
        private const string SettingsKey = "com.unity.xr.management.loader_settings";

        static StandaloneOpenXRConfigurator()
        {
            EditorApplication.delayCall += ConfigureOnLoad;
        }

        public static bool IsConfigured(out string status)
        {
            var settings = FindSettings();
            var manager = settings?.ManagerSettingsForBuildTarget(BuildTargetGroup.Standalone);
            if (manager == null)
            {
                status = "Standalone XR settings are missing";
                return false;
            }
            var loaders = manager.activeLoaders.Where(loader => loader != null).ToArray();
            var openXrIndex = Array.FindIndex(
                loaders, loader => loader.GetType().FullName == OpenXRLoaderType);
            var general = settings.SettingsForBuildTarget(BuildTargetGroup.Standalone);
            if (openXrIndex == 0 && general != null && general.InitManagerOnStart)
            {
                status = loaders.Length == 1
                    ? "OpenXR"
                    : $"OpenXR ({loaders.Length - 1} fallback loader(s) preserved)";
                return true;
            }
            if (openXrIndex > 0)
            {
                status = "OpenXR must be the first Standalone XR loader; current order: " +
                         string.Join(", ", loaders.Select(loader => loader.GetType().Name));
            }
            else if (openXrIndex < 0)
            {
                status = loaders.Length == 0
                    ? "No Standalone XR loader"
                    : "OpenXR is missing; current loaders: " +
                      string.Join(", ", loaders.Select(loader => loader.GetType().Name));
            }
            else
            {
                status = "Initialize XR on Startup is disabled for Standalone";
            }
            return false;
        }

        public static bool Configure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Stop Play mode before configuring Standalone OpenXR");
            }
            var changed = false;
            if (!IsConfigured(out _))
            {
                var settings = GetOrCreateSettings();
                if (!settings.HasSettingsForBuildTarget(BuildTargetGroup.Standalone))
                {
                    settings.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Standalone);
                }
                if (!settings.HasManagerSettingsForBuildTarget(BuildTargetGroup.Standalone))
                {
                    settings.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Standalone);
                }
                var general = settings.SettingsForBuildTarget(BuildTargetGroup.Standalone);
                var manager = settings.ManagerSettingsForBuildTarget(BuildTargetGroup.Standalone);
                var existingLoaders = manager.activeLoaders.Where(loader => loader != null).ToList();
                if (existingLoaders.All(loader => loader.GetType().FullName != OpenXRLoaderType) &&
                    !XRPackageMetadataStore.AssignLoader(
                        manager, OpenXRLoaderType, BuildTargetGroup.Standalone))
                {
                    throw new InvalidOperationException(
                        "Standalone OpenXR loader could not be assigned. Reimport the MetalQuestLink package.");
                }
                existingLoaders = manager.activeLoaders.Where(loader => loader != null).ToList();
                var openXrLoader = existingLoaders.FirstOrDefault(
                    loader => loader != null && loader.GetType().FullName == OpenXRLoaderType);
                if (openXrLoader == null)
                {
                    throw new InvalidOperationException("Standalone OpenXR loader could not be selected");
                }
                var orderedTypeNames = OrderLoaderTypeNames(
                    existingLoaders.Select(loader => loader.GetType().FullName));
                var orderedLoaders = orderedTypeNames.Select(typeName =>
                        existingLoaders.First(loader => loader.GetType().FullName == typeName))
                    .ToList();
                if (!manager.TrySetLoaders(orderedLoaders))
                {
                    throw new InvalidOperationException("Standalone OpenXR loader could not be moved first");
                }
                general.InitManagerOnStart = true;
                EditorUtility.SetDirty(general);
                EditorUtility.SetDirty(manager);
                EditorUtility.SetDirty(settings);
                changed = true;
            }
            changed |= EnableFeature(BuildTargetGroup.Standalone, OculusTouchFeatureId);
            changed |= EnableFeatureSet(BuildTargetGroup.Standalone, MetaXRFeatureSetId);
            AssetDatabase.SaveAssets();
            if (changed)
            {
                Debug.Log("METALQUESTLINK_STANDALONE_OPENXR_CONFIGURED loader=OpenXR touch=1 metaFeatureSet=1");
            }
            return changed;
        }

        public static IReadOnlyList<string> OrderLoaderTypeNames(IEnumerable<string> typeNames)
        {
            var unique = typeNames.Where(typeName => !string.IsNullOrEmpty(typeName))
                .Distinct().ToList();
            var openXr = unique.FirstOrDefault(typeName => typeName == OpenXRLoaderType);
            if (openXr == null)
            {
                unique.Insert(0, OpenXRLoaderType);
                return unique;
            }
            unique.Remove(openXr);
            unique.Insert(0, openXr);
            return unique;
        }

        private static void ConfigureOnLoad()
        {
            if (!MetalQuestLinkSettings.instance.autoConfigureStandaloneOpenXR ||
                EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }
            try
            {
                Configure();
            }
            catch (Exception exception)
            {
                Debug.LogError("METALQUESTLINK_STANDALONE_OPENXR_SETUP_FAILED " + exception.Message);
            }
        }

        private static XRGeneralSettingsPerBuildTarget FindSettings()
        {
            EditorBuildSettings.TryGetConfigObject(SettingsKey, out XRGeneralSettingsPerBuildTarget settings);
            if (settings != null)
            {
                return settings;
            }
            var guid = AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget").FirstOrDefault();
            return string.IsNullOrEmpty(guid)
                ? null
                : AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(AssetDatabase.GUIDToAssetPath(guid));
        }

        private static bool EnableFeature(BuildTargetGroup group, string featureId)
        {
            FeatureHelpers.RefreshFeatures(group);
            var feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(group, featureId);
            if (feature == null || feature.enabled)
            {
                return false;
            }
            feature.enabled = true;
            EditorUtility.SetDirty(feature);
            return true;
        }

        private static bool EnableFeatureSet(BuildTargetGroup group, string featureSetId)
        {
            var featureSet = OpenXRFeatureSetManager.GetFeatureSetWithId(group, featureSetId);
            if (featureSet == null || featureSet.isEnabled)
            {
                return false;
            }
            featureSet.isEnabled = true;
            OpenXRFeatureSetManager.SetFeaturesFromEnabledFeatureSets(group);
            return true;
        }

        private static XRGeneralSettingsPerBuildTarget GetOrCreateSettings()
        {
            var existing = FindSettings();
            if (existing != null)
            {
                EditorBuildSettings.AddConfigObject(SettingsKey, existing, true);
                return existing;
            }
            var method = typeof(XRGeneralSettingsPerBuildTarget).GetMethod(
                "GetOrCreate", BindingFlags.Static | BindingFlags.NonPublic);
            var created = method?.Invoke(null, null) as XRGeneralSettingsPerBuildTarget;
            if (created == null)
            {
                throw new InvalidOperationException("XR Plug-in Management settings could not be created");
            }
            return created;
        }
    }
}
