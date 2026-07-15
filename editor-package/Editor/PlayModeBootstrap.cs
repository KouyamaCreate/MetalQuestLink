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
            try
            {
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
                var manifest = OpenXRLayerInstaller.RegisterLayer();
                var settings = MaQuestLinkSettings.instance;
                var adb = settings.autoStartQuestClient ? AdbBridge.StartClient() : AdbBridge.Reverse();
                Debug.Log($"MAQUESTLINK_PLAY_READY manifest={manifest} adbReady={adb.Success} " +
                          $"questClientStarted={settings.autoStartQuestClient && adb.Success} " +
                          $"passthrough={settings.enablePassthroughPreview} hands={settings.showTrackedHands} " +
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
