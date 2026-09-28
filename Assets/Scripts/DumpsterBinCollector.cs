using UnityEngine;

// Attach this to the Dumpster Dive bin prefab itself (not the player).
// While the bin exists - following the player's X/Z each frame for the
// duration of the power-up (see Movement.PlayDumpsterDiveSequence,
// LateUpdate) - it acts as an extension of the player: destroying
// obstacles and collecting coins it touches, as part of the "bulldozing
// through everything" power fantasy.
//
// This can't live in PlayerCollision.cs, because obstacle-hit detection
// there is tied specifically to OnControllerColliderHit, a callback
// exclusive to the CharacterController component on the player's own
// GameObject - a plain Collider on a separate object (this bin) can never
// trigger it. So the bin needs its own trigger-based detection instead.
//
// Requires: a Collider marked "Is Trigger" on this object (or a child of
// it), and a Rigidbody (kinematic is fine) somewhere in this hierarchy -
// Unity trigger events need at least one side of an overlap to have a
// Rigidbody, and the player's own CharacterController doesn't extend
// that requirement to other objects.
public class DumpsterBinCollector : MonoBehaviour
{
    // Assigned explicitly by Movement.cs right after this bin is
    // instantiated (not auto-found here) - the bin is deliberately NOT
    // parented to the player, so there's no hierarchy to search, and a
    // scene-wide lookup would be needlessly fragile/slower than just
    // being handed the reference directly by the one place that spawns this.
    [HideInInspector]
    public PlayerCollision playerCollision;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Obstacle") || other.CompareTag("ObstacleFront"))
        {
            // Destroy the whole obstacle hierarchy, not just whichever
            // collider triggered - same reasoning as Trashbin.cs.
            // ObstacleFront is a child of the main obstacle, so destroying
            // via its own collider's GameObject would leave the parent
            // orphaned in the scene.
            Destroy(other.transform.root.gameObject);
        }
        else if (other.CompareTag("Coin"))
        {
            other.enabled = false; // same double-trigger guard PlayerCollision uses for coins

            if (playerCollision != null)
            {
                playerCollision.CollectCoin();
            }
            else
            {
                Debug.LogWarning("DumpsterBinCollector: playerCollision was never assigned - this coin won't be credited. Check Movement.cs's bin-spawn code.");
            }

            Destroy(other.gameObject);
        }
    }
}