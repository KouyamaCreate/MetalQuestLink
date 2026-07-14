using System.Collections;
using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MaQuestLink.Sample.Tests
{
    public sealed class SamplePlayModeTests
    {
        private const string ScenePath = "Assets/MaQuestLinkSample/Scenes/Minimal.unity";
        private static string StateDirectory => Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", "Library", "MaQuestLink"));
        private static string LayerLogPath => Path.Combine(StateDirectory, "layer.log");
        private static string StatusPath => Path.Combine(StateDirectory, "status.json");

        [Serializable]
        private sealed class Status
        {
            public bool connected;
        }

        [UnityTest]
        public IEnumerator LayerLoadsAndWaitsForQuestConnection()
        {
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            Assert.IsNotNull(UnityEngine.Object.FindFirstObjectByType<OVRCameraRig>());
            Assert.IsNotNull(UnityEngine.Object.FindFirstObjectByType<OVRGrabbable>());

            var deadline = Time.realtimeSinceStartup + 10f;
            string log = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (File.Exists(LayerLogPath))
                {
                    log = File.ReadAllText(LayerLogPath);
                    if (log.Contains("loaded instance for"))
                    {
                        break;
                    }
                }
                yield return null;
            }

            Assert.That(log, Does.Contain("loaded instance for"),
                "The implicit OpenXR layer was not loaded by Unity Play mode");
            Assert.IsTrue(File.Exists(StatusPath), "The layer status file was not written");
            var status = JsonUtility.FromJson<Status>(File.ReadAllText(StatusPath));
            Assert.IsNotNull(status);
            Assert.IsFalse(status.connected, "The layer should be waiting when no Quest client is connected");
            Debug.Log("MAQUESTLINK_SAMPLE_PLAY_VERIFIED layer=loaded status=waiting_for_connection");
        }
    }
}
