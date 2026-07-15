using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace MaQuestLink.Editor
{
    public readonly struct AdbResult
    {
        public AdbResult(int exitCode, string output)
        {
            ExitCode = exitCode;
            Output = output;
        }

        public int ExitCode { get; }
        public string Output { get; }
        public bool Success => ExitCode == 0;
    }

    public static class AdbBridge
    {
        public const string PackageName = "com.maquestlink.questclient";
        public const string ActivityName = "com.unity3d.player.UnityPlayerGameActivity";

        public static AdbResult Reverse()
        {
            var port = MaQuestLinkSettings.instance.port;
            return Run(BuildDeviceArguments($"reverse tcp:{port} tcp:{port}"));
        }

        public static AdbResult Install()
        {
            var apk = OpenXRLayerInstaller.FindDefaultApk();
            if (string.IsNullOrEmpty(apk) || !File.Exists(apk))
            {
                return new AdbResult(2, $"APK not found: {apk}");
            }
            return Run(BuildDeviceArguments($"install -r {Quote(apk)}"), 180000);
        }

        public static AdbResult StartClient()
        {
            var reverse = Reverse();
            if (!reverse.Success)
            {
                return reverse;
            }
            return Run(BuildDeviceArguments(BuildStartClientArguments()), 60000);
        }

        public static AdbResult StartClientDetached()
        {
            var reverse = Reverse();
            if (!reverse.Success)
            {
                return reverse;
            }
            var adb = FindAdb();
            if (string.IsNullOrEmpty(adb))
            {
                return new AdbResult(127, "adb was not found. Set its path in Window > MaQuestLink.");
            }
            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = adb,
                    Arguments = BuildDeviceArguments(BuildStartClientArguments()),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                process?.Dispose();
                return process == null
                    ? new AdbResult(1, "adb process could not be started")
                    : new AdbResult(0, "Quest client launch scheduled");
            }
            catch (Exception exception)
            {
                return new AdbResult(1, exception.Message);
            }
        }

        public static string BuildStartClientArguments()
        {
            var settings = MaQuestLinkSettings.instance;
            var arguments = $"shell am start -S -n {PackageName}/{ActivityName} " +
                   $"--ei maquestlink_port {settings.port} " +
                   $"--ez maquestlink_passthrough {BooleanArgument(settings.enablePassthroughPreview)} " +
                   $"--ez maquestlink_hand_visualization {BooleanArgument(settings.showTrackedHands)}";
            if (!string.IsNullOrWhiteSpace(settings.wifiFallbackHost))
            {
                arguments += " --es maquestlink_wifi_host " + Quote(settings.wifiFallbackHost.Trim());
            }
            return arguments;
        }

        public static AdbResult Run(string arguments, int timeoutMilliseconds = 15000)
        {
            var adb = FindAdb();
            if (string.IsNullOrEmpty(adb))
            {
                return new AdbResult(127, "adb was not found. Set its path in Window > MaQuestLink.");
            }
            return RunWithExecutable(adb, arguments, timeoutMilliseconds);
        }

        private static AdbResult RunWithExecutable(
            string adb, string arguments, int timeoutMilliseconds)
        {
            try
            {
                var output = new StringBuilder();
                var outputLock = new object();
                var start = new ProcessStartInfo
                {
                    FileName = adb,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using (var process = Process.Start(start))
                {
                    if (process == null)
                    {
                        return new AdbResult(1, "adb process could not be started");
                    }
                    process.OutputDataReceived += (_, eventArgs) =>
                    {
                        if (eventArgs.Data == null) return;
                        lock (outputLock) output.AppendLine(eventArgs.Data);
                    };
                    process.ErrorDataReceived += (_, eventArgs) =>
                    {
                        if (eventArgs.Data == null) return;
                        lock (outputLock) output.AppendLine(eventArgs.Data);
                    };
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    if (!process.WaitForExit(timeoutMilliseconds))
                    {
                        process.Kill();
                        process.WaitForExit(2000);
                        return new AdbResult(124, "adb command timed out");
                    }
                    process.WaitForExit();
                    lock (outputLock)
                    {
                        return new AdbResult(process.ExitCode, output.ToString().Trim());
                    }
                }
            }
            catch (Exception exception)
            {
                return new AdbResult(1, exception.Message);
            }
        }

        public static string FindAdb()
        {
            var configured = MaQuestLinkSettings.instance.adbPath;
            var configuredPath = OpenXRLayerInstaller.ResolveProjectPath(configured);
            if (!string.IsNullOrEmpty(configuredPath) && File.Exists(configuredPath))
            {
                return configuredPath;
            }
            var environmentPath = Environment.GetEnvironmentVariable("ADB");
            if (!string.IsNullOrEmpty(environmentPath) && File.Exists(environmentPath))
            {
                return environmentPath;
            }
            var androidToolsType = Type.GetType("UnityEditor.Android.AndroidExternalToolsSettings, UnityEditor.Android.Extensions");
            var sdkProperty = androidToolsType?.GetProperty("sdkRootPath", BindingFlags.Public | BindingFlags.Static);
            var sdkRoot = sdkProperty?.GetValue(null) as string;
            if (!string.IsNullOrEmpty(sdkRoot))
            {
                var bundled = Path.Combine(sdkRoot, "platform-tools", "adb");
                if (File.Exists(bundled))
                {
                    return bundled;
                }
            }
            foreach (var candidate in new[] { "/opt/homebrew/bin/adb", "/usr/local/bin/adb" })
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var directory in path.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory)) continue;
                var candidate = Path.Combine(directory, "adb");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            return null;
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        public static string BuildDeviceArguments(string arguments)
        {
            var serial = MaQuestLinkSettings.instance.deviceSerial;
            return string.IsNullOrWhiteSpace(serial)
                ? arguments
                : $"-s {Quote(serial.Trim())} {arguments}";
        }

        private static string BooleanArgument(bool value)
        {
            return value ? "true" : "false";
        }
    }
}
