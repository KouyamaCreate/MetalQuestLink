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

        private Component overlay;
        private Type overlayType;

        public int SurfaceWidth => surfaceWidth;
        public int SurfaceHeight => surfaceHeight;

        public void ConfigureDimensions(int width, int height)
        {
            surfaceWidth = Math.Max(2, width);
            surfaceHeight = Math.Max(2, height);
            if (overlay != null)
            {
                SetMember("externalSurfaceWidth", surfaceWidth);
                SetMember("externalSurfaceHeight", surfaceHeight);
            }
        }

        public AndroidJavaObject TryGetSurface()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
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
            EnsureOverlay();
            AttachToHead();
        }

        private void LateUpdate()
        {
            if (transform.parent == null)
            {
                AttachToHead();
            }
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
            var height = widthMeters * surfaceHeight / Math.Max(1.0f, surfaceWidth);
            transform.localScale = new Vector3(widthMeters, height, 1.0f);
        }

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
