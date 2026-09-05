using KinematicCharacterController;
using UnityEngine;

/// <summary>
/// Catches anyone who falls out of the dungeon and puts them back on their feet.
///
/// It used to teleport to the world origin, which is the corner of the dungeon grid and sits
/// below the floor - so falling through once left you stuck under the map for the rest of the
/// run. Now it returns you to the generator's entry point.
/// </summary>
public class Fallbox : MonoBehaviour
{
    [Tooltip("Where to put a fallen delver. Leave empty to use the dungeon's party spawn point.")]
    [SerializeField] private Transform respawnPoint;

    [Tooltip("Fallback used when there is no generator and no respawn point set.")]
    [SerializeField] private Vector3 fallbackPosition = new Vector3(0f, 5f, 0f);

    private Generator3D generator;

    private void Awake()
    {
        generator = FindAnyObjectByType<Generator3D>();
    }

    private Vector3 RecoveryPoint()
    {
        if (respawnPoint != null) return respawnPoint.position;

        // The generator only has a spawn point once it has actually built something.
        if (generator == null) generator = FindAnyObjectByType<Generator3D>();
        if (generator != null && generator.PartySpawnPoint != Vector3.zero)
            return generator.PartySpawnPoint;

        return fallbackPosition;
    }

    private void OnTriggerEnter(Collider other)
    {
        var motor = other.GetComponentInParent<KinematicCharacterMotor>();
        if (motor == null) return;

        Vector3 target = RecoveryPoint();

        // Going through the motor is the only safe way to move a KCC character; setting the
        // transform directly gets overwritten on the next character update.
        motor.SetPosition(target);
        motor.BaseVelocity = Vector3.zero;

        Debug.Log($"[Fallbox] Recovered {other.name} to {target}.", this);
    }
}
