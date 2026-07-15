using System;
using System.Collections.Generic;
using UnityEngine;

namespace MaQuestLink.QuestClient
{
    public sealed class QuestClientController : MonoBehaviour
    {
        private const int DefaultPort = 42424;

        [SerializeField] private string host = "127.0.0.1";
        [SerializeField] private string wifiFallbackHost = string.Empty;
        [SerializeField] private int port = DefaultPort;
        [SerializeField] private bool diagnosticMode;
        [SerializeField] private bool passthroughMode;
        [SerializeField] private bool handVisualizationMode;
        [SerializeField] private ExternalSurfacePresenter presenter;

        private StreamTransport transport;
        private MediaCodecDecoder decoder;
        private QuestHaptics haptics;
        private HandTrackingVisualizer handVisualizer;
        private HandTrackingInput latestHands;
        private float nextDiagnosticTime;
        private long previousReceived;
        private long previousDecoded;
        private long previousSent;
        private double captureToReceiveMs = -1.0;
        private bool presenterErrorLogged;

        private void Awake()
        {
            Application.targetFrameRate = 72;
            ParseAndroidExtras();
            ResolvePresenter();
            transport = new StreamTransport();
            decoder = new MediaCodecDecoder();
            haptics = new QuestHaptics();
            if (handVisualizationMode)
            {
                handVisualizer = gameObject.AddComponent<HandTrackingVisualizer>();
            }
        }

        private void Start()
        {
            var candidates = new List<string> { host };
            if (!string.IsNullOrWhiteSpace(wifiFallbackHost))
            {
                candidates.Add(wifiFallbackHost);
            }
            transport.Start(candidates, port);
            if (passthroughMode) EnablePassthrough();
            nextDiagnosticTime = Time.unscaledTime + 1.0f;
        }

        private void Update()
        {
            // Update follows the Quest display cadence (72/80/90Hz), satisfying the >=60Hz input path.
            var pose = QuestInputSampler.Sample();
            latestHands = QuestHandSampler.Sample(pose.SampleTimestampNs);
            transport.SubmitLatestHands(latestHands);
            handVisualizer?.UpdateHands(latestHands);
            transport.SubmitLatestPose(pose);
            while (transport.TryDequeueHaptic(out var haptic))
            {
                haptics.Apply(haptic, ClockSynchronizer.NowNs());
            }
            haptics.Tick(ClockSynchronizer.NowNs());
            if (transport.TryDequeueLatestVideo(out var frame))
            {
                Present(frame);
            }
            if (diagnosticMode && Time.unscaledTime >= nextDiagnosticTime)
            {
                EmitDiagnostic();
                nextDiagnosticTime = Time.unscaledTime + 1.0f;
            }
        }

        private void OnDestroy()
        {
            decoder?.Dispose();
            haptics?.Dispose();
            transport?.Dispose();
        }

        private void Present(VideoFrame frame)
        {
            if (!ResolvePresenter())
            {
                return;
            }
            if (((VideoFrameFlags)frame.Flags & VideoFrameFlags.Passthrough) != 0 && !passthroughMode)
            {
                passthroughMode = true;
                EnablePassthrough();
            }
            presenter.ApplyRenderPose(frame);
            captureToReceiveMs = transport.EstimateHostAgeMs(
                frame.CaptureTimestampNs, frame.ReceiveTimestampNs);
            if (!decoder.IsConfigured)
            {
                presenter.ConfigureDimensions((int)frame.Width, (int)frame.Height);
                using (var surface = presenter.TryGetSurface())
                {
                    if (surface == null || !decoder.Configure(
                            frame.Codec, (int)frame.Width, (int)frame.Height, surface))
                    {
                        return;
                    }
                }
            }
            decoder.Queue(frame, transport.ClockOffsetNs, transport.HasClockSync);
        }

        private void EmitDiagnostic()
        {
            var received = transport.ReceivedFrames;
            var decoded = decoder.DecodedFrames;
            var sent = transport.SentPoses;
            var value = new DiagnosticLine
            {
                connected = transport.IsConnected,
                host = transport.ConnectedHost,
                received = received,
                decoded = decoded,
                poses_sent = sent,
                hands_sent = transport.SentHands,
                haptics_received = transport.ReceivedHaptics,
                left_hand_active = latestHands?.LeftActive == true,
                right_hand_active = latestHands?.RightActive == true,
                valid_hand_joints = HandTrackingVisualizer.CountValidJoints(latestHands),
                hand_visualization = handVisualizer != null,
                received_fps = received - previousReceived,
                decode_fps = decoded - previousDecoded,
                pose_hz = sent - previousSent,
                dropped = transport.DroppedFrames,
                low_latency = decoder.LowLatencyRequested,
                reprojection = ResolvePresenter() ? presenter.ProjectionMode : "unavailable",
                clock_synced = transport.HasClockSync,
                clock_rtt_ms = transport.ClockRoundTripMs,
                capture_to_receive_ms = captureToReceiveMs,
                capture_to_decode_ms = decoder.CaptureToDecodeMs,
                passthrough = passthroughMode ? "underlay_uniform_alpha" : "disabled",
            };
            previousReceived = received;
            previousDecoded = decoded;
            previousSent = sent;
            Debug.Log("MAQUESTLINK_DIAGNOSTIC " + JsonUtility.ToJson(value));
        }

        private void ParseAndroidExtras()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var intent = activity.Call<AndroidJavaObject>("getIntent"))
                {
                    diagnosticMode = intent.Call<bool>("getBooleanExtra", "maquestlink_diagnostic", diagnosticMode);
                    host = intent.Call<string>("getStringExtra", "maquestlink_host") ?? host;
                    wifiFallbackHost = intent.Call<string>("getStringExtra", "maquestlink_wifi_host") ?? wifiFallbackHost;
                    port = intent.Call<int>("getIntExtra", "maquestlink_port", port);
                    passthroughMode = intent.Call<bool>("getBooleanExtra", "maquestlink_passthrough", passthroughMode);
                    handVisualizationMode = intent.Call<bool>(
                        "getBooleanExtra", "maquestlink_hand_visualization", handVisualizationMode);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"MaQuestLink Android extras could not be read: {exception.Message}");
            }
#endif
        }

        private void EnablePassthrough()
        {
            if (!ResolvePresenter())
            {
                return;
            }
            presenter.SetPassthroughApproximation(true);
            if (!PassthroughConfigurator.EnableUnderlay())
            {
                Debug.LogWarning("MaQuestLink passthrough underlay could not be enabled");
            }
        }

        private bool ResolvePresenter()
        {
            if (presenter == null)
            {
                presenter = GetComponentInChildren<ExternalSurfacePresenter>(true);
            }
            if (presenter != null)
            {
                return true;
            }
            if (!presenterErrorLogged)
            {
                Debug.LogError(
                    "MaQuestLink ExternalSurfacePresenter is missing from the Quest client scene");
                presenterErrorLogged = true;
            }
            return false;
        }

        [Serializable]
        private sealed class DiagnosticLine
        {
            public bool connected;
            public string host;
            public long received;
            public long decoded;
            public long poses_sent;
            public long hands_sent;
            public long haptics_received;
            public bool left_hand_active;
            public bool right_hand_active;
            public int valid_hand_joints;
            public bool hand_visualization;
            public long received_fps;
            public long decode_fps;
            public long pose_hz;
            public long dropped;
            public bool low_latency;
            public string reprojection;
            public bool clock_synced;
            public double clock_rtt_ms;
            public double capture_to_receive_ms;
            public double capture_to_decode_ms;
            public string passthrough;
        }
    }
}
