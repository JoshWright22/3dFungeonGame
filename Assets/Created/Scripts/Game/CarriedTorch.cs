using UnityEngine;

namespace Delver.Game
{
    /// <summary>
    /// The delver's light source. Creates its own point light, so nothing has to be wired in the
    /// prefab, and gives it a slow irregular flicker.
    ///
    /// Deliberately short-ranged: the design wants a party that can only see a room at a time,
    /// and wants the dark past the torch edge to be a real place you can walk into.
    /// </summary>
    [DisallowMultipleComponent]
    public class CarriedTorch : MonoBehaviour
    {
        [Header("Flame")]
        [SerializeField] private Color flameColor = new Color(1f, 0.72f, 0.42f);
        [SerializeField] private float baseIntensity = 3.2f;
        [SerializeField] private float range = 16f;

        [Header("Flicker")]
        [Tooltip("How far intensity strays from the base, as a fraction of it.")]
        [Range(0f, 0.6f)][SerializeField] private float flickerDepth = 0.16f;
        [SerializeField] private float flickerSpeed = 7f;

        [Tooltip("Shadows make the torch feel like a torch, but the light sits inside the owner's shadows-only body, which then casts that body's shadow over everything. Needs the body moved to its own layer and excluded from cullingMask before this can go back on.")]
        [SerializeField] private bool castShadows = false;

        [Tooltip("Offset from the camera. Must clear the character capsule (radius 0.35) or the delver stands inside their own shadow.")]
        [SerializeField] private Vector3 localOffset = new Vector3(0.4f, -0.1f, 0.75f);

        [Header("Burn")]
        [Tooltip("Seconds of fuel. A run is longer than a torch. Zero disables burn-down.")]
        [SerializeField] private float burnSeconds = 0f;

        private Light _light;
        private float _noiseSeed;
        private float _fuel;

        /// <summary>0-1 fuel remaining, or 1 when burn-down is disabled.</summary>
        public float FuelNormalised => burnSeconds > 0f ? Mathf.Clamp01(_fuel / burnSeconds) : 1f;

        private void Awake()
        {
            _noiseSeed = Random.value * 100f;
            _fuel = burnSeconds;

            var go = new GameObject("~TorchLight");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localOffset;

            _light = go.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = flameColor;
            _light.intensity = baseIntensity;
            _light.range = range;
            _light.shadows = castShadows ? LightShadows.Soft : LightShadows.None;
            _light.shadowStrength = 0.85f;
        }

        private void Update()
        {
            if (_light == null) return;

            float fuelFactor = 1f;
            if (burnSeconds > 0f)
            {
                _fuel = Mathf.Max(0f, _fuel - Time.deltaTime);

                // Guttering: the last fifth of the fuel dims noticeably before it goes out.
                fuelFactor = Mathf.Clamp01(FuelNormalised / 0.2f);
            }

            // Perlin rather than Random so the flicker wanders instead of strobing.
            float n = Mathf.PerlinNoise(_noiseSeed, Time.time * flickerSpeed);
            float flicker = 1f + (n - 0.5f) * 2f * flickerDepth;

            _light.intensity = baseIntensity * flicker * fuelFactor;
            _light.enabled = fuelFactor > 0.001f;
        }
    }
}
