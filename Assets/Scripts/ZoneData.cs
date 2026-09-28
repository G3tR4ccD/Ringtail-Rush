using UnityEngine;

// One zone's full set of visuals: ground, obstacles, and side scenery.
// GroundSpawner swaps its working prefab arrays to point at whichever
// zone is currently active - a hard cut, no blending, always landing
// exactly on a ground-piece boundary (pieces are always a whole
// spawndistance long, so this can never happen mid-piece).
//
// Deliberately kept in its OWN file, separate from GroundSpawner.cs -
// sharing a file with a MonoBehaviour caused Unity's scene serialization
// to misidentify this plain (non-MonoBehaviour) class, throwing
// "'ZoneData' is missing the class attribute 'ExtensionOfNativeClass'"
// and auto-"fixing" (silently altering) references on the GameObject
// GroundSpawner is attached to. One class per file avoids this.
[System.Serializable]
public class ZoneData
{
    [Tooltip("For your own reference in the Inspector - not used in code.")]
    public string zoneName;

    [Header("Ground & Obstacles")]
    public GameObject[] groundPrefab;
    public GameObject[] obstaclePrefab;
    [Tooltip("Base Y offset applied to every obstacle in this zone, on top of any per-prefab offset below.")]
    public float obstacleYOffset = 0f;
    [Tooltip("Per-obstacle Y offset, matched by index to Obstacle Prefab above. Leave shorter than Obstacle Prefab, or empty, to use 0 for any prefab without an explicit entry.")]
    public float[] obstacleYOffsets;

    [Header("Special Ground Handling")]
    [Tooltip("The specific ground prefab in THIS zone that needs the pre-emptive flip.")]
    public GameObject specialGroundPrefab;
    [Tooltip("Forward/backward nudge to close the gap left by the 180-degree flip - specific to this zone's special ground model.")]
    public float flippedZOffset = 36f;
    [Tooltip("Name of the child object holding the visual mesh (not the trigger) on this zone's special ground prefab.")]
    public string modelChildName = "Model";

    [Header("Special Row (wide obstacle)")]
    [Tooltip("A single prefab spanning all 3 lanes, spawned on a dedicated random roll (see Special Row Chance on GroundSpawner) instead of combining separate per-lane obstacles. Leave empty to disable special rows for this zone entirely.")]
    public GameObject wideObstaclePrefab;
    [Tooltip("Y offset for Wide Obstacle Prefab when it spawns.")]
    public float wideObstacleYOffset = 0f;

    [Header("Side Scenery")]
    [Tooltip("If checked, scenery is placed in fixed, non-overlapping lots (for buildings, which vary too much in size for simple distance-based spacing to handle well). If unchecked, scenery is randomly scattered with minimum-distance spacing (for organic scenery like trees).")]
    public bool useLotBasedScenery;

    [Tooltip("Organic scatter prefabs (trees etc). Used when Use Lot Based Scenery is UNCHECKED.")]
    public GameObject[] scatterPrefab;

    [Tooltip("Lot-based prefabs (houses etc - build any props like mailboxes/hedges/cars directly into these prefabs, rather than placing them separately). Used when Use Lot Based Scenery is CHECKED.")]
    public GameObject[] lotPrefab;
    [Tooltip("Z-distance between each lot's center - e.g. 12 units apart. Combined with the 60-unit ground piece length, this determines how many lots fit per side per piece (60 / 12 = 5). Deterministic, no randomness - lots are placed on an exact grid.")]
    public float lotLength = 12f;
    [Tooltip("Exact X distance from center for lot-based placement, mirrored per side (left side = -this, right side = +this). Fixed, not a random range.")]
    public float lotX = 18f;
    [Tooltip("Exact Y offset for lot-based prefabs. Fixed, not randomized.")]
    public float lotY = 0.24f;
}