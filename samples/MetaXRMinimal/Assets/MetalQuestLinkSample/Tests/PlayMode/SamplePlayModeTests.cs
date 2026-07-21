using System.Collections;
using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MetalQuestLink.Sample.Tests
{
    public sealed class SamplePlayModeTests
    {
        private const string ScenePath = "Assets/MetalQuestLinkSample/Scenes/Minimal.unity";
        private static string StateDirectory => Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", "Library", "MetalQuestLink"));
        private static string LayerLogPath => Path.Combine(StateDirectory, "layer.log");
        private static string StatusPath => Path.Combine(StateDirectory, "status.json");

        [Serializable]
        private sealed class Status
        {
            public bool connected;
        }

        [UnityTest]
        public IEnumerator LayerLoadsAndPublishesConnectionStatus()
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
            Debug.Log("METALQUESTLINK_SAMPLE_PLAY_VERIFIED layer=loaded status=" +
                      (status.connected ? "connected" : "waiting_for_connection"));
        }
    }
}
