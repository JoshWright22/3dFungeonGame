using UnityEngine;

namespace Delver.Game
{
    /// <summary>
    /// Puts the lights out.
    ///
    /// The dungeon is unlit: no sun, no skybox, no ambient fill. Every photon in a run comes from
    /// something a delver is carrying, which is what makes light a hand-slot cost and makes being
    /// visible the same property as being findable.
    /// </summary>
    [DisallowMultipleComponent]
    public class DungeonAtmosphere : MonoBehaviour
    {
        [Tooltip("Ambient fill. Not quite black - pure zero makes unlit corners read as holes in the world rather than as dark.")]
        [SerializeField] private Color ambient = new Color(0.020f, 0.022f, 0.028f);

        [SerializeField] private bool useFog = true;
        [SerializeField] private Color fogColor = new Color(0.015f, 0.016f, 0.020f);

        [Tooltip("Distance at which the dark becomes total. Roughly the reach of a shout (design doc S8).")]
        [SerializeField] private float fogStart = 6f;
        [SerializeField] private float fogEnd = 34f;

        [Tooltip("Switch off directional lights in the scene. There is no sun underground.")]
        [SerializeField] private bool killDirectionalLights = true;

        private void Awake()
        {
            Apply();
        }

        private void Start()
        {
            // Again after everything has woken up, in case something else set a skybox.
            Apply();
        }

        private void Apply()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = ambient;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.skybox = null;

            // Leave the reflection probe alone. Forcing Custom with no texture leaves URP
            // sampling an undefined cubemap, which tints every surface in the scene.
            RenderSettings.reflectionIntensity = 0f;

            RenderSettings.fog = useFog;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;

            if (killDirectionalLights)
            {
                foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (light.type == LightType.Directional) light.enabled = false;
            }

            // Cameras must clear to black, or the skybox shader paints daylight back in.
            foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                ApplyToCamera(cam);
        }

        /// <summary>
        /// Blacks out one camera's background. Player cameras spawn long after this component's
        /// Start, so they call this for themselves rather than being found by a sweep.
        /// </summary>
        public static void ApplyToCamera(Camera cam)
        {
            if (cam == null) return;

            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
        }
    }
}
