using System;
using System.Reflection;
using UnityEngine;

namespace MetalQuestLink.QuestClient
{
    public static class PassthroughConfigurator
    {
        public static bool EnableUnderlay()
        {
            var managerType = FindType("OVRManager");
            var layerType = FindType("OVRPassthroughLayer");
            if (managerType == null || layerType == null) return false;
            var manager = UnityEngine.Object.FindObjectOfType(managerType) as Component;
            if (manager == null) return false;
            SetMember(manager, managerType, "isInsightPassthroughEnabled", true);
            var layer = manager.GetComponent(layerType) ?? manager.gameObject.AddComponent(layerType);
            SetMember(layer, layerType, "hidden", false);
            SetEnumMember(layer, layerType, "overlayType", "Underlay");
            return true;
        }

        private static void SetMember(object target, Type type, string name, object value)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var field = type.GetField(name, flags);
            if (field != null && field.FieldType.IsInstanceOfType(value)) field.SetValue(target, value);
            var property = type.GetProperty(name, flags);
            if (property?.CanWrite == true && property.PropertyType.IsInstanceOfType(value))
                property.SetValue(target, value);
        }

        private static void SetEnumMember(object target, Type type, string name, string value)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var field = type.GetField(name, flags);
            if (field != null && field.FieldType.IsEnum)
                field.SetValue(target, Enum.Parse(field.FieldType, value));
        }

        private static Type FindType(string name)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(name, false);
                if (type != null) return type;
            }
            return null;
        }
    }
}
