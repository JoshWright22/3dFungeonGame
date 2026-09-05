using KinematicCharacterController.Examples;
using Unity.Netcode;
using UnityEngine;

namespace Delver.Movement
{
    /// <summary>
    /// Reads the local player's input and hands it to their own character and camera.
    ///
    /// Owner-gated at spawn: without that, every client would drive every delver in the lobby.
    /// Also refuses input while a menu owns the cursor, so pressing W in the lobby does not walk
    /// your character into a wall behind the UI.
    /// </summary>
    [DisallowMultipleComponent]
    public class DelverInput : NetworkBehaviour
    {
        public DelverCharacterController Character;
        public ExampleCharacterCamera CharacterCamera;

        [Header("Bindings")]
        public KeyCode SprintKey = KeyCode.LeftShift;
        public KeyCode CrouchKey = KeyCode.C;
        public KeyCode JumpKey = KeyCode.Space;

        [Header("Look")]
        public float LookSensitivity = 1f;
        [Tooltip("Suppress look and movement while the cursor is unlocked (lobby, menus, sheets).")]
        public bool RequireCursorLock = true;

        private const string MouseX = "Mouse X";
        private const string MouseY = "Mouse Y";
        private const string Horizontal = "Horizontal";
        private const string Vertical = "Vertical";

        private bool _isOwnerPlayer;

        private void Awake()
        {
            if (Character == null) Character = GetComponent<DelverCharacterController>();
        }

        public override void OnNetworkSpawn()
        {
            _isOwnerPlayer = IsOwner;

            if (!IsOwner)
            {
                enabled = false;
                return;
            }

            if (CharacterCamera != null && Character != null)
            {
                CharacterCamera.SetFollowTransform(Character.transform);

                // The camera must not collide with the character it is attached to.
                CharacterCamera.IgnoredColliders.Clear();
                CharacterCamera.IgnoredColliders.AddRange(Character.GetComponentsInChildren<Collider>());
            }

            LockCursor();
        }

        private void Start()
        {
            // Un-networked play (a scene run without a NetworkManager) still needs to be drivable.
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
                _isOwnerPlayer = true;
        }

        private void Update()
        {
            if (!_isOwnerPlayer || Character == null) return;

            // Clicking back into the game window re-captures the cursor.
            if (Input.GetMouseButtonDown(0) && !CursorIsClaimedByUI())
                LockCursor();

            var inputs = new DelverInputs
            {
                LookRotation = CharacterCamera != null ? CharacterCamera.Transform.rotation : transform.rotation,
            };

            if (AcceptsGameplayInput())
            {
                inputs.MoveForward = Input.GetAxisRaw(Vertical);
                inputs.MoveRight = Input.GetAxisRaw(Horizontal);
                inputs.JumpDown = Input.GetKeyDown(JumpKey);
                inputs.SprintHeld = Input.GetKey(SprintKey);
                inputs.CrouchHeld = Input.GetKey(CrouchKey);
            }

            Character.SetInputs(ref inputs);
        }

        private void LateUpdate()
        {
            if (!_isOwnerPlayer || CharacterCamera == null) return;

            float up = 0f, right = 0f, scroll = 0f;

            if (AcceptsGameplayInput())
            {
                up = Input.GetAxisRaw(MouseY) * LookSensitivity;
                right = Input.GetAxisRaw(MouseX) * LookSensitivity;
#if !UNITY_WEBGL
                scroll = -Input.GetAxis("Mouse ScrollWheel");
#endif
            }

            CharacterCamera.UpdateWithInput(Time.deltaTime, scroll, new Vector3(right, up, 0f));
        }

        private bool AcceptsGameplayInput()
        {
            if (!RequireCursorLock) return true;
            return Cursor.lockState == CursorLockMode.Locked;
        }

        /// <summary>True while a screen has deliberately released the cursor.</summary>
        private static bool CursorIsClaimedByUI()
        {
            var runner = Delver.Game.DungeonRunner.Instance;
            return runner != null && !runner.IsDelving;
        }

        private static void LockCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}
