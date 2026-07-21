using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MetalQuestLink.Editor
{
    public sealed class MetalQuestLinkPreflightReport
    {
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();

        public bool IsReady => Errors.Count == 0;

        public string Summary => !IsReady
            ? $"{Errors.Count} error(s)"
            : Warnings.Count > 0 ? $"Ready with {Warnings.Count} warning(s)" : "Ready";

        public string ToMultilineString()
        {
            var lines = new List<string> { "Project check: " + Summary };
            foreach (var error in Errors) lines.Add("ERROR: " + error);
            foreach (var warning in Warnings) lines.Add("WARNING: " + warning);
            return string.Join("\n", lines);
        }
    }

    public static class MetalQuestLinkPreflight
    {
        public const int MinimumUnityMajor = 2022;
        public const int MinimumUnityMinor = 3;

        public static MetalQuestLinkPreflightReport Evaluate()
        {
            var report = new MetalQuestLinkPreflightReport();
            var settings = MetalQuestLinkSettings.instance;
            var compatibility = GetUnityCompatibility(Application.unityVersion);
            if (!compatibility.Supported)
            {
                report.Errors.Add(compatibility.Message);
            }
            else if (!compatibility.Verified)
            {
                report.Warnings.Add(compatibility.Message);
            }
            if (settings.port < 1 || settings.port > 65535)
            {
                report.Errors.Add("TCP port must be between 1 and 65535.");
            }
            if (settings.bitrateMbps < 0 || settings.bitrateMbps > 80)
            {
                report.Errors.Add("Bitrate must be 0 (Auto) or between 1 and 80 Mbps.");
            }
            if (settings.maxPendingFrames < 1 || settings.maxPendingFrames > 8)
            {
                report.Errors.Add("Max pending frames must be between 1 and 8.");
            }
            if (!StandaloneOpenXRConfigurator.IsConfigured(out var xrStatus))
            {
                report.Errors.Add(xrStatus + ". Run Configure Standalone OpenXR.");
            }
            if (string.IsNullOrEmpty(OpenXRLayerInstaller.FindSimulatorRuntimeJson()))
            {
                report.Errors.Add("Meta XR Simulator runtime JSON was not found.");
            }
            else if (!string.IsNullOrWhiteSpace(settings.openXrRuntimeJsonPath) &&
                     !File.Exists(OpenXRLayerInstaller.ResolveProjectPath(
                         settings.openXrRuntimeJsonPath)))
            {
                report.Warnings.Add(
                    "Configured runtime JSON was not found; the auto-detected runtime will be used.");
            }
            if (string.IsNullOrEmpty(OpenXRLayerInstaller.FindLayerLibrary()))
            {
                report.Errors.Add("The packaged Apple Silicon OpenXR layer is missing.");
            }
            if (!string.IsNullOrWhiteSpace(settings.apkPath) &&
                !File.Exists(OpenXRLayerInstaller.ResolveProjectPath(settings.apkPath)))
            {
                report.Warnings.Add("Configured APK does not exist; clear APK path to use the packaged client.");
            }
            if (settings.autoStartQuestClient)
            {
                var adb = AdbBridge.FindAdb();
                if (string.IsNullOrEmpty(adb))
                {
                    report.Warnings.Add("adb was not found; the Quest client must be started manually.");
                }
                else if (!string.IsNullOrWhiteSpace(settings.adbPath) &&
                         !File.Exists(OpenXRLayerInstaller.ResolveProjectPath(settings.adbPath)))
                {
                    report.Warnings.Add(
                        $"Configured adb does not exist; using auto-detected adb: {adb}");
                }
            }
            if (settings.bitrateMbps == 0)
            {
                report.Warnings.Add("Stream bitrate is Auto and will scale with the eye texture resolution.");
            }
            return report;
        }

        public static UnityCompatibility GetUnityCompatibility(string version)
        {
            if (!TryGetUnityMajorMinor(version, out var major, out var minor))
            {
                return new UnityCompatibility(false, false,
                    $"Unity version '{version}' could not be parsed.");
            }
            if (major < MinimumUnityMajor ||
                (major == MinimumUnityMajor && minor < MinimumUnityMinor))
            {
                return new UnityCompatibility(false, false,
                    $"Unity {version} is unsupported; use Unity 2022.3 LTS or newer.");
            }
            if (major == 6000 && (minor == 2 || minor == 3))
            {
                return new UnityCompatibility(true, true,
                    $"Unity {version} is in the verified compatibility matrix.");
            }
            return new UnityCompatibility(true, false,
                $"Unity {version} meets the package baseline but is not in the verified matrix; run Project Check before Play.");
        }

        public static bool TryGetUnityMajorMinor(string version, out int major, out int minor)
        {
            major = 0;
            minor = 0;
            if (string.IsNullOrWhiteSpace(version)) return false;
            var parts = version.Split('.');
            return parts.Length >= 2 && int.TryParse(parts[0], out major) &&
                   int.TryParse(parts[1], out minor);
        }
    }

    public readonly struct UnityCompatibility
    {
        public UnityCompatibility(bool supported, bool verified, string message)
        {
            Supported = supported;
            Verified = verified;
            Message = message;
        }

        public bool Supported { get; }
        public bool Verified { get; }
        public string Message { get; }
    }
}
