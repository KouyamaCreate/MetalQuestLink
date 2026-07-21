using UnityEngine;

namespace MetalQuestLink.Sample
{
    public sealed class DemoCaptureMotion : MonoBehaviour
    {
        public Transform cyanCube;
        public Transform purpleSphere;
        public Light cyanLight;
        public Light purpleLight;

        private Vector3 cubeOrigin;
        private Vector3 sphereOrigin;

        private void Start()
        {
            cubeOrigin = cyanCube.position;
            sphereOrigin = purpleSphere.position;
        }

        private void Update()
        {
            var t = Time.time;
            cyanCube.rotation = Quaternion.Euler(14f + Mathf.Sin(t * 1.2f) * 6f, 28f + t * 38f, -8f);
            cyanCube.position = cubeOrigin + Vector3.up * (Mathf.Sin(t * 1.7f) * 0.13f);

            var pulse = 1f + Mathf.Sin(t * 2.1f + 0.8f) * 0.055f;
            purpleSphere.localScale = Vector3.one * pulse;
            purpleSphere.position = sphereOrigin + new Vector3(0f, Mathf.Sin(t * 1.45f + 1.1f) * 0.16f, 0f);

            cyanLight.intensity = 8.5f + Mathf.Sin(t * 1.8f) * 1.2f;
            purpleLight.intensity = 8f + Mathf.Sin(t * 1.65f + 1.4f) * 1.1f;
        }
    }
}
