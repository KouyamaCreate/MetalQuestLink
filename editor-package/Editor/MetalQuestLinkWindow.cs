using System;
using UnityEditor;
using UnityEngine;

namespace MetalQuestLink.Editor
{
    public sealed class MetalQuestLinkWindow : EditorWindow
    {
        private string lastResult = "Ready";

        [MenuItem("Window/MetalQuestLink")]
        public static void ShowWindow()
        {
            GetWindow<MetalQuestLinkWindow>("MetalQuestLink");
        }

        private void OnEnable()
        {
            EditorApplication.update += Repaint;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Repaint;
        }

        private void OnGUI()
        {
            var settings = MetalQuestLinkSettings.instance;
            EditorGUILayout.LabelField("Connection", EditorStyles.boldLabel);
            if (LayerStatus.TryRead(out var status))
            {
                EditorGUILayout.LabelField("Quest", status.connected ? "Connected" : "Waiting for connection");
                EditorGUILayout.LabelField("FPS", status.fps.ToString("F1"));
                EditorGUILayout.LabelField("Pipeline latency", status.averagePipelineMs.ToString("F2") + " ms");
                EditorGUILayout.LabelField("Encoded frames", status.encodedFrames.ToString());
                EditorGUILayout.LabelField("Dropped frames", status.droppedFrames.ToString());
                if (status.streamWidth > 0 && status.streamHeight > 0)
                {
                    EditorGUILayout.LabelField(
                        "Stream resolution", $"{status.streamWidth} x {status.streamHeight}");
                }
            }
            else
            {
                EditorGUILayout.LabelField("Layer", "Not running");
            }
            EditorGUILayout.LabelField("Standalone XR",
                StandaloneOpenXRConfigurator.IsConfigured(out var xrStatus) ? "OpenXR" : xrStatus);
            var runtimeJson = OpenXRLayerInstaller.FindSimulatorRuntimeJson();
            EditorGUILayout.LabelField("OpenXR runtime",
                string.IsNullOrEmpty(runtimeJson) ? "Meta XR Simulator not found" : runtimeJson);
            var preflight = MetalQuestLinkPreflight.Evaluate();
            EditorGUILayout.LabelField("Project compatibility", preflight.Summary);
            var unityCompatibility = MetalQuestLinkPreflight.GetUnityCompatibility(Application.unityVersion);
            EditorGUILayout.LabelField("Unity compatibility",
                unityCompatibility.Verified ? "Verified" : unityCompatibility.Supported ? "Compatible (unverified)" : "Unsupported");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Setup", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            settings.port = EditorGUILayout.IntField("TCP port", settings.port);
            settings.adbPath = EditorGUILayout.TextField("adb path", settings.adbPath);
            settings.deviceSerial = EditorGUILayout.TextField("Quest serial (optional)", settings.deviceSerial);
            settings.apkPath = EditorGUILayout.TextField("APK path", settings.apkPath);
            settings.openXrRuntimeJsonPath = EditorGUILayout.TextField(
                "Runtime JSON path", settings.openXrRuntimeJsonPath);
            settings.wifiFallbackHost = EditorGUILayout.TextField(
                "Wi-Fi fallback host", settings.wifiFallbackHost);
            settings.bitrateMbps = EditorGUILayout.IntField("Bitrate Mbps (0 = Auto)", settings.bitrateMbps);
            settings.maxPendingFrames = EditorGUILayout.IntSlider(
                "Max pending frames", settings.maxPendingFrames, 1, 8);
            settings.autoStartQuestClient = EditorGUILayout.Toggle("Start client on Play", settings.autoStartQuestClient);
            settings.autoConfigureStandaloneOpenXR = EditorGUILayout.Toggle(
                "Configure Standalone OpenXR", settings.autoConfigureStandaloneOpenXR);
            settings.enablePassthroughPreview = EditorGUILayout.Toggle(
                "Passthrough preview", settings.enablePassthroughPreview);
            settings.showTrackedHands = EditorGUILayout.Toggle("Show tracked hands", settings.showTrackedHands);
            if (EditorGUI.EndChangeCheck())
            {
                settings.SaveSettings();
            }

            if (GUILayout.Button("Register OpenXR Layer"))
            {
                Run(() => OpenXRLayerInstaller.RegisterLayer());
            }
            if (GUILayout.Button("Quick Setup (Project + Quest)"))
            {
                Run(QuickSetup);
            }
            if (GUILayout.Button("Run Project Check"))
            {
                lastResult = MetalQuestLinkPreflight.Evaluate().ToMultilineString();
            }
            if (GUILayout.Button("Configure Standalone OpenXR"))
            {
                Run(() =>
                {
                    var changed = StandaloneOpenXRConfigurator.Configure();
                    return changed ? "Standalone OpenXR configured" : "Standalone OpenXR already configured";
                });
            }
            if (GUILayout.Button("Install Quest APK"))
            {
                RunAdb(AdbBridge.Install);
            }
            if (GUILayout.Button("Set adb reverse"))
            {
                RunAdb(AdbBridge.Reverse);
            }
            if (GUILayout.Button("Start Quest Client"))
            {
                RunAdb(AdbBridge.StartClient);
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(lastResult,
                preflight.IsReady ? MessageType.Info : MessageType.Error);
        }

        private static string QuickSetup()
        {
            var changed = StandaloneOpenXRConfigurator.Configure();
            var manifest = OpenXRLayerInstaller.RegisterLayer();
            var install = AdbBridge.Install();
            var report = MetalQuestLinkPreflight.Evaluate();
            var installStatus = install.Success
                ? "Quest APK installed"
                : $"Quest APK pending ({install.Output})";
            return $"Quick Setup complete. Standalone OpenXR " +
                   $"{(changed ? "configured" : "already configured")}; " +
                   $"layer registered at {manifest}; {installStatus}.\n" +
                   report.ToMultilineString();
        }

        private void Run(Func<string> action)
        {
            try
            {
                lastResult = action();
            }
            catch (Exception exception)
            {
                lastResult = exception.Message;
            }
        }

        private void RunAdb(Func<AdbResult> action)
        {
            var result = action();
            lastResult = result.Success ? "OK: " + result.Output : $"Failed ({result.ExitCode}): {result.Output}";
        }
    }
}
