using System;
using System.IO;
using UnityEngine;

namespace MaQuestLink.Editor
{
    [Serializable]
    public sealed class LayerStatus
    {
        public int version;
        public bool connected;
        public ulong encodedFrames;
        public ulong droppedFrames;
        public int streamWidth;
        public int streamHeight;
        public float fps;
        public float averageCopyMs;
        public float averageEncodeMs;
        public float averagePipelineMs;

        public static bool TryRead(out LayerStatus status)
        {
            status = null;
            try
            {
                if (!File.Exists(OpenXRLayerInstaller.StatusPath))
                {
                    return false;
                }
                status = JsonUtility.FromJson<LayerStatus>(File.ReadAllText(OpenXRLayerInstaller.StatusPath));
                return status != null;
            }
            catch (IOException)
            {
                return false;
            }
        }
    }
}
