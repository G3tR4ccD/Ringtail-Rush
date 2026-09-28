using UnityEngine;

// Hit registration used to live here, triggered by this object's own
// trigger volume. That depended on the trigger being positioned with
// exactly the right gap in front of the obstacle's solid collider - if the
// solid collider stopped the player's CharacterController right at its
// surface first (very possible, since that's normal Unity collision
// resolution), the player's collider would never actually overlap this
// trigger, so OnTriggerEnter never fired and the hit fell through to a
// glancing/graze registration instead of the head-on hit it should have
// been.
//
// Head-on vs. glancing hits are now classified directly from the surface
// normal in PlayerCollision.OnControllerColliderHit, which is guaranteed
// to fire on every solid contact regardless of trigger positioning. This
// script (and the "ObstacleFront" tag/trigger it's on) is no longer used
// for hit detection - it can stay in the scene harmlessly (e.g. Trashbin
// still cleans it up via its tag), or be removed if it's not needed for
// anything else.
public class FrontTrigger : MonoBehaviour
{
}