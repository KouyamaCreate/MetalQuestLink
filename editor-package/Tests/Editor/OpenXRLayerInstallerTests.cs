using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MetalQuestLink.Editor.Tests
{
    public sealed class OpenXRLayerInstallerTests
    {
        private string manifestDirectory;

        [SetUp]
        public void SetUp()
        {
            manifestDirectory = Path.Combine(Path.GetTempPath(), "metalquestlink-manifest-" + Guid.NewGuid());
            Environment.SetEnvironmentVariable("METALQUESTLINK_MANIFEST_DIR", manifestDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            Environment.SetEnvironmentVariable("METALQUESTLINK_MANIFEST_DIR", null);
            if (Directory.Exists(manifestDirectory))
            {
                Directory.Delete(manifestDirectory, true);
            }
        }

        [Test]
        public void RegisterLayerWritesImplicitManifestForBuiltLibrary()
        {
            var path = OpenXRLayerInstaller.RegisterLayer();
            var json = File.ReadAllText(path);
            StringAssert.Contains(OpenXRLayerInstaller.LayerName, json);
            StringAssert.Contains("libmetalquestlink_openxr_layer.so", json);
            StringAssert.Contains("METALQUESTLINK_ENABLE_API_LAYER", json);
            StringAssert.Contains("XR_EXT_hand_tracking", json);
        }

        [Test]
        public void ReleasePackageContainsNativeLayerManifestAndQuestApk()
        {
            var library = OpenXRLayerInstaller.FindLayerLibrary();
            var apk = OpenXRLayerInstaller.FindDefaultApk();
            Assert.That(library, Is.Not.Null.And.Not.Empty);
            Assert.That(File.Exists(library), Is.True, library);
            Assert.That(apk, Is.Not.Null.And.Not.Empty);
            Assert.That(File.Exists(apk), Is.True, apk);

            var packageRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(library), "..", ".."));
            var bundledManifest = Path.Combine(
                packageRoot, "Native~", "macOS", "XrApiLayer_metalquestlink.json");
            Assert.That(File.Exists(bundledManifest), Is.True, bundledManifest);
            var packageJson = File.ReadAllText(Path.Combine(packageRoot, "package.json"));
            StringAssert.Contains("\"unity\": \"2022.3\"", packageJson);
            StringAssert.Contains("\"com.unity.xr.openxr\": \"1.8.2\"", packageJson);
        }

        [Test]
        public void StandaloneUsesOpenXRAndSimulatorRuntime()
        {
            Assert.That(StandaloneOpenXRConfigurator.IsConfigured(out var status), Is.True, status);
            var runtimeJson = OpenXRLayerInstaller.FindSimulatorRuntimeJson();
            Assert.That(runtimeJson, Is.Not.Null.And.Not.Empty);
            Assert.That(File.Exists(runtimeJson), Is.True, runtimeJson);
        }

        [Test]
        public void StatusSchemaHasStableVersionField()
        {
            var status = JsonUtility.FromJson<LayerStatus>(
                "{\"version\":1,\"connected\":false,\"fps\":72.0," +
                "\"droppedFrames\":3,\"streamWidth\":3840,\"streamHeight\":1920}");
            Assert.That(status.version, Is.EqualTo(1));
            Assert.IsFalse(status.connected);
            Assert.That(status.fps, Is.EqualTo(72.0f));
            Assert.That(status.droppedFrames, Is.EqualTo(3));
            Assert.That(status.streamWidth, Is.EqualTo(3840));
            Assert.That(status.streamHeight, Is.EqualTo(1920));
        }

        [Test]
        public void QuestClientLaunchIncludesRealtimePreviewOptions()
        {
            var settings = MetalQuestLinkSettings.instance;
            var originalPort = settings.port;
            var originalPassthrough = settings.enablePassthroughPreview;
            var originalHands = settings.showTrackedHands;
            var originalWifi = settings.wifiFallbackHost;
            var originalSerial = settings.deviceSerial;
            try
            {
                settings.port = 42424;
                settings.enablePassthroughPreview = true;
                settings.showTrackedHands = true;
                settings.wifiFallbackHost = "192.168.1.20";
                settings.deviceSerial = "QUEST SERIAL";

                var arguments = AdbBridge.BuildStartClientArguments();
                var selectedArguments = AdbBridge.BuildDeviceArguments(arguments);

                StringAssert.Contains("am start -S", arguments);
                StringAssert.Contains(AdbBridge.PackageName + "/" + AdbBridge.ActivityName, arguments);
                StringAssert.Contains("--ei metalquestlink_port 42424", arguments);
                StringAssert.Contains("--ez metalquestlink_passthrough true", arguments);
                StringAssert.Contains("--ez metalquestlink_hand_visualization true", arguments);
                StringAssert.Contains("--es metalquestlink_wifi_host \"192.168.1.20\"", arguments);
                StringAssert.StartsWith("-s \"QUEST SERIAL\" ", selectedArguments);
            }
            finally
            {
                settings.port = originalPort;
                settings.enablePassthroughPreview = originalPassthrough;
                settings.showTrackedHands = originalHands;
                settings.wifiFallbackHost = originalWifi;
                settings.deviceSerial = originalSerial;
            }
        }

        [Test]
        public void StandaloneLoaderOrderingPreservesProjectFallbacks()
        {
            var loaders = StandaloneOpenXRConfigurator.OrderLoaderTypeNames(new[]
            {
                "Vendor.CustomLoader",
                StandaloneOpenXRConfigurator.OpenXRLoaderType,
                "Vendor.CustomLoader",
                "UnityEngine.XR.MockHMD.MockHMDLoader",
            });

            Assert.That(loaders.First(), Is.EqualTo(StandaloneOpenXRConfigurator.OpenXRLoaderType));
            CollectionAssert.AreEqual(new[]
            {
                StandaloneOpenXRConfigurator.OpenXRLoaderType,
                "Vendor.CustomLoader",
                "UnityEngine.XR.MockHMD.MockHMDLoader",
            }, loaders);
        }

        [TestCase("6000.2.5f1", 6000, 2)]
        [TestCase("6000.3.6f1", 6000, 3)]
        [TestCase("2022.3.44f1", 2022, 3)]
        public void UnityVersionParserAcceptsSupportedVersionShape(
            string version, int expectedMajor, int expectedMinor)
        {
            Assert.That(MetalQuestLinkPreflight.TryGetUnityMajorMinor(
                version, out var major, out var minor), Is.True);
            Assert.That(major, Is.EqualTo(expectedMajor));
            Assert.That(minor, Is.EqualTo(expectedMinor));
        }

        [TestCase("2022.3.44f1", true, false)]
        [TestCase("6000.0.50f1", true, false)]
        [TestCase("6000.2.5f1", true, true)]
        [TestCase("2021.3.48f1", false, false)]
        public void UnityCompatibilityUsesBaselineAndVerifiedMatrix(
            string version, bool supported, bool verified)
        {
            var compatibility = MetalQuestLinkPreflight.GetUnityCompatibility(version);
            Assert.That(compatibility.Supported, Is.EqualTo(supported));
            Assert.That(compatibility.Verified, Is.EqualTo(verified));
        }

        [Test]
        public void RelativeConfigurationPathsResolveFromUnityProjectRoot()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var resolved = OpenXRLayerInstaller.ResolveProjectPath("Tools/runtime.json");
            Assert.That(resolved, Is.EqualTo(Path.Combine(projectRoot, "Tools", "runtime.json")));
        }
    }
}
