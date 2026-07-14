using System;
using System.IO;
using System.Text;
using UnityEditor.PackageManager;
using UnityEngine;

namespace MaQuestLink.Editor
{
    public static class OpenXRLayerInstaller
    {
        public const string LayerName = "XR_APILAYER_MAQUESTLINK_streaming";

        public static string StatusPath => Path.Combine(ProjectStateDirectory, "status.json");
        public static string LogPath => Path.Combine(ProjectStateDirectory, "layer.log");
        public static string ProjectStateDirectory => Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", "Library", "MaQuestLink"));

        public static void PrepareEnvironment()
        {
            Directory.CreateDirectory(ProjectStateDirectory);
            Environment.SetEnvironmentVariable("MAQUESTLINK_ENABLE_API_LAYER", "1");
            Environment.SetEnvironmentVariable("MAQUESTLINK_DISABLE_API_LAYER", null);
            Environment.SetEnvironmentVariable("MAQUESTLINK_PORT", MaQuestLinkSettings.instance.port.ToString());
            Environment.SetEnvironmentVariable("MAQUESTLINK_LAYER_LOG", LogPath);
            Environment.SetEnvironmentVariable("MAQUESTLINK_STATUS_FILE", StatusPath);
        }

        public static string RegisterLayer()
        {
            PrepareEnvironment();
            var libraryPath = FindLayerLibrary();
            if (string.IsNullOrEmpty(libraryPath))
            {
                throw new FileNotFoundException(
                    "MaQuestLink native layer is missing from this package. Reinstall the release package.");
            }

            var manifestDirectory = Environment.GetEnvironmentVariable("MAQUESTLINK_MANIFEST_DIR");
            if (string.IsNullOrEmpty(manifestDirectory))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                manifestDirectory = Path.Combine(home, ".local", "share", "openxr", "1", "api_layers", "implicit.d");
            }
            Directory.CreateDirectory(manifestDirectory);
            var manifestPath = Path.Combine(manifestDirectory, "XrApiLayer_maquestlink.json");
            var escapedLibraryPath = EscapeJson(Path.GetFullPath(libraryPath));
            var json = "{\n" +
                       "  \"file_format_version\": \"1.0.0\",\n" +
                       "  \"api_layer\": {\n" +
                       $"    \"name\": \"{LayerName}\",\n" +
                       $"    \"library_path\": \"{escapedLibraryPath}\",\n" +
                       "    \"api_version\": \"1.0\",\n" +
                       "    \"implementation_version\": \"1\",\n" +
                       "    \"description\": \"MaQuestLink OpenXR streaming layer\",\n" +
                       "    \"instance_extensions\": [\n" +
                       "      {\n" +
                       "        \"name\": \"XR_EXT_hand_tracking\",\n" +
                       "        \"extension_version\": \"4\"\n" +
                       "      }\n" +
                       "    ],\n" +
                       "    \"enable_environment\": \"MAQUESTLINK_ENABLE_API_LAYER\",\n" +
                       "    \"disable_environment\": \"MAQUESTLINK_DISABLE_API_LAYER\"\n" +
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
                Path.Combine(package.resolvedPath, "Native~", "macOS", "libmaquestlink_openxr_layer.so"),
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
            var configured = MaQuestLinkSettings.instance.apkPath;
            if (!string.IsNullOrEmpty(configured))
            {
                return Path.GetFullPath(configured);
            }
            var package = PackageInfo.FindForAssembly(typeof(OpenXRLayerInstaller).Assembly);
            if (package == null)
            {
                return null;
            }
            var candidates = new[]
            {
                Path.Combine(package.resolvedPath, "QuestClient~", "MaQuestLink.apk"),
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

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
