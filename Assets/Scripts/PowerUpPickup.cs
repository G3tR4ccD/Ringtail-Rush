using UnityEngine;

// Which power-up a given pickup grants. Only DumpsterDive is actually
// implemented right now (see PlayerCollision.ApplyPowerUp) - the other
// three are placeholders from the original store design notes, ready to
// wire up later without needing to touch this enum or the pickup system again.
public enum PowerUpType
{
    DumpsterDive,  // ghost mode / invincibility - IMPLEMENTED
    JungMagnet,    // coin magnet - not implemented yet
    TrashTornado,  // giant mode, phase through obstacles - not implemented yet
    BubbleWrap     // temporary one-hit buffer - not implemented yet
}

// Attach to each power-up pickup prefab spawned into the world (see
// Spawner.cs's SpawnPowerUps). Identifies which power-up this is and how
// long its effect lasts - PlayerCollision reads this on pickup and
// applies the matching effect, then destroys the pickup.
public class PowerUpPickup : MonoBehaviour
{
    public PowerUpType powerUpType;
    [Tooltip("How long the granted effect lasts, in seconds.")]
    public float duration = 5f;
}
