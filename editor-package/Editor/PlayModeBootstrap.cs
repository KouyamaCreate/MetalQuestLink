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
                var reverse = AdbBridge.Reverse();
                if (MaQuestLinkSettings.instance.autoStartQuestClient && reverse.Success)
                {
                    AdbBridge.StartClient();
                }
                Debug.Log($"MAQUESTLINK_PLAY_READY manifest={manifest} adbReverse={reverse.Success} " +
                          $"status=waiting_for_connection port={MaQuestLinkSettings.instance.port}");
            }
            catch (Exception exception)
            {
                Debug.LogError($"MAQUESTLINK_PLAY_SETUP_FAILED {exception}");
            }
        }
    }
}
