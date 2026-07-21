using System;
using UnityEngine;

namespace MetalQuestLink.QuestClient
{
    public sealed class QuestHaptics : IDisposable
    {
        private ulong leftStopNs;
        private ulong rightStopNs;

        public static float NormalizeFrequency(float frequencyHz)
        {
            if (frequencyHz <= 0.0f) return 1.0f;
            return Mathf.Clamp01(frequencyHz / 320.0f);
        }

        public void Apply(HapticCommand command, ulong nowNs)
        {
            if (command == null) return;
            var apply = command.Action == HapticAction.Apply;
            SetVibration(command.Side, apply ? NormalizeFrequency(command.FrequencyHz) : 0.0f,
                apply ? Mathf.Clamp01(command.Amplitude) : 0.0f);
            var stop = apply && command.DurationNs > 0
                ? nowNs + command.DurationNs
                : 0ul;
            if (command.Side == HandSide.Right) rightStopNs = stop;
            else leftStopNs = stop;
        }

        public void Tick(ulong nowNs)
        {
            if (leftStopNs != 0 && nowNs >= leftStopNs)
            {
                SetVibration(HandSide.Left, 0, 0);
                leftStopNs = 0;
            }
            if (rightStopNs != 0 && nowNs >= rightStopNs)
            {
                SetVibration(HandSide.Right, 0, 0);
                rightStopNs = 0;
            }
        }

        public void Dispose()
        {
            SetVibration(HandSide.Left, 0, 0);
            SetVibration(HandSide.Right, 0, 0);
            leftStopNs = 0;
            rightStopNs = 0;
        }

        private static void SetVibration(HandSide side, float frequency, float amplitude)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            OVRInput.SetControllerVibration(frequency, amplitude,
                side == HandSide.Right ? OVRInput.Controller.RTouch : OVRInput.Controller.LTouch);
#endif
        }
    }
}
