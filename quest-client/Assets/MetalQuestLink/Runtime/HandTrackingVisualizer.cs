using System.Collections.Generic;
using UnityEngine;

namespace MetalQuestLink.QuestClient
{
    /// <summary>実機診断用に左右26関節を球とboneで描画する軽量hand skeleton。</summary>
    public sealed class HandTrackingVisualizer : MonoBehaviour
    {
        private static readonly int[,] Bones =
        {
            { 1, 0 },
            { 1, 2 }, { 2, 3 }, { 3, 4 }, { 4, 5 },
            { 1, 6 }, { 6, 7 }, { 7, 8 }, { 8, 9 }, { 9, 10 },
            { 1, 11 }, { 11, 12 }, { 12, 13 }, { 13, 14 }, { 14, 15 },
            { 1, 16 }, { 16, 17 }, { 17, 18 }, { 18, 19 }, { 19, 20 },
            { 1, 21 }, { 21, 22 }, { 22, 23 }, { 23, 24 }, { 24, 25 },
        };

        private HandView left;
        private HandView right;
        private Mesh sphereMesh;
        private Mesh cylinderMesh;

        public bool LeftVisible => left?.Root.activeSelf == true;
        public bool RightVisible => right?.Root.activeSelf == true;
        public int VisibleJointCount { get; private set; }

        public void UpdateHands(HandTrackingInput hands)
        {
            EnsureInitialized();
            VisibleJointCount = UpdateHand(left, hands?.LeftActive == true ? hands.LeftJoints : null) +
                                UpdateHand(right, hands?.RightActive == true ? hands.RightJoints : null);
        }

        public static int CountValidJoints(HandTrackingInput hands)
        {
            if (hands == null) return 0;
            return CountValid(hands.LeftActive ? hands.LeftJoints : null) +
                   CountValid(hands.RightActive ? hands.RightJoints : null);
        }

        private void EnsureInitialized()
        {
            if (left != null) return;
            sphereMesh = CreateSphereMesh();
            cylinderMesh = CreateCylinderMesh();
            left = CreateHand("LeftHandSkeleton", new Color(0.1f, 1.0f, 0.35f, 1.0f));
            right = CreateHand("RightHandSkeleton", new Color(0.1f, 0.65f, 1.0f, 1.0f));
        }

        private HandView CreateHand(string name, Color color)
        {
            var root = new GameObject(name);
            root.transform.SetParent(transform, false);
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            var material = shader == null ? null : new Material(shader) { color = color };
            var joints = new Transform[Protocol.HandJointCount];
            for (var index = 0; index < joints.Length; index++)
            {
                joints[index] = CreatePrimitive(
                    sphereMesh, $"Joint{index:00}", root.transform, material);
            }
            var bones = new Transform[Bones.GetLength(0)];
            for (var index = 0; index < bones.Length; index++)
            {
                bones[index] = CreatePrimitive(
                    cylinderMesh, $"Bone{index:00}", root.transform, material);
            }
            root.SetActive(false);
            return new HandView(root, joints, bones, material);
        }

        private static Transform CreatePrimitive(
            Mesh mesh, string name, Transform parent, Material material)
        {
            var value = new GameObject(name);
            value.transform.SetParent(parent, false);
            value.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = value.AddComponent<MeshRenderer>();
            if (material != null) renderer.sharedMaterial = material;
            return value.transform;
        }

        private static Mesh CreateSphereMesh()
        {
            const int latitudeCount = 6;
            const int longitudeCount = 8;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (var latitude = 0; latitude <= latitudeCount; latitude++)
            {
                var angle = Mathf.PI * latitude / latitudeCount;
                var y = Mathf.Cos(angle);
                var ring = Mathf.Sin(angle);
                for (var longitude = 0; longitude <= longitudeCount; longitude++)
                {
                    var around = 2.0f * Mathf.PI * longitude / longitudeCount;
                    vertices.Add(new Vector3(ring * Mathf.Cos(around), y, ring * Mathf.Sin(around)));
                }
            }
            for (var latitude = 0; latitude < latitudeCount; latitude++)
            {
                for (var longitude = 0; longitude < longitudeCount; longitude++)
                {
                    var current = latitude * (longitudeCount + 1) + longitude;
                    var next = current + longitudeCount + 1;
                    triangles.Add(current);
                    triangles.Add(next);
                    triangles.Add(current + 1);
                    triangles.Add(current + 1);
                    triangles.Add(next);
                    triangles.Add(next + 1);
                }
            }
            return CreateMesh("MetalQuestLinkHandJoint", vertices, triangles);
        }

        private static Mesh CreateCylinderMesh()
        {
            const int segmentCount = 8;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (var segment = 0; segment <= segmentCount; segment++)
            {
                var angle = 2.0f * Mathf.PI * segment / segmentCount;
                var radial = new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle));
                vertices.Add(radial + Vector3.down);
                vertices.Add(radial + Vector3.up);
            }
            for (var segment = 0; segment < segmentCount; segment++)
            {
                var current = segment * 2;
                triangles.Add(current);
                triangles.Add(current + 1);
                triangles.Add(current + 2);
                triangles.Add(current + 2);
                triangles.Add(current + 1);
                triangles.Add(current + 3);
            }
            return CreateMesh("MetalQuestLinkHandBone", vertices, triangles);
        }

        private static Mesh CreateMesh(string name, List<Vector3> vertices, List<int> triangles)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static int UpdateHand(HandView view, HandJointState[] states)
        {
            var active = states != null && states.Length == Protocol.HandJointCount;
            view.Root.SetActive(active);
            if (!active) return 0;

            var valid = 0;
            for (var index = 0; index < view.Joints.Length; index++)
            {
                var state = states[index];
                var visible = TryGetPosition(state, out var position);
                view.Joints[index].gameObject.SetActive(visible);
                if (!visible) continue;
                view.Joints[index].position = position;
                view.Joints[index].rotation = ToUnity(state.Pose.Orientation);
                var radius = Mathf.Clamp(state.Radius, 0.004f, 0.015f);
                view.Joints[index].localScale = Vector3.one * radius * 2.0f;
                valid++;
            }

            for (var index = 0; index < view.Bones.Length; index++)
            {
                var start = view.Joints[Bones[index, 0]];
                var end = view.Joints[Bones[index, 1]];
                var visible = start.gameObject.activeSelf && end.gameObject.activeSelf;
                view.Bones[index].gameObject.SetActive(visible);
                if (!visible) continue;
                var offset = end.position - start.position;
                var length = offset.magnitude;
                view.Bones[index].position = start.position + offset * 0.5f;
                view.Bones[index].up = length > 0.0001f ? offset / length : Vector3.up;
                view.Bones[index].localScale = new Vector3(0.0035f, length * 0.5f, 0.0035f);
            }
            return valid;
        }

        private static int CountValid(HandJointState[] states)
        {
            if (states == null || states.Length != Protocol.HandJointCount) return 0;
            var count = 0;
            foreach (var state in states)
            {
                if ((state.Pose.Flags & PoseFlags.PositionValid) != 0) count++;
            }
            return count;
        }

        private static bool TryGetPosition(HandJointState state, out Vector3 position)
        {
            position = new Vector3(state.Pose.Position.X, state.Pose.Position.Y, -state.Pose.Position.Z);
            return (state.Pose.Flags & PoseFlags.PositionValid) != 0;
        }

        private static Quaternion ToUnity(Quaternionf value) =>
            new Quaternion(-value.X, -value.Y, value.Z, value.W);

        private void OnDestroy()
        {
            DestroyHand(left);
            DestroyHand(right);
            DestroyObject(sphereMesh);
            DestroyObject(cylinderMesh);
        }

        private static void DestroyHand(HandView view)
        {
            if (view == null) return;
            DestroyObject(view.Material);
            DestroyObject(view.Root);
        }

        private static void DestroyObject(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }

        private sealed class HandView
        {
            public readonly GameObject Root;
            public readonly Transform[] Joints;
            public readonly Transform[] Bones;
            public readonly Material Material;

            public HandView(GameObject root, Transform[] joints, Transform[] bones, Material material)
            {
                Root = root;
                Joints = joints;
                Bones = bones;
                Material = material;
            }
        }
    }
}
