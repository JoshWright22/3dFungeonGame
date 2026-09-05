using System.Collections.Generic;
using KinematicCharacterController;
using UnityEngine;

namespace Delver.Movement
{
    public struct DelverInputs
    {
        public float MoveForward;
        public float MoveRight;
        public Quaternion LookRotation;
        public bool JumpDown;
        public bool SprintHeld;
        public bool CrouchHeld;
    }

    /// <summary>
    /// The delver's locomotion. A Kinematic Character Controller motor driven with a walk/sprint/
    /// crouch model tuned for corridors rather than the KCC sample's open-field platforming.
    ///
    /// Two things make it this game's controller rather than a generic one: sprint burns stamina
    /// that only recovers when you slow down, and what you are carrying slows you down - so the
    /// party's decision about whether to arm itself or fill its hands with loot (design doc S5)
    /// is felt in the movement, not just in an inventory screen.
    /// </summary>
    [DisallowMultipleComponent]
    public class DelverCharacterController : MonoBehaviour, ICharacterController
    {
        public KinematicCharacterMotor Motor;

        [Header("Ground movement")]
        [Tooltip("Base walking speed before ability scores and carry load are applied.")]
        public float WalkSpeed = 4.2f;
        public float SprintSpeed = 7.0f;
        public float CrouchSpeed = 2.0f;
        [Tooltip("How sharply velocity converges on the target. Higher is more responsive and less slippery.")]
        public float GroundSharpness = 16f;

        [Header("Air movement")]
        public float AirMoveSpeed = 5.5f;
        public float AirAcceleration = 22f;
        public float AirDrag = 0.12f;

        [Header("Jumping")]
        public float JumpSpeed = 7.2f;
        [Tooltip("Grace period after walking off a ledge during which a jump still counts. Corridors have a lot of ledges.")]
        public float CoyoteTime = 0.12f;
        [Tooltip("A jump pressed this long before landing still fires on touchdown.")]
        public float JumpBufferTime = 0.14f;

        [Header("Crouching")]
        public float StandingHeight = 1.8f;
        public float CrouchedHeight = 1.05f;
        public float CapsuleRadius = 0.35f;

        [Header("Stamina")]
        public float MaxStamina = 100f;
        [Tooltip("Stamina spent per second while sprinting.")]
        public float SprintDrain = 22f;
        [Tooltip("Stamina recovered per second while not sprinting.")]
        public float StaminaRecovery = 16f;
        [Tooltip("Delay after sprinting before recovery starts.")]
        public float RecoveryDelay = 0.8f;
        [Tooltip("Stamina needed to start a sprint. Prevents stutter-sprinting at zero.")]
        public float SprintUnlockThreshold = 15f;

        [Header("Load")]
        [Tooltip("Speed multiplier when carrying the maximum the delver can lift.")]
        [Range(0.2f, 1f)] public float FullyLadenSpeedMultiplier = 0.62f;

        [Header("Physics")]
        public Vector3 Gravity = new Vector3(0f, -26f, 0f);
        public List<Collider> IgnoredColliders = new List<Collider>();

        [Header("View")]
        [Tooltip("Where the camera sits. Leave empty and one is created at eye height. Must NOT be the character root - that is at the feet, and pointing a first-person camera at it puts your eyes on the floor.")]
        public Transform CameraFollowPoint;

        [Tooltip("Eye height above the feet when standing.")]
        public float StandingEyeHeight = 1.62f;

        [Tooltip("Eye height above the feet when crouched.")]
        public float CrouchedEyeHeight = 0.95f;

        [Tooltip("How quickly the view drops and rises when crouching.")]
        public float EyeHeightSharpness = 12f;

        [Header("Hooks")]
        public Animator ThirdPersonAnimator;
        public Animator FirstPersonAnimator;

        /// <summary>0-1. Drives the stamina bar and gates sprinting.</summary>
        public float StaminaNormalised => MaxStamina > 0f ? _stamina / MaxStamina : 0f;

        public bool IsSprinting { get; private set; }
        public bool IsCrouching { get; private set; }
        public bool IsGrounded => Motor != null && Motor.GroundingStatus.IsStableOnGround;

        /// <summary>
        /// 0 = empty handed, 1 = at the delver's carry limit. Set by the inventory; read here so
        /// carrying loot costs speed.
        /// </summary>
        public float CarryLoad { get; set; }

        private PlayerStats _stats;

        private Vector3 _moveInput;
        private Vector3 _lookInput;
        private bool _jumpRequested;
        private bool _jumpConsumed;
        private float _timeSinceJumpRequested = Mathf.Infinity;
        private float _timeSinceGrounded;
        private bool _wantsCrouch;
        private bool _wantsSprint;
        private float _stamina;
        private float _timeSinceSprintEnded;
        private bool _sprintLocked;
        private Collider[] _probedColliders = new Collider[8];

        private void Awake()
        {
            _stamina = MaxStamina;

            if (Motor == null) Motor = GetComponent<KinematicCharacterMotor>();
            if (Motor != null) Motor.CharacterController = this;

            _stats = GetComponent<PlayerStats>();
            if (_stats == null) _stats = GetComponentInParent<PlayerStats>();

            EnsureCameraFollowPoint();
            ApplyCapsule(StandingHeight);
        }

        /// <summary>
        /// Guarantees a follow point at eye height. Without this the camera ends up on whatever
        /// transform it was handed - and the character root is at the feet.
        /// </summary>
        private void EnsureCameraFollowPoint()
        {
            if (CameraFollowPoint == null)
            {
                var go = new GameObject("~EyePoint");
                go.transform.SetParent(transform, false);
                CameraFollowPoint = go.transform;
            }

            CameraFollowPoint.localPosition = new Vector3(0f, StandingEyeHeight, 0f);
        }

        private void UpdateEyeHeight(float deltaTime)
        {
            if (CameraFollowPoint == null) return;

            float target = IsCrouching ? CrouchedEyeHeight : StandingEyeHeight;
            Vector3 local = CameraFollowPoint.localPosition;

            local.y = Mathf.Lerp(local.y, target, 1f - Mathf.Exp(-EyeHeightSharpness * deltaTime));
            CameraFollowPoint.localPosition = local;
        }

        /// <summary>Feeds a frame of player intent. Called from the owner's input component only.</summary>
        public void SetInputs(ref DelverInputs inputs)
        {
            // Flatten the camera onto the character's ground plane so looking up does not shorten
            // the forward move vector.
            Vector3 moveInput = Vector3.ClampMagnitude(new Vector3(inputs.MoveRight, 0f, inputs.MoveForward), 1f);

            Vector3 planarForward = Vector3.ProjectOnPlane(inputs.LookRotation * Vector3.forward, Motor.CharacterUp).normalized;
            if (planarForward.sqrMagnitude < 0.0001f)
                planarForward = Vector3.ProjectOnPlane(inputs.LookRotation * Vector3.up, Motor.CharacterUp).normalized;

            Quaternion planarRotation = Quaternion.LookRotation(planarForward, Motor.CharacterUp);

            _moveInput = planarRotation * moveInput;
            _lookInput = planarForward;

            if (inputs.JumpDown)
            {
                _timeSinceJumpRequested = 0f;
                _jumpRequested = true;
            }

            _wantsCrouch = inputs.CrouchHeld;
            _wantsSprint = inputs.SprintHeld;
        }

        // ------------------------------------------------------------------ speed model

        /// <summary>
        /// Target ground speed for this frame: the stance's base speed, modified by Dexterity and
        /// by how loaded down the delver is.
        /// </summary>
        private float CurrentTargetSpeed()
        {
            float baseSpeed = IsCrouching ? CrouchSpeed
                            : IsSprinting ? SprintSpeed
                            : WalkSpeed;

            return baseSpeed * DexterityMultiplier() * LoadMultiplier();
        }

        /// <summary>
        /// Dexterity nudges speed by roughly +/-12% across the 3-18 range. Deliberately small:
        /// a bad roll should be felt, not be a different game.
        /// </summary>
        private float DexterityMultiplier()
        {
            if (_stats == null || !_stats.IsSpawned) return 1f;

            int dex = _stats.maxDex;
            if (dex <= 0) return 1f;

            return 1f + Mathf.Clamp((dex - 10.5f) * 0.016f, -0.12f, 0.12f);
        }

        private float LoadMultiplier()
        {
            return Mathf.Lerp(1f, FullyLadenSpeedMultiplier, Mathf.Clamp01(CarryLoad));
        }

        private void UpdateStamina(float deltaTime)
        {
            bool moving = _moveInput.sqrMagnitude > 0.01f;
            bool wantsToSprint = _wantsSprint && moving && !IsCrouching && IsGrounded;

            // Once stamina bottoms out you cannot sprint again until you have recovered a chunk,
            // so running out is a real setback rather than a one-frame stutter.
            if (_stamina <= 0.01f) _sprintLocked = true;
            if (_sprintLocked && _stamina >= SprintUnlockThreshold) _sprintLocked = false;

            IsSprinting = wantsToSprint && !_sprintLocked;

            if (IsSprinting)
            {
                _stamina = Mathf.Max(0f, _stamina - SprintDrain * deltaTime);
                _timeSinceSprintEnded = 0f;
            }
            else
            {
                _timeSinceSprintEnded += deltaTime;
                if (_timeSinceSprintEnded >= RecoveryDelay)
                    _stamina = Mathf.Min(MaxStamina, _stamina + StaminaRecovery * deltaTime);
            }
        }

        // ------------------------------------------------------------------ ICharacterController

        public void BeforeCharacterUpdate(float deltaTime)
        {
            UpdateStamina(deltaTime);
            HandleCrouchIntent();
        }

        public void UpdateRotation(ref Quaternion currentRotation, float deltaTime)
        {
            // First person: the body simply faces where the camera looks. No orientation smoothing,
            // because any lag between look and body direction reads as input lag.
            if (_lookInput.sqrMagnitude > 0f)
                currentRotation = Quaternion.LookRotation(_lookInput, Motor.CharacterUp);
        }

        public void UpdateVelocity(ref Vector3 currentVelocity, float deltaTime)
        {
            if (Motor.GroundingStatus.IsStableOnGround)
            {
                float speed = currentVelocity.magnitude;

                // Reorient existing velocity onto the ground plane so running down a ramp does not
                // launch the delver.
                Vector3 effectiveGroundNormal = Motor.GroundingStatus.GroundNormal;
                currentVelocity = Motor.GetDirectionTangentToSurface(currentVelocity, effectiveGroundNormal) * speed;

                Vector3 inputRight = Vector3.Cross(_moveInput, Motor.CharacterUp);
                Vector3 targetDirection = Vector3.Cross(effectiveGroundNormal, inputRight).normalized * _moveInput.magnitude;
                Vector3 targetVelocity = targetDirection * CurrentTargetSpeed();

                currentVelocity = Vector3.Lerp(currentVelocity, targetVelocity, 1f - Mathf.Exp(-GroundSharpness * deltaTime));
            }
            else
            {
                if (_moveInput.sqrMagnitude > 0f)
                {
                    Vector3 targetVelocity = _moveInput * AirMoveSpeed * LoadMultiplier();

                    // Keep air control from fighting the ground when pressed into a slope.
                    if (Motor.GroundingStatus.FoundAnyGround)
                    {
                        Vector3 perpendicular = Vector3.Cross(Motor.CharacterUp, Vector3.Cross(Motor.CharacterUp, Motor.GroundingStatus.GroundNormal));
                        targetVelocity = Vector3.ProjectOnPlane(targetVelocity, perpendicular);
                    }

                    Vector3 delta = Vector3.ProjectOnPlane(targetVelocity - currentVelocity, Gravity);
                    currentVelocity += delta * AirAcceleration * deltaTime;
                }

                currentVelocity += Gravity * deltaTime;
                currentVelocity *= 1f / (1f + AirDrag * deltaTime);
            }

            HandleJump(ref currentVelocity, deltaTime);
        }

        private void HandleJump(ref Vector3 currentVelocity, float deltaTime)
        {
            _timeSinceJumpRequested += deltaTime;

            if (!_jumpRequested) return;

            // Buffered press expired.
            if (_timeSinceJumpRequested > JumpBufferTime)
            {
                _jumpRequested = false;
                return;
            }

            bool canJump = !_jumpConsumed &&
                           (Motor.GroundingStatus.IsStableOnGround || _timeSinceGrounded <= CoyoteTime);

            if (!canJump) return;
            if (IsCrouching) return;

            Vector3 up = Motor.CharacterUp;

            // Unstick before launching, or the motor snaps the character back to the ground.
            Motor.ForceUnground();
            currentVelocity += up * JumpSpeed - Vector3.Project(currentVelocity, up);

            _jumpRequested = false;
            _jumpConsumed = true;

            if (ThirdPersonAnimator != null) ThirdPersonAnimator.SetTrigger("Jump");
            if (FirstPersonAnimator != null) FirstPersonAnimator.SetTrigger("Jump");
        }

        public void AfterCharacterUpdate(float deltaTime)
        {
            if (Motor.GroundingStatus.IsStableOnGround)
            {
                _timeSinceGrounded = 0f;
                _jumpConsumed = false;
            }
            else
            {
                _timeSinceGrounded += deltaTime;
            }

            UpdateEyeHeight(deltaTime);
            DriveAnimators();
        }

        public void PostGroundingUpdate(float deltaTime)
        {
        }

        private void HandleCrouchIntent()
        {
            if (_wantsCrouch && !IsCrouching)
            {
                IsCrouching = true;
                ApplyCapsule(CrouchedHeight);
                return;
            }

            if (!_wantsCrouch && IsCrouching)
            {
                // Only stand up if there is actually headroom - releasing crouch under a low
                // ceiling must not push the delver through it.
                ApplyCapsule(StandingHeight);

                if (Motor.CharacterOverlap(Motor.TransientPosition, Motor.TransientRotation,
                        _probedColliders, Motor.CollidableLayers, QueryTriggerInteraction.Ignore) > 0)
                {
                    ApplyCapsule(CrouchedHeight);
                }
                else
                {
                    IsCrouching = false;
                }
            }
        }

        private void ApplyCapsule(float height)
        {
            if (Motor == null) return;

            Motor.SetCapsuleDimensions(CapsuleRadius, height, height * 0.5f);
        }

        private void DriveAnimators()
        {
            Vector3 planar = Vector3.ProjectOnPlane(Motor.Velocity, Motor.CharacterUp);
            float speed = planar.magnitude;

            SetAnimator(ThirdPersonAnimator, speed);
            SetAnimator(FirstPersonAnimator, speed);
        }

        private void SetAnimator(Animator animator, float speed)
        {
            if (animator == null || !animator.isActiveAndEnabled) return;

            animator.SetFloat("Speed", speed);
            animator.SetFloat("MotionSpeed", 1f);
            animator.SetBool("Grounded", Motor.GroundingStatus.IsStableOnGround);
        }

        public bool IsColliderValidForCollisions(Collider coll)
        {
            return !IgnoredColliders.Contains(coll);
        }

        public void OnGroundHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, ref HitStabilityReport hitStabilityReport)
        {
        }

        public void OnMovementHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, ref HitStabilityReport hitStabilityReport)
        {
        }

        public void ProcessHitStabilityReport(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint,
            Vector3 atCharacterPosition, Quaternion atCharacterRotation, ref HitStabilityReport hitStabilityReport)
        {
        }

        public void OnDiscreteCollisionDetected(Collider hitCollider)
        {
        }
    }
}
