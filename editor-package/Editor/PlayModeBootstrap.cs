using System;
using UnityEditor;
using UnityEngine;

namespace MaQuestLink.Editor
{
    [InitializeOnLoad]
    public static class PlayModeBootstrap
    {
        static PlayModeBootstrap()
        {
            EditorApplication.delayCall += RegisterLayerOnLoad;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void RegisterLayerOnLoad()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }
            try
            {
                if (MaQuestLinkSettings.instance.autoConfigureStandaloneOpenXR)
                {
                    StandaloneOpenXRConfigurator.Configure();
                }
                var manifest = OpenXRLayerInstaller.RegisterLayer();
                Debug.Log($"MAQUESTLINK_LAYER_REGISTERED manifest={manifest}");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"MAQUESTLINK_LAYER_REGISTRATION_FAILED {exception.Message}");
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode)
            {
                return;
            }
            try
            {
                var preflight = MaQuestLinkPreflight.Evaluate();
                if (!preflight.IsReady)
                {
                    EditorApplication.isPlaying = false;
                    Debug.LogError("MAQUESTLINK_PLAY_BLOCKED " + preflight.ToMultilineString());
                    return;
                }
                var manifest = OpenXRLayerInstaller.RegisterLayer();
                var settings = MaQuestLinkSettings.instance;
                var adb = settings.autoStartQuestClient
                    ? AdbBridge.StartClientDetached()
                    : AdbBridge.Reverse();
                Debug.Log($"MAQUESTLINK_PLAY_READY manifest={manifest} adbReady={adb.Success} " +
                          $"questClientStarted={(settings.autoStartQuestClient && adb.Success ? "scheduled" : "false")} " +
                          $"passthrough={settings.enablePassthroughPreview} hands={settings.showTrackedHands} " +
                          $"bitrateMbps={(settings.bitrateMbps == 0 ? "auto" : settings.bitrateMbps.ToString())} " +
                          $"maxPendingFrames={settings.maxPendingFrames} " +
                          $"status=waiting_for_connection port={MaQuestLinkSettings.instance.port}");
                if (!adb.Success)
                {
                    Debug.LogWarning($"MAQUESTLINK_ADB_SETUP_FAILED exit={adb.ExitCode} output={adb.Output}");
                }
            }
            catch (Exception exception)
            {
                Debug.LogError($"MAQUESTLINK_PLAY_SETUP_FAILED {exception}");
            }
        }
    }
}
