using System;
using System.Reflection;
using UnityEngine;

namespace MaQuestLink.QuestClient
{
    /// <summary>Meta OVROverlay の External Surface を横並びステレオ映像の表示先にする。</summary>
    public sealed class ExternalSurfacePresenter : MonoBehaviour
    {
        [SerializeField] private int surfaceWidth = 3360;
        [SerializeField] private int surfaceHeight = 1760;
        [SerializeField] private float distanceMeters = 2.0f;
        [SerializeField] private float widthMeters = 3.2f;
        [SerializeField] private bool worldFixed = true;

        private Component overlay;
        private Type overlayType;
        private bool passthroughApproximation;
        private bool immersiveProjection;

        public int SurfaceWidth => surfaceWidth;
        public int SurfaceHeight => surfaceHeight;
        public bool WorldFixed => worldFixed;
        public string ProjectionMode =>
            immersiveProjection || ImmersiveProjectionFeature.IsAvailable
                ? "immersive_projection"
                : worldFixed ? "world_fixed_quad_fallback" : "head_locked_quad_fallback";
        public bool PassthroughApproximation => passthroughApproximation;

        public void SetPassthroughApproximation(bool enabled)
        {
            passthroughApproximation = enabled;
            if (immersiveProjection)
            {
                return;
            }
            EnsureOverlay();
            SetMember("overridePerLayerColorScaleAndOffset", enabled);
            SetMember("colorScale", enabled
                ? new Vector4(1.0f, 1.0f, 1.0f, Protocol.PassthroughApproximationAlpha)
                : Vector4.one);
            SetMember("colorOffset", Vector4.zero);
        }

        public void ConfigureDimensions(int width, int height)
        {
            surfaceWidth = Math.Max(2, width);
            surfaceHeight = Math.Max(2, height);
            if (!immersiveProjection && overlay != null)
            {
                SetMember("externalSurfaceWidth", surfaceWidth);
                SetMember("externalSurfaceHeight", surfaceHeight);
            }
        }

        public AndroidJavaObject TryGetSurface()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            immersiveProjection |= ImmersiveProjectionFeature.IsAvailable;
            if (immersiveProjection)
            {
                return ImmersiveProjectionFeature.TryCreateSurface(surfaceWidth, surfaceHeight);
            }
            if (overlay == null)
            {
                EnsureOverlay();
            }
            var rawObject = GetMember("externalSurfaceObject");
            if (rawObject is IntPtr pointer && pointer != IntPtr.Zero)
            {
                // OVROverlay exposes the compositor-created android.view.Surface as a JNI jobject.
                return new AndroidJavaObject(pointer);
            }
            return null;
#else
            return null;
#endif
        }

        private void Awake()
        {
            immersiveProjection = ImmersiveProjectionFeature.IsAvailable;
            if (!immersiveProjection)
            {
                EnsureOverlay();
                AttachToHead();
            }
        }

        private void LateUpdate()
        {
            if (!immersiveProjection && !worldFixed && transform.parent == null)
            {
                AttachToHead();
            }
        }

        /// <summary>Macが描画したhead poseへQuadを固定し、Quest compositorのworld-space reprojectionを使う。</summary>
        public bool ApplyRenderPose(VideoFrame frame)
        {
            immersiveProjection |= ImmersiveProjectionFeature.IsAvailable;
            if (immersiveProjection)
            {
                return ImmersiveProjectionFeature.SubmitFrame(frame, passthroughApproximation);
            }
            if (!worldFixed || !TryGetWorldPose(frame, distanceMeters, out var position, out var rotation))
            {
                return false;
            }
            transform.SetParent(null, true);
            transform.SetPositionAndRotation(position, rotation);
            ApplyScale();
            return true;
        }

        public static bool TryGetWorldPose(
            VideoFrame frame, float distance, out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;
            if (frame?.RenderViews == null || frame.RenderViews.Length != 2 || distance <= 0.0f)
            {
                return false;
            }
            var left = frame.RenderViews[0].Pose;
            var right = frame.RenderViews[1].Pose;
            const PoseFlags required = PoseFlags.PositionValid | PoseFlags.OrientationValid;
            if ((left.Flags & required) != required || (right.Flags & required) != required)
            {
                return false;
            }
            var leftPosition = ToUnity(left.Position);
            var rightPosition = ToUnity(right.Position);
            var leftRotation = ToUnity(left.Orientation);
            var rightRotation = ToUnity(right.Orientation);
            rotation = Quaternion.Slerp(leftRotation, rightRotation, 0.5f).normalized;
            var renderHeadPosition = (leftPosition + rightPosition) * 0.5f;
            position = renderHeadPosition + rotation * Vector3.forward * distance;
            return true;
        }

        private void EnsureOverlay()
        {
            if (overlay != null)
            {
                return;
            }
            overlayType = FindType("OVROverlay");
            if (overlayType == null)
            {
                Debug.LogError("MaQuestLink: OVROverlay was not found. Meta XR Core SDK is required.");
                return;
            }
            overlay = GetComponent(overlayType) ?? gameObject.AddComponent(overlayType);
            SetEnumMember("currentOverlayType", "Overlay");
            SetEnumMember("currentOverlayShape", "Quad");
            SetMember("isExternalSurface", true);
            SetMember("isDynamic", true);
            SetMember("externalSurfaceWidth", surfaceWidth);
            SetMember("externalSurfaceHeight", surfaceHeight);

            // Side-by-side source: left eye is the left half, right eye is the right half.
            SetMember("srcRectLeft", new Rect(0.0f, 0.0f, 0.5f, 1.0f));
            SetMember("srcRectRight", new Rect(0.5f, 0.0f, 0.5f, 1.0f));
            SetMember("overrideTextureRectMatrix", true);
        }

        private void AttachToHead()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                return;
            }
            transform.SetParent(camera.transform, false);
            transform.localPosition = new Vector3(0.0f, 0.0f, distanceMeters);
            transform.localRotation = Quaternion.identity;
            ApplyScale();
        }

        private void ApplyScale()
        {
            var height = widthMeters * surfaceHeight / Math.Max(1.0f, surfaceWidth);
            transform.localScale = new Vector3(widthMeters, height, 1.0f);
        }

        private static Vector3 ToUnity(Vector3f value) => new Vector3(value.X, value.Y, -value.Z);

        private static Quaternion ToUnity(Quaternionf value) =>
            new Quaternion(-value.X, -value.Y, value.Z, value.W);

        private object GetMember(string name)
        {
            if (overlay == null || overlayType == null)
            {
                return null;
            }
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var field = overlayType.GetField(name, flags);
            if (field != null)
            {
                return field.GetValue(overlay);
            }
            var property = overlayType.GetProperty(name, flags);
            return property?.CanRead == true ? property.GetValue(overlay) : null;
        }

        private bool SetMember(string name, object value)
        {
            if (overlay == null || overlayType == null)
            {
                return false;
            }
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var field = overlayType.GetField(name, flags);
            if (field != null && field.FieldType.IsInstanceOfType(value))
            {
                field.SetValue(overlay, value);
                return true;
            }
            var property = overlayType.GetProperty(name, flags);
            if (property?.CanWrite == true && property.PropertyType.IsInstanceOfType(value))
            {
                property.SetValue(overlay, value);
                return true;
            }
            return false;
        }

        private void SetEnumMember(string name, string enumValue)
        {
            if (overlay == null || overlayType == null)
            {
                return;
            }
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var field = overlayType.GetField(name, flags);
            if (field != null && field.FieldType.IsEnum)
            {
                field.SetValue(overlay, Enum.Parse(field.FieldType, enumValue));
                return;
            }
            var property = overlayType.GetProperty(name, flags);
            if (property?.CanWrite == true && property.PropertyType.IsEnum)
            {
                property.SetValue(overlay, Enum.Parse(property.PropertyType, enumValue));
            }
        }

        private static Type FindType(string name)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(name, false);
                if (type != null)
                {
                    return type;
                }
            }
            return null;
        }
    }
}
