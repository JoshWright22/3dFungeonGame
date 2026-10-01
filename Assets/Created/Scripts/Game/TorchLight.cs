using UnityEngine;

namespace Delver.Game
{
    /// <summary>
    /// A flickering point light for dungeon fixtures - braziers, sconces, lanterns.
    ///
    /// Added by the generator rather than baked into the Synty prop prefabs, so lighting can be
    /// tuned in one place and the props stay untouched asset-store content.
    /// </summary>
    [DisallowMultipleComponent]
    public class TorchLight : MonoBehaviour
    {
        [Header("Flame")]
        public Color FlameColor = new Color(1f, 0.68f, 0.36f);
        public float BaseIntensity = 2.2f;
        public float Range = 11f;
        public Vector3 LocalOffset = new Vector3(0f, 1.9f, 0f);

        [Header("Flicker")]
        [Range(0f, 0.6f)] public float FlickerDepth = 0.22f;
        public float FlickerSpeed = 6.5f;

        [Tooltip("Shadows from static fixtures look good but cost a shadow map each. Off by default: a dungeon has a lot of these.")]
        public bool CastShadows = false;

        [Tooltip("Switched off while the viewer is further than this. A dungeon carries two hundred of these, and a light behind the fog still costs every object in its range a lighting pass.")]
        public float CullDistance = 50f;

        /// <summary>The camera lights are culled against. Falls back to Camera.main.</summary>
        public static Transform Viewer;

        // Staggered so two hundred lights do not all test on the same frame.
        private float _nextCullCheck;

        private Light _light;
        private float _noiseSeed;

        private void Start()
        {
            // Start rather than Awake, so the generator can set the fields after AddComponent.
            _noiseSeed = Random.value * 100f;

            var go = new GameObject("~Flame");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = LocalOffset;

            _light = go.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = FlameColor;
            _light.intensity = BaseIntensity;
            _light.range = Range;
            _light.shadows = CastShadows ? LightShadows.Soft : LightShadows.None;
        }

        private void Update()
        {
            if (_light == null) return;

            if (Time.time >= _nextCullCheck)
            {
                _nextCullCheck = Time.time + 0.25f + Random.value * 0.1f;

                Transform viewer = Viewer;
                if (viewer == null && Camera.main != null) viewer = Camera.main.transform;

                if (viewer != null)
                {
                    float reach = CullDistance + Range;
                    _light.enabled = (viewer.position - transform.position).sqrMagnitude < reach * reach;
                }
            }

            if (!_light.enabled) return;

            float n = Mathf.PerlinNoise(_noiseSeed, Time.time * FlickerSpeed);
            _light.intensity = BaseIntensity * (1f + (n - 0.5f) * 2f * FlickerDepth);
        }
    }
}
