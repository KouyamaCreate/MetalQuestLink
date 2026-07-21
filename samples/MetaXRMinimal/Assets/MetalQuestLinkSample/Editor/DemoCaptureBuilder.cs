using System.IO;
using MetalQuestLink.Sample;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace MetalQuestLink.Sample.Editor
{
    public static class DemoCaptureBuilder
    {
        public const string ScenePath = "Assets/MetalQuestLinkSample/Scenes/DemoCapture.unity";

        [MenuItem("MetalQuestLink/Prepare Demo Capture Scene")]
        public static void Prepare()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.055f, 0.07f, 0.11f);

            var cameraObject = new GameObject("Demo Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.008f, 0.014f, 0.028f);
            camera.fieldOfView = 37f;
            camera.nearClipPlane = 0.05f;
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 1.65f, -5.8f);
            cameraObject.transform.LookAt(new Vector3(0f, 0.85f, 0f));

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Graphite Floor";
            floor.transform.localScale = new Vector3(1.25f, 1f, 0.78f);
            floor.GetComponent<Renderer>().sharedMaterial = Material("Graphite", new Color(0.025f, 0.036f, 0.065f), Color.black, 0.72f, 0.14f);

            var plinth = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plinth.name = "Floating Plinth";
            plinth.transform.position = new Vector3(0f, 0.16f, 0.08f);
            plinth.transform.localScale = new Vector3(3.7f, 0.32f, 1.5f);
            plinth.GetComponent<Renderer>().sharedMaterial = Material("Plinth", new Color(0.035f, 0.075f, 0.12f), new Color(0.03f, 0.16f, 0.23f), 0.82f, 0.22f);

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Cyan Live Cube";
            cube.transform.position = new Vector3(-1.05f, 1.04f, 0f);
            cube.transform.localScale = Vector3.one * 1.08f;
            cube.transform.rotation = Quaternion.Euler(14f, 28f, -8f);
            cube.GetComponent<Renderer>().sharedMaterial = Material("Cyan Glass", new Color(0.08f, 0.68f, 0.86f), new Color(0.05f, 0.75f, 1f) * 2.6f, 0.74f, 0.18f);

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Purple Live Sphere";
            sphere.transform.position = new Vector3(1.05f, 1.12f, 0f);
            sphere.GetComponent<Renderer>().sharedMaterial = Material("Purple Glass", new Color(0.54f, 0.22f, 0.95f), new Color(0.58f, 0.16f, 1f) * 2.25f, 0.76f, 0.12f);

            var centerLine = GameObject.CreatePrimitive(PrimitiveType.Cube);
            centerLine.name = "Signal Divider";
            centerLine.transform.position = new Vector3(0f, 0.96f, 0.26f);
            centerLine.transform.localScale = new Vector3(0.025f, 1.62f, 0.025f);
            centerLine.GetComponent<Renderer>().sharedMaterial = Material("Signal", new Color(0.22f, 0.9f, 1f), new Color(0.14f, 0.8f, 1f) * 3.2f, 0.9f, 0f);

            var key = AreaLight("Cyan Key", new Vector3(-3.2f, 4.4f, -2.4f), new Color(0.2f, 0.82f, 1f), 980f, 3.4f);
            key.transform.LookAt(cube.transform);
            var fill = AreaLight("Purple Key", new Vector3(3.1f, 3.8f, -1.5f), new Color(0.62f, 0.28f, 1f), 850f, 3f);
            fill.transform.LookAt(sphere.transform);

            var cyanPoint = PointLight("Cyan Pulse", new Vector3(-1.15f, 1.2f, -0.4f), new Color(0.08f, 0.74f, 1f));
            var purplePoint = PointLight("Purple Pulse", new Vector3(1.18f, 1.25f, -0.35f), new Color(0.62f, 0.18f, 1f));

            var motion = new GameObject("Realtime Motion").AddComponent<DemoCaptureMotion>();
            motion.cyanCube = cube.transform;
            motion.purpleSphere = sphere.transform;
            motion.cyanLight = cyanPoint;
            motion.purpleLight = purplePoint;

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new IOException($"Failed to save {ScenePath}");
            }
            Selection.activeObject = cameraObject;
            Debug.Log($"METALQUESTLINK_DEMO_CAPTURE_READY scene={ScenePath}");
        }

        private static Material Material(string name, Color baseColor, Color emission, float metallic, float smoothness)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) {name = name, color = baseColor};
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (emission.maxColorComponent > 0f)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission);
            }
            return material;
        }

        private static Light AreaLight(string name, Vector3 position, Color color, float intensity, float size)
        {
            var light = new GameObject(name).AddComponent<Light>();
            light.type = LightType.Rectangle;
            light.transform.position = position;
            light.color = color;
            light.intensity = intensity;
            light.range = 10f;
            light.areaSize = new Vector2(size, size);
            return light;
        }

        private static Light PointLight(string name, Vector3 position, Color color)
        {
            var light = new GameObject(name).AddComponent<Light>();
            light.type = LightType.Point;
            light.transform.position = position;
            light.color = color;
            light.intensity = 8f;
            light.range = 4.5f;
            light.shadows = LightShadows.Soft;
            return light;
        }
    }
}
