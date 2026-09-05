using UnityEngine;
using Delver.Game;

namespace Delver.Dungeon
{
    /// <summary>
    /// A door leaf that swings out of the way when a delver comes near, and closes behind them.
    ///
    /// Deliberately has no RPC. The dungeon is built locally from a shared seed, so every client
    /// already has this exact door in this exact place, and delver positions are replicated. Every
    /// client therefore evaluates the same proximity test against the same positions and reaches
    /// the same answer - the door is synchronised by construction rather than by messages.
    /// </summary>
    [DisallowMultipleComponent]
    public class DungeonDoor : MonoBehaviour
    {
        [Tooltip("How far a delver has to be before the door opens for them.")]
        public float OpenRadius = 3.2f;

        [Tooltip("Extra distance required before it closes again, so standing in a doorway does not make it flap.")]
        public float CloseHysteresis = 1.2f;

        [Tooltip("How far the leaf swings, in degrees.")]
        public float OpenAngle = 95f;

        public float SwingSpeed = 220f;

        private Quaternion _closed;
        private float _current;
        private bool _isOpen;

        /// <summary>Which way the leaf swings. Set by the generator so doors open away from the corridor.</summary>
        public float SwingSign = 1f;

        private void Start()
        {
            _closed = transform.localRotation;
        }

        private void Update()
        {
            bool wanted = ShouldBeOpen();
            float target = wanted ? OpenAngle * Mathf.Sign(SwingSign) : 0f;

            _current = Mathf.MoveTowards(_current, target, SwingSpeed * Time.deltaTime);
            transform.localRotation = _closed * Quaternion.Euler(0f, _current, 0f);
        }

        private bool ShouldBeOpen()
        {
            // Hysteresis: once open it takes a little more distance to shut, or a delver loitering
            // exactly on the threshold makes the door chatter.
            float radius = _isOpen ? OpenRadius + CloseHysteresis : OpenRadius;
            float sqr = radius * radius;

            var roster = DelverIdentity.All;
            for (int i = 0; i < roster.Count; i++)
            {
                var d = roster[i];
                if (d == null) continue;

                if ((d.transform.position - transform.position).sqrMagnitude <= sqr)
                {
                    _isOpen = true;
                    return true;
                }
            }

            _isOpen = false;
            return false;
        }
    }
}
