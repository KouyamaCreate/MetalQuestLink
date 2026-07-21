using System;
using UnityEngine;

namespace MetalQuestLink.QuestClient
{
    public sealed class MediaCodecDecoder : IDisposable
    {
        private AndroidJavaObject codec;
        private AndroidJavaObject bufferInfo;
        private bool configured;
        private long decodedFrames;

        public bool IsConfigured => configured;
        public long DecodedFrames => decodedFrames;
        public bool LowLatencyRequested { get; private set; }
        public double CaptureToDecodeMs { get; private set; } = -1.0;

        public bool Configure(VideoCodec videoCodec, int width, int height, AndroidJavaObject surface)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Dispose();
            if (surface == null || width <= 0 || height <= 0)
            {
                return false;
            }
            var mime = videoCodec == VideoCodec.Hevc ? "video/hevc" : "video/avc";
            try
            {
                using (var mediaFormatClass = new AndroidJavaClass("android.media.MediaFormat"))
                using (var codecClass = new AndroidJavaClass("android.media.MediaCodec"))
                using (var format = mediaFormatClass.CallStatic<AndroidJavaObject>(
                    "createVideoFormat", mime, width, height))
                {
                    format.Call("setInteger", "low-latency", 1);
                    format.Call("setInteger", "priority", 0);
                    codec = codecClass.CallStatic<AndroidJavaObject>("createDecoderByType", mime);
                    codec.Call("configure", format, surface, null, 0);
                    codec.Call("start");
                    bufferInfo = new AndroidJavaObject("android.media.MediaCodec$BufferInfo");
                    configured = true;
                    LowLatencyRequested = true;
                    return true;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"MetalQuestLink MediaCodec low-latency configure failed: {exception.Message}");
                Dispose();
                return ConfigureFallback(mime, width, height, surface);
            }
#else
            return false;
#endif
        }

        public bool Queue(VideoFrame frame, long hostMinusClientNs = 0, bool clockSynchronized = false)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!configured || codec == null || frame == null || frame.EncodedData == null)
            {
                return false;
            }
            try
            {
                var inputIndex = codec.Call<int>("dequeueInputBuffer", 0L);
                if (inputIndex >= 0)
                {
                    using (var input = codec.Call<AndroidJavaObject>("getInputBuffer", inputIndex))
                    {
                        input.Call<AndroidJavaObject>("clear");
                        input.Call<AndroidJavaObject>("put", frame.EncodedData);
                    }
                    codec.Call("queueInputBuffer", inputIndex, 0, frame.EncodedData.Length,
                        unchecked((long)(frame.CaptureTimestampNs / 1000ul)), 0);
                }
                DrainOutput(hostMinusClientNs, clockSynchronized);
                return inputIndex >= 0;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"MetalQuestLink MediaCodec queue failed: {exception.Message}");
                return false;
            }
#else
            return false;
#endif
        }

        public void Dispose()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (codec != null)
            {
                try { codec.Call("stop"); } catch (Exception) { }
                try { codec.Call("release"); } catch (Exception) { }
                codec.Dispose();
                codec = null;
            }
            bufferInfo?.Dispose();
            bufferInfo = null;
#endif
            configured = false;
            LowLatencyRequested = false;
            CaptureToDecodeMs = -1.0;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private bool ConfigureFallback(string mime, int width, int height, AndroidJavaObject surface)
        {
            try
            {
                using (var mediaFormatClass = new AndroidJavaClass("android.media.MediaFormat"))
                using (var codecClass = new AndroidJavaClass("android.media.MediaCodec"))
                using (var format = mediaFormatClass.CallStatic<AndroidJavaObject>(
                    "createVideoFormat", mime, width, height))
                {
                    codec = codecClass.CallStatic<AndroidJavaObject>("createDecoderByType", mime);
                    codec.Call("configure", format, surface, null, 0);
                    codec.Call("start");
                    bufferInfo = new AndroidJavaObject("android.media.MediaCodec$BufferInfo");
                    configured = true;
                    LowLatencyRequested = false;
                    return true;
                }
            }
            catch (Exception exception)
            {
                Debug.LogError($"MetalQuestLink MediaCodec configure failed: {exception}");
                Dispose();
                return false;
            }
        }

        private void DrainOutput(long hostMinusClientNs, bool clockSynchronized)
        {
            while (configured)
            {
                var outputIndex = codec.Call<int>("dequeueOutputBuffer", bufferInfo, 0L);
                if (outputIndex < 0)
                {
                    break;
                }
                var presentationUs = bufferInfo.Get<long>("presentationTimeUs");
                codec.Call("releaseOutputBuffer", outputIndex, true);
                if (clockSynchronized)
                {
                    var captureInClientNs = presentationUs * 1000L - hostMinusClientNs;
                    var elapsedNs = unchecked((long)ClockSynchronizer.NowNs() - captureInClientNs);
                    CaptureToDecodeMs = Math.Max(0.0, elapsedNs / 1_000_000.0);
                }
                decodedFrames++;
            }
        }
#endif
    }
}
