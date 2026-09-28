using UnityEngine;

// Trails behind the player at a fixed offset and destroys any Ground,
// Obstacle, or Coin object it touches. This is what actually cleans up
// world geometry after the player has passed it - without this, spawned
// ground/obstacles/coins would accumulate forever and eventually tank
// performance, since nothing else in the spawning system (Spawner.cs)
// ever destroys what it creates.
public class Trashbin : MonoBehaviour
{
    // The player Transform this object trails behind. Must be assigned in
    // the Inspector.
    public Transform player;

    // Fixed offset from the player - should be set to some distance BEHIND
    // the player (negative Z) so this trigger only catches things the
    // player has already passed, not things still ahead of them.
    public Vector3 offset;

    // Follows the player every frame, same pattern as FollowPlayer.cs.
    void LateUpdate()
    {
        if (player != null)
        {
            transform.position = player.position + offset;
        }
    }

    // Fires whenever this object's trigger collider overlaps something
    // tagged Obstacle, ObstacleFront, Ground, Coin, Forest, Tree,
    // Scenery, or PowerUp - i.e. whenever the player has moved far enough
    // past a piece of world geometry that this trailing trigger has
    // caught up to and swept through it. Tree (organic scatter) and
    // Scenery (lot-based, e.g. houses) cover the two zone-based scenery
    // types from Spawner.cs's SpawnForest/SpawnLotStrip; Forest is kept
    // too for any older-tagged prefabs already in the project. PowerUp
    // cleans up any pickups the player ran past without collecting.
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Obstacle") || other.CompareTag("ObstacleFront") || other.CompareTag("Ground") || other.CompareTag("Coin") || other.CompareTag("Forest") || other.CompareTag("Tree") || other.CompareTag("Scenery") || other.CompareTag("PowerUp"))
        {
            // Destroy the whole hierarchy, not just whichever collider
            // happened to trigger first. ObstacleFront is a child of the
            // main obstacle - if ITS collider is what overlaps Trashbin,
            // Destroy(other.gameObject) would only remove the child and
            // leave the parent obstacle orphaned in the scene.
            Destroy(other.transform.root.gameObject);
        }
    }
}