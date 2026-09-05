using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Delver.Appearance
{
    public enum RigScope
    {
        /// <summary>A whole character, as other players see them.</summary>
        FullBody,

        /// <summary>First-person viewmodel: arms and hands only, or you see your own head.</summary>
        ArmsOnly,
    }

    /// <summary>
    /// Builds a Synty modular character from a single integer.
    ///
    /// The part hierarchy is discovered by name at runtime - every mesh under the rig is called
    /// <c>Chr_&lt;Slot&gt;[_Gender]_&lt;NN&gt;</c> - so nothing has to be assigned in the inspector and
    /// nothing breaks when Synty adds parts. Because the whole appearance is a pure function of
    /// the seed, the network replicates one <c>int</c> instead of a part manifest, exactly like
    /// the dungeon.
    /// </summary>
    [DisallowMultipleComponent]
    public class CharacterAppearance : MonoBehaviour
    {
        [Tooltip("Root of the modular character rig. Leave empty to search this GameObject's children.")]
        [SerializeField] private Transform rigRoot;

        [Tooltip("FullBody for the third-person character, ArmsOnly for the first-person viewmodel.")]
        [SerializeField] private RigScope scope = RigScope.FullBody;

        [Tooltip("Rebuild whenever the seed changes in the inspector. Useful for browsing looks.")]
        [SerializeField] private bool livePreview = false;

        [SerializeField] private int previewSeed = 0;

        /// <summary>The seed this character was last built from.</summary>
        public int CurrentSeed { get; private set; }

        /// <summary>Male or female rig chosen by the seed. Drives which gendered parts are eligible.</summary>
        public bool IsFemale { get; private set; }

        // slot key -> the variants found for it, in stable (name-sorted) order.
        private readonly Dictionary<string, List<GameObject>> _slots = new Dictionary<string, List<GameObject>>();
        private readonly List<GameObject> _allParts = new List<GameObject>();
        private bool _indexed;

        // Body slots. Exactly one of each must be active or the character has a hole in it.
        private static readonly string[] RequiredGendered =
        {
            "Torso", "Hips",
            "ArmUpperRight", "ArmUpperLeft", "ArmLowerRight", "ArmLowerLeft",
            "HandRight", "HandLeft", "LegRight", "LegLeft",
        };

        // Attachment slots. Each is an independent coin flip; a delver with no pauldrons is fine.
        private static readonly (string slot, float chance)[] Attachments =
        {
            ("ShoulderAttachRight", 0.40f),
            ("ShoulderAttachLeft",  0.40f),
            ("ElbowAttachRight",    0.25f),
            ("ElbowAttachLeft",     0.25f),
            ("KneeAttachRight",     0.30f),
            ("KneeAttachLeft",      0.30f),
            ("HipsAttachment",      0.35f),
            ("BackAttachment",      0.25f),
        };

        private static readonly int IdColorPrimary        = Shader.PropertyToID("_Color_Primary");
        private static readonly int IdColorSecondary      = Shader.PropertyToID("_Color_Secondary");
        private static readonly int IdColorMetalPrimary   = Shader.PropertyToID("_Color_Metal_Primary");
        private static readonly int IdColorMetalSecondary = Shader.PropertyToID("_Color_Metal_Secondary");
        private static readonly int IdColorLeatherPrimary = Shader.PropertyToID("_Color_Leather_Primary");
        private static readonly int IdColorLeatherSecondary = Shader.PropertyToID("_Color_Leather_Secondary");
        private static readonly int IdColorSkin           = Shader.PropertyToID("_Color_Skin");
        private static readonly int IdColorHair           = Shader.PropertyToID("_Color_Hair");
        private static readonly int IdColorStubble        = Shader.PropertyToID("_Color_Stubble");

        // Muted, torchlit-dungeon appropriate. Nothing here is a saturated hero colour.
        private static readonly Color[] GearPrimary =
        {
            new Color(0.286f, 0.400f, 0.494f), new Color(0.439f, 0.196f, 0.173f),
            new Color(0.353f, 0.380f, 0.271f), new Color(0.682f, 0.439f, 0.220f),
            new Color(0.431f, 0.231f, 0.271f), new Color(0.592f, 0.494f, 0.259f),
            new Color(0.482f, 0.416f, 0.353f), new Color(0.235f, 0.235f, 0.235f),
            new Color(0.231f, 0.431f, 0.416f),
        };

        private static readonly Color[] GearSecondary =
        {
            new Color(0.702f, 0.624f, 0.467f), new Color(0.737f, 0.737f, 0.737f),
            new Color(0.165f, 0.165f, 0.165f), new Color(0.239f, 0.251f, 0.188f),
        };

        private static readonly Color[] MetalPrimary =
        {
            new Color(0.671f, 0.671f, 0.671f), new Color(0.557f, 0.596f, 0.639f),
            new Color(0.557f, 0.624f, 0.600f), new Color(0.631f, 0.620f, 0.557f),
            new Color(0.698f, 0.651f, 0.620f),
        };

        private static readonly Color[] MetalSecondary =
        {
            new Color(0.392f, 0.404f, 0.412f), new Color(0.478f, 0.518f, 0.545f),
            new Color(0.376f, 0.361f, 0.337f), new Color(0.325f, 0.376f, 0.337f),
            new Color(0.400f, 0.404f, 0.357f),
        };

        private static readonly Color[] SkinTones =
        {
            new Color(0.878f, 0.729f, 0.612f), new Color(0.800f, 0.616f, 0.482f),
            new Color(0.639f, 0.463f, 0.345f), new Color(0.478f, 0.333f, 0.243f),
            new Color(0.353f, 0.243f, 0.180f), new Color(0.259f, 0.180f, 0.137f),
            new Color(0.741f, 0.769f, 0.686f), // elf
        };

        private static readonly Color[] HairColors =
        {
            new Color(0.129f, 0.106f, 0.090f), new Color(0.239f, 0.161f, 0.106f),
            new Color(0.416f, 0.278f, 0.161f), new Color(0.588f, 0.443f, 0.263f),
            new Color(0.663f, 0.596f, 0.451f), new Color(0.784f, 0.776f, 0.741f),
            new Color(0.373f, 0.157f, 0.098f),
        };

        private void Awake()
        {
            if (rigRoot == null) rigRoot = transform;
        }

        private void OnValidate()
        {
            if (livePreview && Application.isPlaying)
                Build(previewSeed);
        }

        /// <summary>
        /// Rebuilds the character. Deterministic: the same seed always yields the same delver,
        /// on every machine.
        /// </summary>
        public void Build(int seed)
        {
            EnsureIndexed();
            if (_slots.Count == 0) return;

            CurrentSeed = seed;
            var rng = new System.Random(seed);
            IsFemale = rng.Next(2) == 0;

            // Start from nothing so a rebuild never leaves the previous look's parts behind.
            for (int i = 0; i < _allParts.Count; i++)
                if (_allParts[i] != null) _allParts[i].SetActive(false);

            string g = IsFemale ? "Female" : "Male";

            if (scope == RigScope.ArmsOnly)
            {
                BuildArms(rng, g);
                ApplyColors(rng);
                return;
            }

            foreach (var slot in RequiredGendered)
                ActivateOne(Key(slot, g), rng);

            // Head covering decides whether hair is even possible, so resolve it first.
            bool helmet = rng.NextDouble() < 0.22 && HasSlot(Key("HelmetAttachment", null));
            bool hood = !helmet && rng.NextDouble() < 0.16 && HasSlot(Key("HeadCoverings_No_Hair", null));

            if (helmet) ActivateOne(Key("HelmetAttachment", null), rng);
            if (hood) ActivateOne(Key("HeadCoverings_No_Hair", null), rng);

            // A full helmet needs the head without ears and stray geometry poking through it.
            bool needsPlainHead = helmet || hood;
            if (!needsPlainHead || !ActivateOne(Key("Head_No_Elements_" + g, null), rng))
                ActivateOne(Key("Head", g), rng);

            ActivateOne(Key("Eyebrow", g), rng);

            if (!needsPlainHead && rng.NextDouble() < 0.9)
                ActivateOne(Key("Hair", null), rng);

            if (!IsFemale && !helmet && rng.NextDouble() < 0.45)
                ActivateOne(Key("FacialHair", g), rng);

            foreach (var (slot, chance) in Attachments)
                if (rng.NextDouble() < chance) ActivateOne(Key(slot, null), rng);

            if (rng.NextDouble() < 0.18) ActivateOne(Key("Cape_Attachment", null), rng);

            ApplyColors(rng);
        }

        /// <summary>
        /// The first-person viewmodel. Draws from the same seed as the body so your sleeves match
        /// what the rest of the party can see, but only the parts that belong in front of a camera.
        /// </summary>
        private void BuildArms(System.Random rng, string gender)
        {
            // Consumed in the same order as the full body so both rigs agree on which variant of
            // each shared slot they picked.
            foreach (var slot in RequiredGendered)
            {
                bool isArm = slot.StartsWith("Arm") || slot.StartsWith("Hand");
                if (isArm) ActivateOne(Key(slot, gender), rng);
                else rng.Next();                 // burn the draw so the sequence stays aligned
            }

            foreach (var (slot, chance) in Attachments)
            {
                bool roll = rng.NextDouble() < chance;
                bool visibleInFirstPerson = slot.StartsWith("Shoulder") || slot.StartsWith("Elbow");
                if (roll && visibleInFirstPerson) ActivateOne(Key(slot, null), rng);
                else if (roll) rng.Next();
            }
        }

        private static string Key(string slot, string gender)
        {
            return gender == null ? slot : slot + "|" + gender;
        }

        private bool HasSlot(string key)
        {
            return _slots.TryGetValue(key, out var list) && list.Count > 0;
        }

        private bool ActivateOne(string key, System.Random rng)
        {
            if (!_slots.TryGetValue(key, out var list) || list.Count == 0) return false;

            var pick = list[rng.Next(list.Count)];
            if (pick == null) return false;

            pick.SetActive(true);
            return true;
        }

        /// <summary>
        /// Walks the rig once and buckets every <c>Chr_*</c> mesh by slot. Names look like
        /// <c>Chr_ArmUpperRight_Female_04</c> or <c>Chr_Hair_12</c>; the trailing number is the
        /// variant, an optional Male/Female segment is the gender, and the rest is the slot.
        /// </summary>
        private void EnsureIndexed()
        {
            if (_indexed) return;
            _indexed = true;

            if (rigRoot == null) rigRoot = transform;

            var renderers = rigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var go = renderers[i].gameObject;
                string name = go.name;
                if (!name.StartsWith("Chr_")) continue;

                string body = name.Substring(4);

                // Strip the trailing variant number.
                int lastUnderscore = body.LastIndexOf('_');
                if (lastUnderscore <= 0) continue;

                string tail = body.Substring(lastUnderscore + 1);
                if (!int.TryParse(tail, out _)) continue;

                string slot = body.Substring(0, lastUnderscore);

                string gender = null;
                if (slot.EndsWith("_Male")) { gender = "Male"; slot = slot.Substring(0, slot.Length - 5); }
                else if (slot.EndsWith("_Female")) { gender = "Female"; slot = slot.Substring(0, slot.Length - 7); }

                string key = Key(slot, gender);
                if (!_slots.TryGetValue(key, out var list))
                {
                    list = new List<GameObject>();
                    _slots[key] = list;
                }

                list.Add(go);
                _allParts.Add(go);
            }

            // Stable ordering: GetComponentsInChildren order is hierarchy order, which is stable
            // in a prefab, but sorting by name makes a seed survive someone reordering the rig.
            foreach (var list in _slots.Values)
                list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        }

        private void ApplyColors(System.Random rng)
        {
            Color primary = Pick(GearPrimary, rng);
            Color secondary = Pick(GearSecondary, rng);
            Color metalPrimary = Pick(MetalPrimary, rng);
            Color metalSecondary = Pick(MetalSecondary, rng);
            Color skin = Pick(SkinTones, rng);
            Color hair = Pick(HairColors, rng);

            var block = new MaterialPropertyBlock();
            block.SetColor(IdColorPrimary, primary);
            block.SetColor(IdColorSecondary, secondary);
            block.SetColor(IdColorMetalPrimary, metalPrimary);
            block.SetColor(IdColorMetalSecondary, metalSecondary);
            block.SetColor(IdColorLeatherPrimary, primary * 0.72f);
            block.SetColor(IdColorLeatherSecondary, secondary * 0.72f);
            block.SetColor(IdColorSkin, skin);
            block.SetColor(IdColorHair, hair);
            block.SetColor(IdColorStubble, hair * 0.8f);

            // A property block avoids instantiating a material per delver, so four characters
            // still batch against the one Synty atlas.
            var renderers = rigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].SetPropertyBlock(block);
        }

        private static Color Pick(Color[] set, System.Random rng)
        {
            return set[rng.Next(set.Length)];
        }

        /// <summary>Slots discovered on this rig. Diagnostic - useful from a custom editor.</summary>
        public IEnumerable<KeyValuePair<string, List<GameObject>>> DiscoveredSlots
        {
            get { EnsureIndexed(); return _slots; }
        }
    }
}
