using System;
using System.IO;
using System.Text;
using UnityEditor.PackageManager;
using UnityEngine;

namespace MetalQuestLink.Editor
{
    public static class OpenXRLayerInstaller
    {
        public const string LayerName = "XR_APILAYER_METALQUESTLINK_streaming";
        public const string DefaultSimulatorRuntimeJson =
            "/Applications/MetaXRSimulator.app/Contents/Resources/MetaXRSimulator/meta_openxr_simulator.json";

        public static string StatusPath => Path.Combine(ProjectStateDirectory, "status.json");
        public static string LogPath => Path.Combine(ProjectStateDirectory, "layer.log");
        public static string ProjectStateDirectory => Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", "Library", "MetalQuestLink"));

        public static void PrepareEnvironment()
        {
            Directory.CreateDirectory(ProjectStateDirectory);
            Environment.SetEnvironmentVariable("METALQUESTLINK_ENABLE_API_LAYER", "1");
            Environment.SetEnvironmentVariable("METALQUESTLINK_DISABLE_API_LAYER", null);
            Environment.SetEnvironmentVariable("METALQUESTLINK_PORT", MetalQuestLinkSettings.instance.port.ToString());
            Environment.SetEnvironmentVariable(
                "METALQUESTLINK_BITRATE_MBPS", MetalQuestLinkSettings.instance.bitrateMbps.ToString());
            Environment.SetEnvironmentVariable(
                "METALQUESTLINK_MAX_PENDING_FRAMES", MetalQuestLinkSettings.instance.maxPendingFrames.ToString());
            Environment.SetEnvironmentVariable("METALQUESTLINK_LAYER_LOG", LogPath);
            Environment.SetEnvironmentVariable("METALQUESTLINK_STATUS_FILE", StatusPath);
            var runtimeJson = FindSimulatorRuntimeJson();
            if (!string.IsNullOrEmpty(runtimeJson))
            {
                Environment.SetEnvironmentVariable("XR_RUNTIME_JSON", runtimeJson);
            }
        }

        public static string RegisterLayer()
        {
            PrepareEnvironment();
            var libraryPath = FindLayerLibrary();
            if (string.IsNullOrEmpty(libraryPath))
            {
                throw new FileNotFoundException(
                    "MetalQuestLink native layer is missing from this package. Reinstall the release package.");
            }

            var manifestDirectory = Environment.GetEnvironmentVariable("METALQUESTLINK_MANIFEST_DIR");
            if (string.IsNullOrEmpty(manifestDirectory))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                manifestDirectory = Path.Combine(home, ".local", "share", "openxr", "1", "api_layers", "implicit.d");
            }
            Directory.CreateDirectory(manifestDirectory);
            var manifestPath = Path.Combine(manifestDirectory, "XrApiLayer_metalquestlink.json");
            var escapedLibraryPath = EscapeJson(Path.GetFullPath(libraryPath));
            var json = "{\n" +
                       "  \"file_format_version\": \"1.0.0\",\n" +
                       "  \"api_layer\": {\n" +
                       $"    \"name\": \"{LayerName}\",\n" +
                       $"    \"library_path\": \"{escapedLibraryPath}\",\n" +
                       "    \"api_version\": \"1.0\",\n" +
                       "    \"implementation_version\": \"1\",\n" +
                       "    \"description\": \"MetalQuestLink OpenXR streaming layer\",\n" +
                       "    \"instance_extensions\": [\n" +
                       "      {\n" +
                       "        \"name\": \"XR_EXT_hand_tracking\",\n" +
                       "        \"extension_version\": \"4\"\n" +
                       "      }\n" +
                       "    ],\n" +
                       "    \"enable_environment\": \"METALQUESTLINK_ENABLE_API_LAYER\",\n" +
                       "    \"disable_environment\": \"METALQUESTLINK_DISABLE_API_LAYER\"\n" +
                       "  }\n" +
                       "}\n";
            if (!File.Exists(manifestPath) || File.ReadAllText(manifestPath) != json)
            {
                File.WriteAllText(manifestPath, json, new UTF8Encoding(false));
            }
            return manifestPath;
        }

        public static string FindLayerLibrary()
        {
            var package = PackageInfo.FindForAssembly(typeof(OpenXRLayerInstaller).Assembly);
            if (package == null)
            {
                return null;
            }
            var candidates = new[]
            {
                Path.Combine(package.resolvedPath, "Native~", "macOS", "libmetalquestlink_openxr_layer.so"),
            };
            foreach (var candidate in candidates)
            {
                var fullPath = Path.GetFullPath(candidate);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
            return null;
        }

        public static string FindDefaultApk()
        {
            var configured = MetalQuestLinkSettings.instance.apkPath;
            if (!string.IsNullOrEmpty(configured))
            {
                return ResolveProjectPath(configured);
            }
            var package = PackageInfo.FindForAssembly(typeof(OpenXRLayerInstaller).Assembly);
            if (package == null)
            {
                return null;
            }
            var candidates = new[]
            {
                Path.Combine(package.resolvedPath, "QuestClient~", "MetalQuestLink.apk"),
            };
            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            return Path.GetFullPath(candidates[0]);
        }

        public static string FindSimulatorRuntimeJson()
        {
            var candidates = new[]
            {
                MetalQuestLinkSettings.instance.openXrRuntimeJsonPath,
                Environment.GetEnvironmentVariable("METALQUESTLINK_XR_RUNTIME_JSON"),
                Environment.GetEnvironmentVariable("XR_RUNTIME_JSON"),
                DefaultSimulatorRuntimeJson,
            };
            foreach (var candidate in candidates)
            {
                var resolved = ResolveProjectPath(candidate);
                if (!string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved))
                {
                    return resolved;
                }
            }
            return null;
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        public static string ResolveProjectPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }
            return Path.GetFullPath(Path.IsPathRooted(path)
                ? path
                : Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), path));
        }
    }
}
