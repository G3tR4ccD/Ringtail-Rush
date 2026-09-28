using UnityEngine;

// Makes this object (typically the main camera or a camera rig) follow the
// player's Transform every frame, offset by a fixed amount. Runs in
// LateUpdate so it always reads the player's FINAL position for this frame
// (after Movement.cs has already moved them in Update), avoiding a frame of
// lag/jitter that would happen if this ran before the player moved.
public class FollowPlayer : MonoBehaviour
{
    // The player Transform to follow. Must be assigned in the Inspector -
    // if left empty, this script does nothing (see the null check below).
    public Transform player;

    // Fixed offset from the player's position - e.g. (0, 5, -10) for a
    // camera sitting up and behind the player.
    public Vector3 offset;

    void LateUpdate()
    {
        // Guards against a missing/unassigned reference (e.g. if this
        // script ends up in a scene where the player hasn't spawned yet,
        // or the reference was never dragged in the Inspector) so it fails
        // silently instead of throwing a MissingReferenceException every frame.
        if (player == null) return;

        transform.position = player.position + offset;
    }
}