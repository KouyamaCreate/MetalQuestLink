using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.XR.OpenXR.Features;
#endif

namespace MaQuestLink.QuestClient
{
#if UNITY_EDITOR
    [OpenXRFeature(
        UiName = "MaQuestLink Immersive Projection",
        BuildTargetGroups = new[] { BuildTargetGroup.Android },
        Company = "MaQuestLink",
        Desc = "Submits the decoded stereo stream as an OpenXR projection layer.",
        OpenxrExtensionStrings =
            "XR_KHR_android_surface_swapchain XR_KHR_composition_layer_color_scale_bias",
        Version = "0.1.0",
        FeatureId = FeatureId)]
#endif
    public sealed class ImmersiveProjectionFeature : OpenXRFeature
    {
        public const string FeatureId = "com.maquestlink.openxr.feature.immersiveprojection";
        private const string NativeLibrary = "maquestlink_projection";
        private static ImmersiveProjectionFeature current;
        private bool sessionReady;

        public static bool IsAvailable => current != null && current.enabled && current.sessionReady;

        protected override IntPtr HookGetInstanceProcAddr(IntPtr function)
        {
            return NativeHookGetInstanceProcAddr(function);
        }

        protected override bool OnInstanceCreate(ulong instance)
        {
            if (!OpenXRRuntime.IsExtensionEnabled("XR_KHR_android_surface_swapchain"))
            {
                Debug.LogError(
                    "MaQuestLink immersive projection requires XR_KHR_android_surface_swapchain");
                return false;
            }
            current = this;
            NativeSetInstance(instance);
            Debug.Log("MaQuestLink immersive projection OpenXR feature enabled");
            return true;
        }

        protected override void OnSessionCreate(ulong session)
        {
            NativeSetSession(session);
            sessionReady = true;
        }

        protected override void OnAppSpaceChange(ulong appSpace)
        {
            NativeSetAppSpace(appSpace);
        }

        protected override void OnSessionDestroy(ulong session)
        {
            sessionReady = false;
            NativeResetSession();
        }

        protected override void OnInstanceDestroy(ulong instance)
        {
            if (current == this) current = null;
        }

        public static AndroidJavaObject TryCreateSurface(int width, int height)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!IsAvailable || width < 2 || height < 2 || (width & 1) != 0) return null;
            var result = NativeCreateSurface((uint)width, (uint)height, out var surface);
            if (result != 0 || surface == IntPtr.Zero)
            {
                Debug.LogError($"MaQuestLink immersive projection surface failed: XrResult={result}");
                return null;
            }
            return new AndroidJavaObject(surface);
#else
            return null;
#endif
        }

        public static bool SubmitFrame(VideoFrame frame, bool passthrough)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!IsAvailable || !TryBuildViews(frame, out var views)) return false;
            NativeUpdateFrame(views, (uint)views.Length, passthrough);
            return true;
#else
            return false;
#endif
        }

        public static bool TryBuildViews(VideoFrame frame, out NativeStreamView[] views)
        {
            views = null;
            if (frame?.RenderViews == null || frame.RenderViews.Length != 2) return false;
            const PoseFlags required = PoseFlags.PositionValid | PoseFlags.OrientationValid;
            var result = new NativeStreamView[2];
            for (var eye = 0; eye < result.Length; eye++)
            {
                var source = frame.RenderViews[eye];
                if ((source.Pose.Flags & required) != required) return false;
                result[eye] = new NativeStreamView
                {
                    OrientationX = source.Pose.Orientation.X,
                    OrientationY = source.Pose.Orientation.Y,
                    OrientationZ = source.Pose.Orientation.Z,
                    OrientationW = source.Pose.Orientation.W,
                    PositionX = source.Pose.Position.X,
                    PositionY = source.Pose.Position.Y,
                    PositionZ = source.Pose.Position.Z,
                    AngleLeft = source.Fov.AngleLeft,
                    AngleRight = source.Fov.AngleRight,
                    AngleUp = source.Fov.AngleUp,
                    AngleDown = source.Fov.AngleDown,
                };
            }
            views = result;
            return true;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NativeStreamView
        {
            public float OrientationX;
            public float OrientationY;
            public float OrientationZ;
            public float OrientationW;
            public float PositionX;
            public float PositionY;
            public float PositionZ;
            public float AngleLeft;
            public float AngleRight;
            public float AngleUp;
            public float AngleDown;
        }

        [DllImport(NativeLibrary, EntryPoint = "maquestlink_projection_hook_get_instance_proc_addr")]
        private static extern IntPtr NativeHookGetInstanceProcAddr(IntPtr function);

        [DllImport(NativeLibrary, EntryPoint = "maquestlink_projection_set_instance")]
        private static extern void NativeSetInstance(ulong instance);

        [DllImport(NativeLibrary, EntryPoint = "maquestlink_projection_set_session")]
        private static extern void NativeSetSession(ulong session);

        [DllImport(NativeLibrary, EntryPoint = "maquestlink_projection_set_app_space")]
        private static extern void NativeSetAppSpace(ulong appSpace);

        [DllImport(NativeLibrary, EntryPoint = "maquestlink_projection_create_surface")]
        private static extern int NativeCreateSurface(uint width, uint height, out IntPtr surface);

        [DllImport(NativeLibrary, EntryPoint = "maquestlink_projection_update_frame")]
        private static extern void NativeUpdateFrame(
            [In] NativeStreamView[] views, uint viewCount, [MarshalAs(UnmanagedType.I1)] bool passthrough);

        [DllImport(NativeLibrary, EntryPoint = "maquestlink_projection_reset_session")]
        private static extern void NativeResetSession();
    }
}
