using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.XR;

namespace MaQuestLink.QuestClient
{
    public static class QuestInputSampler
    {
        private static readonly List<InputDevice> Devices = new List<InputDevice>();
        private static readonly InputFeatureUsage<bool> PrimaryTouch = new InputFeatureUsage<bool>("PrimaryTouch");
        private static readonly InputFeatureUsage<bool> SecondaryTouch = new InputFeatureUsage<bool>("SecondaryTouch");
        private static readonly InputFeatureUsage<bool> ThumbstickTouch = new InputFeatureUsage<bool>("Thumbrest");
        private static readonly InputFeatureUsage<bool> TriggerTouch = new InputFeatureUsage<bool>("IndexTouch");
        private static readonly double NanosecondsPerTick = 1_000_000_000.0 / Stopwatch.Frequency;

        public static PoseInput Sample()
        {
            return new PoseInput
            {
                SampleTimestampNs = unchecked((ulong)(Stopwatch.GetTimestamp() * NanosecondsPerTick)),
                Head = SamplePose(InputDeviceCharacteristics.HeadMounted),
                Left = SampleController(InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller),
                Right = SampleController(InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller),
            };
        }

        private static PoseState SamplePose(InputDeviceCharacteristics characteristics)
        {
            var device = FindDevice(characteristics);
            var result = new PoseState { Orientation = new Quaternionf { W = 1.0f } };
            if (!device.isValid)
            {
                return result;
            }
            if (device.TryGetFeatureValue(CommonUsages.devicePosition, out var position))
            {
                result.Position = ToOpenXr(position);
                result.Flags |= PoseFlags.PositionValid | PoseFlags.PositionTracked;
            }
            if (device.TryGetFeatureValue(CommonUsages.deviceRotation, out var rotation))
            {
                result.Orientation = ToOpenXr(rotation);
                result.Flags |= PoseFlags.OrientationValid | PoseFlags.OrientationTracked;
            }
            return result;
        }

        private static ControllerState SampleController(InputDeviceCharacteristics characteristics)
        {
            var device = FindDevice(characteristics);
            var state = new ControllerState
            {
                Pose = SamplePose(characteristics),
            };
            if (!device.isValid)
            {
                return state;
            }

            SetButton(device, CommonUsages.primaryButton, ControllerButtons.PrimaryButton, ref state.Buttons);
            SetButton(device, CommonUsages.secondaryButton, ControllerButtons.SecondaryButton, ref state.Buttons);
            SetButton(device, CommonUsages.primary2DAxisClick, ControllerButtons.ThumbstickButton, ref state.Buttons);
            SetButton(device, CommonUsages.menuButton, ControllerButtons.MenuButton, ref state.Buttons);
            SetButton(device, PrimaryTouch, ControllerButtons.PrimaryTouch, ref state.Buttons);
            SetButton(device, SecondaryTouch, ControllerButtons.SecondaryTouch, ref state.Buttons);
            SetButton(device, ThumbstickTouch, ControllerButtons.ThumbstickTouch, ref state.Buttons);
            SetButton(device, TriggerTouch, ControllerButtons.TriggerTouch, ref state.Buttons);

            if (device.TryGetFeatureValue(CommonUsages.primary2DAxis, out var thumbstick))
            {
                state.Thumbstick = new Vector2f { X = thumbstick.x, Y = thumbstick.y };
            }
            device.TryGetFeatureValue(CommonUsages.trigger, out state.Trigger);
            device.TryGetFeatureValue(CommonUsages.grip, out state.Grip);
            return state;
        }

        private static InputDevice FindDevice(InputDeviceCharacteristics characteristics)
        {
            Devices.Clear();
            InputDevices.GetDevicesWithCharacteristics(characteristics, Devices);
            return Devices.Count > 0 ? Devices[0] : default;
        }

        private static void SetButton(
            InputDevice device,
            InputFeatureUsage<bool> usage,
            ControllerButtons flag,
            ref ControllerButtons buttons)
        {
            if (device.TryGetFeatureValue(usage, out var pressed) && pressed)
            {
                buttons |= flag;
            }
        }

        private static Vector3f ToOpenXr(Vector3 value)
        {
            return new Vector3f { X = value.x, Y = value.y, Z = -value.z };
        }

        private static Quaternionf ToOpenXr(Quaternion value)
        {
            return new Quaternionf { X = -value.x, Y = -value.y, Z = value.z, W = value.w };
        }
    }
}
