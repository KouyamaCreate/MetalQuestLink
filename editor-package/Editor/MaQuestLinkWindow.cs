using System;
using UnityEditor;
using UnityEngine;

namespace MaQuestLink.Editor
{
    public sealed class MaQuestLinkWindow : EditorWindow
    {
        private string lastResult = "Ready";

        [MenuItem("Window/MaQuestLink")]
        public static void ShowWindow()
        {
            GetWindow<MaQuestLinkWindow>("MaQuestLink");
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
            var settings = MaQuestLinkSettings.instance;
            EditorGUILayout.LabelField("Connection", EditorStyles.boldLabel);
            if (LayerStatus.TryRead(out var status))
            {
                EditorGUILayout.LabelField("Quest", status.connected ? "Connected" : "Waiting for connection");
                EditorGUILayout.LabelField("FPS", status.fps.ToString("F1"));
                EditorGUILayout.LabelField("Pipeline latency", status.averagePipelineMs.ToString("F2") + " ms");
                EditorGUILayout.LabelField("Encoded frames", status.encodedFrames.ToString());
            }
            else
            {
                EditorGUILayout.LabelField("Layer", "Not running");
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Setup", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            settings.port = EditorGUILayout.IntField("TCP port", settings.port);
            settings.adbPath = EditorGUILayout.TextField("adb path", settings.adbPath);
            settings.apkPath = EditorGUILayout.TextField("APK path", settings.apkPath);
            settings.autoStartQuestClient = EditorGUILayout.Toggle("Start client on Play", settings.autoStartQuestClient);
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
            EditorGUILayout.HelpBox(lastResult, MessageType.Info);
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
