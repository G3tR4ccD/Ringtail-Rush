using System.Collections.Generic;
using UnityEngine;

// (ZoneData - one zone's full set of ground/obstacle/scenery prefabs -
// now lives in its own file, ZoneData.cs. See that file for why.)

// Procedurally spawns ground pieces, obstacles, and coins ahead of the
// player as they run. Triggered by the player passing through "Spawner"
// tagged trigger volumes on each ground piece (see OnTriggerEnter), which
// spawns the NEXT ground piece plus its rows of obstacles/coins - a rolling
// conveyor belt that only ever builds a little ahead of the player.
// Trashbin.cs is what cleans up everything spawned here once the player
// has passed it.
public class GroundSpawner : MonoBehaviour
{
    // WORKING FIELDS - overwritten automatically from the active zone's
    // own prefab sets each time a new ground piece spawns (see
    // ApplyZoneForNextPiece). Private, since they're never meant to be
    // manually assigned - set everything per-zone in the Zones list
    // below instead. coinPrefab is the one exception, public since coins
    // stay the same across every zone and DO need manual Inspector assignment.
    private GameObject[] groundPrefab;
    private GameObject[] obstaclePrefab;
    public GameObject[] coinPrefab; // shared across all zones, not zone-specific

    [Header("Zones")]
    [Tooltip("Assign exactly 4 zones in this order: Forest, Frontier, Suburbs, City. The sequence experienced in-game bounces back and forth across this list forever via Mathf.PingPong - Forest, Frontier, Suburbs, City, Suburbs, Frontier, Forest, repeat - a true palindrome loop, so only these 4 unique zones are needed even though the visited sequence has 7 steps.")]
    public ZoneData[] zones;
    [Tooltip("How many ground pieces (spawndistance units each) make up one zone. E.g. 17 pieces x 60 units = 1020, close to a 1000-unit zone length - 1000 isn't a clean multiple of 60, so this is expressed in whole pieces instead of a raw distance, guaranteeing the zone switch always lands exactly on a piece boundary.")]
    public int piecesPerZone = 17;

    // How many ground pieces have been spawned in total, across the whole
    // run - used to compute which zone the NEXT piece belongs to.
    private int groundPiecesSpawned = 0;

    // The currently active zone's data, set by ApplyZoneForNextPiece().
    // Null until the first call (e.g. if zones is empty), guarded
    // wherever it's read.
    private ZoneData currentZoneData;

    [Header("Forest / Scenery Placement")]
    [Tooltip("X range for the LEFT side strip (used by both organic scatter and lot-based placement).")]
    public float forestLeftMinX = -28f;
    public float forestLeftMaxX = -8f;
    [Tooltip("X range for the RIGHT side strip.")]
    public float forestRightMinX = 8f;
    public float forestRightMaxX = 28f;
    [Tooltip("How many trees to place per side, per ground piece. Only used for organic-scatter zones (Use Lot Based Scenery unchecked).")]
    public int forestTreesPerSide = 4;
    [Tooltip("Y offset for spawned scenery prefabs (ground level by default).")]
    public float forestYOffset = 0f;
    [Tooltip("Minimum distance kept between trees (within the same side/piece), to reduce them clipping into each other. Only used for organic-scatter zones.")]
    public float forestMinTreeDistance = 4f;
    [Tooltip("How many times to retry a random position before giving up and placing the tree at its last rolled spot anyway - some clipping is better than spawning fewer trees than intended, since this is just background decoration. Only used for organic-scatter zones.")]
    public int forestPlacementAttempts = 5;
    [Tooltip("Y rotation applied to lot-based prefabs (houses etc) so they face the street - the left side strip gets -this, the right side gets +this. Only used for lot-based zones (Use Lot Based Scenery checked); shared across all zones rather than per-zone, since 'face the road' is a placement convention, not a zone-specific look.")]
    public float lotFacingRotation = 90f;

    [Header("Power-Ups")]
    [Tooltip("Power-up pickup prefabs (each needs a PowerUpPickup component, tagged 'PowerUp') that can spawn into the world. One is randomly picked whenever a power-up spawn succeeds.")]
    public GameObject[] powerUpPrefab;
    [Range(0f, 1f)]
    [Tooltip("Chance PER GROUND PIECE that a power-up spawns at all. Kept low - these are meant to be rarer, more special pickups than coins.")]
    public float powerUpSpawnChance = 0.08f;
    [Tooltip("Y offset for spawned power-up pickups.")]
    public float powerUpYOffset = 1f;

    // WORKING FIELDS below - all overwritten automatically per zone by
    // ApplyZoneForNextPiece(). Private for the same reason as
    // groundPrefab/obstaclePrefab above - set these per-zone in the Zones
    // list instead.
    private float obstacleYOffset = 0f;
    private float[] obstacleYOffsets;
    private GameObject specialGroundPrefab; // the specific ground prefab that needs the pre-emptive flip
    private float flippedZOffset = 36f; // forward/backward nudge to close the gap left by the 180-degree flip
    private string modelChildName = "Model"; // name of the child object holding the visual mesh (not the trigger)
    [Range(0f, 1f)]
    [Tooltip("Chance PER ROW that it becomes a 'special row' using the zone's Wide Obstacle Prefab (a single model spanning all 3 lanes) instead of the normal per-lane random logic. Shared across all zones rather than per-zone, same reasoning as Rows Per Ground - it's a difficulty/pacing knob, not a visual one.")]
    public float specialRowChance = 0.1f;

    [Header("Row Spacing")]
    [Tooltip("How many obstacle/coin rows per ground piece. Spacing between rows is automatically calculated (spawndistance / this value) so rows always fit evenly across the ground piece's length - lowering this spaces rows out further, raising it packs them tighter. Was 6 by default; try 5 to space things out more.")]
    public float rowsPerGround = 5f;

    //Ground  Settings
    private float cooldownTime = 1f; // cooldown until next Prefab can be spawned
    private float nextSpawnTime = 0f; // spawn time
    private float spawndistance = 60f; // spawn distance
    private float nextSpawnZ = 68f; // next spawn X

    //Obstacle Settings
    private readonly float[] lanes = { -3.15f, 0f, 3.15f }; // obstacle  lanes
    [Range(0f, 1f)]
    private float SpawnChancePerLane = 0.5f; // chance of spawning a obstacle
    private float rowsSpacingZ; // amount of space between rows - computed in Start() from spawndistance / rowsPerGround
    private float SafeZone = 20f;

    [Header("Coin Groups")]
    [Tooltip("Chance PER GROUND PIECE that a group of coins spawns.")]
    [Range(0f, 1f)]
    public float coinGroupSpawnChance = 0.3f;
    [Tooltip("Minimum coins per group (inclusive).")]
    public int coinGroupMinCount = 1;
    [Tooltip("Maximum coins per group (inclusive).")]
    public int coinGroupMaxCount = 5;
    [Tooltip("Z spacing between coins within a group.")]
    public float coinGroupSpacing = 3f;
    [Tooltip("Radius checked around each candidate coin/power-up position for existing obstacles - if anything tagged 'Obstacle' or 'ObstacleFront' is found within this radius, that spot is skipped/retried.")]
    public float obstacleAvoidRadius = 1.5f;

    // Justice pattern-mode state: a fixed, learnable sequence of lanes
    // instead of independent per-lane random rolls.
    private int justicePatternIndex = 0;

    // Continuous cursor tracking the earliest Z the next row is allowed to
    // spawn at, carried across DIFFERENT ground pieces (and different
    // SpawnRowsForSlot/SpawnFirstWave calls). Without this, each ground
    // piece's rows were only ever bounded by ITS OWN 60-unit slice - if a
    // special row landed near the end of one piece, the extra space it
    // needed afterward was only respected within that piece, not carried
    // over into the next one's independently-generated first row. Starts
    // at negative infinity so the very first row placed is never clamped.
    private float nextAllowedRowZ = float.NegativeInfinity;

    // spawns the first wave of obstacles on start
    private void Start()
    {
        // Derive spacing so rowsPerGround rows always fit evenly across one
        // ground piece, regardless of what rowsPerGround is set to in the
        // Inspector - e.g. 5 rows across 60 units = 12 units apart.
        rowsSpacingZ = spawndistance / Mathf.Max(1f, rowsPerGround);

        ApplyEventModifiers();
        ApplyZoneForNextPiece(); // sets the starting zone (Forest, at groundPiecesSpawned=0) before anything spawns
        SpawnFirstWave();
    }

    // Determines which zone the UPCOMING ground piece(s) belong to, based
    // on how many pieces have been spawned so far, and copies that zone's
    // prefab references into the working fields the rest of this script
    // already reads (groundPrefab, obstaclePrefab, obstacleYOffsets,
    // specialGroundPrefab). A hard cut, no
    // blending - and since ground pieces are always a whole spawndistance
    // long, this can never land mid-piece.
    //
    // Mathf.PingPong bounces the index back and forth across the zones
    // array forever: 0,1,2,3,2,1,0,1,2,3... which for a 4-zone array
    // [Forest, Frontier, Suburbs, City] gives exactly the required
    // sequence: Forest, Frontier, Suburbs, City, Suburbs, Frontier,
    // Forest, repeat - a true palindrome loop from just 4 unique zones.
    private void ApplyZoneForNextPiece()
    {
        if (zones == null || zones.Length == 0) return;

        int zoneIndex = zones.Length > 1
            ? Mathf.RoundToInt(Mathf.PingPong(groundPiecesSpawned / (float)piecesPerZone, zones.Length - 1))
            : 0;

        currentZoneData = zones[zoneIndex];

        groundPrefab = currentZoneData.groundPrefab;
        obstaclePrefab = currentZoneData.obstaclePrefab;
        obstacleYOffset = currentZoneData.obstacleYOffset;
        obstacleYOffsets = currentZoneData.obstacleYOffsets;
        specialGroundPrefab = currentZoneData.specialGroundPrefab;
        flippedZOffset = currentZoneData.flippedZOffset;
        modelChildName = currentZoneData.modelChildName;
    }

    // Reads the active soul trait (if any) from EventRunManager and adjusts
    // spawn density/spacing accordingly. Safe to call with no
    // EventRunManager in the scene (normal runs use the inspector defaults).
    private void ApplyEventModifiers()
    {
        if (EventRunManager.instance == null) return;

        switch (EventRunManager.instance.CurrentTrait)
        {
            case SoulTrait.Bravery:
                SpawnChancePerLane = Mathf.Clamp01(SpawnChancePerLane * EventRunManager.instance.braverySpawnChanceMultiplier);
                cooldownTime *= EventRunManager.instance.braveryCooldownMultiplier;
                break;

            case SoulTrait.Kindness:
                coinGroupSpawnChance = Mathf.Clamp01(coinGroupSpawnChance * EventRunManager.instance.kindnessCoinMultiplier);
                SpawnChancePerLane *= 0.6f;
                break;

            case SoulTrait.Patience:
                SpawnChancePerLane = Mathf.Clamp01(SpawnChancePerLane * EventRunManager.instance.patienceObstacleDensityMultiplier);
                rowsSpacingZ *= EventRunManager.instance.patienceRowSpacingMultiplier;

                // rowsPerGround and rowsSpacingZ are tuned so their product
                // fits exactly inside one ground piece (spawndistance).
                // Widening rowsSpacingZ without shrinking rowsPerGround makes
                // the last rows overflow past the ground piece's end and
                // overlap with the NEXT ground slot's independently-rolled
                // rows - which is what was causing rows to occasionally
                // clip into each other or add up to an impassable lane
                // combination. Recompute rowsPerGround so the total span
                // still fits within spawndistance.
                rowsPerGround = Mathf.Max(2f, Mathf.Floor(spawndistance / rowsSpacingZ));
                break;

            case SoulTrait.Justice:
                justicePatternIndex = 0;
                break;

            case SoulTrait.Determination:
                // Both return 1x if that category wasn't the one rolled,
                // so it's safe to always apply both regardless of which
                // (if either) actually landed.
                SpawnChancePerLane = Mathf.Clamp01(SpawnChancePerLane * EventRunManager.instance.GetDeterminationObstacleDensityMultiplier());
                cooldownTime *= EventRunManager.instance.GetDeterminationObstacleCooldownMultiplier();
                break;
        }
    }

    // True when Justice's per-run pattern spawning should be used instead
    // of the normal per-lane random rolls.
    private bool UseJusticePattern()
    {
        return EventRunManager.instance != null
            && EventRunManager.instance.CurrentTrait == SoulTrait.Justice
            && EventRunManager.instance.justicePatternMode
            && EventRunManager.instance.CurrentJusticePattern != null
            && EventRunManager.instance.CurrentJusticePattern.Length > 0;
    }

    // Everything decided for one row before anything is actually
    // instantiated. Splitting "decide" from "spawn" is what lets the
    // caller find out a row will be special BEFORE picking its Z position,
    // so it can be centered symmetrically instead of only pushed away
    // from the row after it.
    private class RowPlan
    {
        public GameObject[] laneObstacle = new GameObject[3]; // null = no obstacle for that lane
        public bool isSpecial;
        public GameObject wideObstacle; // if set, this row uses ONE wide model spanning all 3 lanes instead of per-lane obstacles - see PlanRow
    }

    // Decides what a row WOULD contain, without spawning anything yet.
    // Behaves exactly like the original per-lane random logic when no
    // event trait is active.
    private RowPlan PlanRow()
    {
        var plan = new RowPlan();

        if (UseJusticePattern())
        {
            int[] pattern = EventRunManager.instance.CurrentJusticePattern;
            int laneIndex = pattern[justicePatternIndex % pattern.Length];
            justicePatternIndex++;

            if (laneIndex >= 0 && laneIndex < lanes.Length && obstaclePrefab != null && obstaclePrefab.Length > 0)
            {
                plan.laneObstacle[laneIndex] = obstaclePrefab[Random.Range(0, obstaclePrefab.Length)];
            }

            return plan; // Justice's pattern only ever places 1 obstacle per row - never "special"
        }

        // Dedicated special-row roll: a single wide obstacle spanning all
        // 3 lanes, rolled independently of the normal per-lane logic
        // below. This REPLACED the old approach of detecting "special" as
        // an emergent side effect of 2 normal obstacles + 1 cap-bypassing
        // obstacle happening to fill all 3 lanes - a purpose-built wide
        // model is used instead of combining 3 separate obstacle
        // instances, and the old cap-bypassing "special obstacle"
        // mechanic has been removed entirely as redundant.
        if (currentZoneData != null && currentZoneData.wideObstaclePrefab != null && Random.value < specialRowChance)
        {
            plan.isSpecial = true;
            plan.wideObstacle = currentZoneData.wideObstaclePrefab;
            return plan;
        }

        int obstaclesPlannedThisTurn = 0; // how many obstacles have been planned this row, capped at 2

        for (int i = 0; i < lanes.Length; i++)
        {
            // guard: ensure there is at least one obstacle prefab, and the row isn't already full
            if (Random.value < SpawnChancePerLane && obstaclePrefab != null && obstaclePrefab.Length > 0 && obstaclesPlannedThisTurn < 2)
            {
                var obstacleToSpawn = obstaclePrefab[Random.Range(0, obstaclePrefab.Length)];
                plan.laneObstacle[i] = obstacleToSpawn;
                obstaclesPlannedThisTurn++; // add to the amount of obstacle planned in this row
            }
        }

        return plan;
    }

    // Actually instantiates whatever PlanRow decided, at the given Z.
    private void SpawnPlannedRow(RowPlan plan, float currentRowZ)
    {
        if (plan.wideObstacle != null)
        {
            // Special row: one wide model centered across all 3 lanes
            // (X=0), instead of per-lane obstacles.
            float y = currentZoneData != null ? currentZoneData.wideObstacleYOffset : 0f;
            Vector3 obstaclePosition = new Vector3(0f, y, currentRowZ);
            Instantiate(plan.wideObstacle, obstaclePosition, Quaternion.identity);
            return;
        }

        for (int i = 0; i < lanes.Length; i++)
        {
            if (plan.laneObstacle[i] != null)
            {
                Vector3 obstaclePosition = new Vector3(lanes[i], GetObstacleYOffset(plan.laneObstacle[i]), currentRowZ);
                Instantiate(plan.laneObstacle[i], obstaclePosition, Quaternion.identity);
            }
        }
    }

    // Returns the total Y offset to spawn a given obstacle prefab at: the
    // shared base offset (obstacleYOffset) plus that specific prefab's own
    // offset, if one is set in obstacleYOffsets (matched by the prefab's
    // index in obstaclePrefab). Missing/out-of-range entries just use 0.
    private float GetObstacleYOffset(GameObject prefab)
    {
        float perObstacleOffset = 0f;

        if (obstaclePrefab != null && obstacleYOffsets != null)
        {
            int index = System.Array.IndexOf(obstaclePrefab, prefab);
            if (index >= 0 && index < obstacleYOffsets.Length)
            {
                perObstacleOffset = obstacleYOffsets[index];
            }
        }

        return obstacleYOffset + perObstacleOffset;
    }

    // Returns the configured rotation for the special obstacle prefab, identity for everything else.
    // Searches this transform and all descendants for a child with the given name.
    // Needed because Transform.Find only checks direct children, not grandchildren.
    private Transform FindDeepChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name)
            {
                return child;
            }

            Transform found = FindDeepChild(child, name);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    // Spawns a ground prefab at a given position with a given rotation.
    // If the prefab matches the special one, it first spawns a 180-degree
    // flipped copy in front of it, then advances the spawn point and
    // spawns the real prefab in its normal orientation.
    // Returns the Z position(s) of every ground slot that was spawned, so
    // the caller can generate a full set of obstacle/coin rows for EACH one.
    private List<float> SpawnGroundPiece(GameObject prefabToSpawn)
    {
        var slotCenters = new List<float>();

        if (specialGroundPrefab != null && prefabToSpawn == specialGroundPrefab)
        {
            // Slot 1: the pre-emptive, flipped-model piece
            float flippedSlotZ = nextSpawnZ;
            slotCenters.Add(flippedSlotZ);

            // Spawn it with NORMAL rotation so the trigger (and any other children) keep
            // their original orientation. flippedZOffset is a purely visual nudge to close
            // the mesh gap caused by flipping the model — it does NOT move the slot itself,
            // so obstacle rows below still line up with the trigger's actual position.
            GameObject flippedInstance = Instantiate(prefabToSpawn, new Vector3(0, 0, flippedSlotZ + flippedZOffset), Quaternion.identity);

            // Only rotate the visual model child by 180 degrees, leaving siblings (e.g. the trigger) untouched
            Transform modelChild = FindDeepChild(flippedInstance.transform, modelChildName);
            if (modelChild != null)
            {
                modelChild.localRotation = Quaternion.Euler(0f, 180f, 0f) * modelChild.localRotation;
            }
            else
            {
                Debug.LogWarning($"GroundSpawner: could not find a child named '{modelChildName}' on {prefabToSpawn.name} to flip.");
            }

            // move the spawn point forward so the real piece doesn't overlap the flipped one
            nextSpawnZ += spawndistance;

            // Slot 2: the same prefab again, in its original orientation
            float normalSlotZ = nextSpawnZ;
            slotCenters.Add(normalSlotZ);
            Instantiate(prefabToSpawn, new Vector3(0, 0, normalSlotZ), Quaternion.identity);
        }
        else
        {
            slotCenters.Add(nextSpawnZ);
            Instantiate(prefabToSpawn, new Vector3(0, 0, nextSpawnZ), Quaternion.identity);
        }

        return slotCenters;
    }

    // Spawns rows of obstacles/coins across a single ground slot, filling
    // as close to rowsPerGround rows as fit. Extracted so it can be called
    // once per ground piece, instead of assuming there's always exactly
    // one ground piece per spawn event.
    private void SpawnRowsForSlot(float slotCenterZ)
    {
        float startZ = slotCenterZ - (spawndistance / 2f);
        float endZ = slotCenterZ + (spawndistance / 2f);

        // Never start earlier than nextAllowedRowZ - if the PREVIOUS
        // ground piece's last row (possibly special) left pending "extra
        // space after it" that spilled past that piece's own boundary,
        // this respects that debt instead of resetting to this piece's own
        // startZ, which would ignore it and place a row too close to the
        // previous one.
        float currentRowZ = Mathf.Max(startZ, nextAllowedRowZ);

        // Bounded by physical distance (endZ), NOT a fixed row count.
        // Special rows eat extra space (see below) - if that pushed
        // placement past a row-count limit instead, the leftover rows
        // would spill past this ground piece's actual length and overlap
        // the NEXT ground piece's own independently-generated rows. Fewer
        // rows fitting in a piece that had a lot of special rows is the
        // correct tradeoff, not overlap.
        while (currentRowZ < endZ)
        {
            RowPlan plan = PlanRow();

            // A special row needs extra room on BOTH sides to be fair -
            // enough space to react before it and recover after it.
            // Planning first (instead of deciding after spawning) is what
            // makes this possible: we know it's special before committing
            // to a position, so half the extra space goes in front...
            if (plan.isSpecial)
            {
                float candidateZ = currentRowZ + rowsSpacingZ * 0.5f;
                if (candidateZ >= endZ) break; // no room left to space it properly in this piece
                currentRowZ = candidateZ;
            }

            SpawnPlannedRow(plan, currentRowZ);

            currentRowZ += rowsSpacingZ;

            // ...and the other half goes after, before the next row - so
            // the special row ends up centered in the extra space instead
            // of flush against the row before it.
            if (plan.isSpecial)
            {
                currentRowZ += rowsSpacingZ * 0.5f;
            }
        }

        // Remember where we left off - including any special-row back-padding
        // that overshot this piece's own boundary - so the NEXT ground
        // piece (even though its own startZ might be smaller) never places
        // a row earlier than this.
        nextAllowedRowZ = currentRowZ;
    }

    // Spawns enough rows of obstacles/coins to fill the ground pieces that
    // already exist in the scene at Start() (placed manually in the editor,
    // covering the player's starting area) - everything AFTER that is
    // spawned dynamically via OnTriggerEnter as the player advances.
    // Ground pieces inside SafeZone (near the very start) are skipped so
    // the player doesn't spawn on top of an obstacle.
    private void SpawnFirstWave()
    {
        float initalGroundCount = Mathf.Ceil(nextSpawnZ / spawndistance);

        for (int g = 0; g < initalGroundCount; g++)
        {
            float currentGroundZ = g * spawndistance;

            // Keep the zone/piece counter in sync from the very start, so
            // OnTriggerEnter's dynamically-spawned pieces continue the
            // sequence correctly afterward instead of resetting to zone 0.
            ApplyZoneForNextPiece();
            groundPiecesSpawned++;

            // Forest is purely decorative and off to the sides (not in the
            // player's lanes), so unlike obstacle rows it isn't gated by
            // SafeZone - it can start right from the very beginning (Z=0).
            SpawnForest(currentGroundZ);

            if (currentGroundZ < SafeZone)
            {
                continue;
            }

            // Power-ups and coin groups occupy actual lanes (unlike
            // Forest, which is off to the side), so they're gated by
            // SafeZone the same way obstacle rows are - no spawning right
            // on top of the player.
            SpawnPowerUps(currentGroundZ);
            SpawnCoinGroup(currentGroundZ);

            float startZ = currentGroundZ - (spawndistance / 2f);
            float endZ = currentGroundZ + (spawndistance / 2f);
            float currentRowZ = Mathf.Max(startZ, nextAllowedRowZ);

            // Bounded by physical distance, same reasoning as SpawnRowsForSlot.
            while (currentRowZ < endZ)
            {
                RowPlan plan = PlanRow();

                if (plan.isSpecial)
                {
                    float candidateZ = currentRowZ + rowsSpacingZ * 0.5f;
                    if (candidateZ >= endZ) break;
                    currentRowZ = candidateZ;
                }

                SpawnPlannedRow(plan, currentRowZ);

                currentRowZ += rowsSpacingZ;

                if (plan.isSpecial)
                {
                    currentRowZ += rowsSpacingZ * 0.5f;
                }
            }

            nextAllowedRowZ = currentRowZ;
        }
    }

    // Occasionally spawns a single power-up pickup in one of the 3 lanes
    // for this ground piece. Kept separate from the obstacle/coin row
    // logic (PlanRow/SpawnPlannedRow) since power-ups are rarer, more
    // special pickups that don't need to interact with the 2-obstacle row
    // cap or special-row centering system.
    //
    // KNOWN LIMITATION: this doesn't check what PlanRow already placed in
    // that lane/row, so a power-up can occasionally spawn overlapping an
    // obstacle. Fine for now given how rare these are meant to be
    // (powerUpSpawnChance is low), but worth revisiting if it turns out
    // to happen often enough to be annoying.
    private void SpawnPowerUps(float slotCenterZ)
    {
        if (powerUpPrefab == null || powerUpPrefab.Length == 0) return;
        if (Random.value > powerUpSpawnChance) return;

        // Retry a few times to find a spot clear of obstacles, same
        // pattern as the forest placement retries - some clipping risk is
        // acceptable elsewhere, but a power-up spawning inside an
        // obstacle could make it impossible to collect, so this just
        // skips the spawn entirely if no clear spot is found rather than
        // forcing an overlap.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            int laneIndex = Random.Range(0, lanes.Length);
            Vector3 position = new Vector3(lanes[laneIndex], powerUpYOffset, slotCenterZ);

            if (!IsNearObstacle(position, obstacleAvoidRadius))
            {
                var prefabToSpawn = powerUpPrefab[Random.Range(0, powerUpPrefab.Length)];
                Instantiate(prefabToSpawn, position, Quaternion.identity);
                return;
            }
        }
    }

    // True if anything tagged Obstacle or ObstacleFront is within radius
    // of position - used to keep coins and power-ups from spawning inside
    // obstacles.
    private bool IsNearObstacle(Vector3 position, float radius)
    {
        Collider[] nearby = Physics.OverlapSphere(position, radius);
        foreach (Collider col in nearby)
        {
            if (col.CompareTag("Obstacle") || col.CompareTag("ObstacleFront"))
            {
                return true;
            }
        }
        return false;
    }

    // Occasionally spawns a group of 1-5 coins in a single lane, spaced
    // along Z within this ground piece. Kept separate from the old
    // per-row random coin logic (removed) for the same reason obstacles'
    // special rows are separate - a deliberate, dedicated roll rather
    // than an emergent per-lane pattern. Each candidate coin position is
    // checked against nearby obstacles and skipped if too close, so a
    // group can end up with fewer coins than count if some spots were blocked.
    private void SpawnCoinGroup(float slotCenterZ)
    {
        if (coinPrefab == null || coinPrefab.Length == 0) return;
        if (Random.value > coinGroupSpawnChance) return;

        int laneIndex = Random.Range(0, lanes.Length);
        int count = Random.Range(coinGroupMinCount, coinGroupMaxCount + 1);

        // Centers the group on slotCenterZ regardless of count.
        float startZ = slotCenterZ - ((count - 1) * coinGroupSpacing / 2f);

        for (int i = 0; i < count; i++)
        {
            float z = startZ + (i * coinGroupSpacing);
            Vector3 position = new Vector3(lanes[laneIndex], 1f, z);

            if (IsNearObstacle(position, obstacleAvoidRadius)) continue;

            var coinToSpawn = coinPrefab[Random.Range(0, coinPrefab.Length)];
            Instantiate(coinToSpawn, position, Quaternion.identity);
        }
    }

    // Spawns random forest decoration flanking the track for one ground
    // piece's Z span (slotCenterZ +/- spawndistance/2), on both the left
    // and right side strips. Purely decorative - doesn't affect gameplay,
    // just fills the world visually. Safe to call even with no forest
    // prefabs assigned (does nothing).
    private void SpawnForest(float slotCenterZ)
    {
        if (currentZoneData == null) return;

        float minZ = slotCenterZ - (spawndistance / 2f);
        float maxZ = slotCenterZ + (spawndistance / 2f);

        if (currentZoneData.useLotBasedScenery)
        {
            // Mirrored fixed X (left = -lotX, right = +lotX) and mirrored
            // facing rotation (left = -lotFacingRotation, right =
            // +lotFacingRotation), so houses on both sides face inward
            // toward the road instead of both facing the same direction.
            SpawnLotStrip(-currentZoneData.lotX, minZ, currentZoneData.lotPrefab, currentZoneData.lotLength, currentZoneData.lotY, lotFacingRotation);
            SpawnLotStrip(currentZoneData.lotX, minZ, currentZoneData.lotPrefab, currentZoneData.lotLength, currentZoneData.lotY, -lotFacingRotation);
        }
        else
        {
            if (currentZoneData.scatterPrefab == null || currentZoneData.scatterPrefab.Length == 0) return;

            SpawnForestStrip(forestLeftMinX, forestLeftMaxX, minZ, maxZ, currentZoneData.scatterPrefab);
            SpawnForestStrip(forestRightMinX, forestRightMaxX, minZ, maxZ, currentZoneData.scatterPrefab);
        }
    }

    // Scatters forestTreesPerSide random trees within one X/Z rectangle,
    // each with a random Y rotation for visual variety. Retries a few
    // times if a candidate spot lands too close to an already-placed tree
    // in this same batch, to reduce visible clipping between them.
    private void SpawnForestStrip(float minX, float maxX, float minZ, float maxZ, GameObject[] scatterPrefabs)
    {
        var placedPositions = new List<Vector2>(); // XZ only - Y doesn't matter for spacing

        for (int i = 0; i < forestTreesPerSide; i++)
        {
            Vector2 candidate = new Vector2(Random.Range(minX, maxX), Random.Range(minZ, maxZ));

            for (int attempt = 0; attempt < forestPlacementAttempts; attempt++)
            {
                bool tooClose = false;
                foreach (Vector2 placed in placedPositions)
                {
                    if (Vector2.Distance(candidate, placed) < forestMinTreeDistance)
                    {
                        tooClose = true;
                        break;
                    }
                }

                if (!tooClose) break; // found a good spot, stop retrying

                candidate = new Vector2(Random.Range(minX, maxX), Random.Range(minZ, maxZ));
            }

            // Even if every attempt was still too close, place it at the
            // last rolled candidate anyway rather than skipping it - some
            // clipping is preferable to a noticeably sparser forest.
            placedPositions.Add(candidate);

            var prefabToSpawn = scatterPrefabs[Random.Range(0, scatterPrefabs.Length)];
            Quaternion randomYRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            Instantiate(prefabToSpawn, new Vector3(candidate.x, forestYOffset, candidate.y), randomYRotation);
        }
    }

    // Places one prefab per fixed-length "lot" along a strip, back to
    // back with no gap check needed - inherently clip-proof by
    // construction, since lots never overlap as long as lotLength is
    // sized to fit the largest prefab. Used for buildings (Suburbs/City),
    // which vary too much in footprint size for the organic
    // minimum-distance approach above to handle cleanly.
    // Places lotPrefabs on an EXACT, deterministic grid - fixed X (no
    // randomness, passed in already mirrored per side by the caller),
    // fixed Y, fixed Z spacing (lotLength apart), and a fixed facing
    // rotation (also mirrored per side by the caller, so houses on both
    // sides face inward toward the road). Places exactly spawndistance /
    // lotLength lots per side (e.g. 60 / 12 = 5), perfectly filling the
    // ground piece with no gaps or overlap - only which prefab gets
    // picked is random, nothing about position or rotation.
    private void SpawnLotStrip(float x, float minZ, GameObject[] lotPrefabs, float lotLength, float y, float yRotation)
    {
        if (lotPrefabs == null || lotPrefabs.Length == 0 || lotLength <= 0f) return;

        int lotsCount = Mathf.RoundToInt(spawndistance / lotLength);
        Quaternion rotation = Quaternion.Euler(0f, yRotation, 0f);

        for (int i = 0; i < lotsCount; i++)
        {
            float z = minZ + (i * lotLength) + (lotLength / 2f); // center of each lot
            var prefabToSpawn = lotPrefabs[Random.Range(0, lotPrefabs.Length)];

            Instantiate(prefabToSpawn, new Vector3(x, y, z), rotation);
        }
    }

    // Fires when the player reaches a "Spawner" tagged trigger (placed on
    // each ground piece) - this is the actual conveyor-belt mechanism that
    // keeps spawning new ground/obstacles/coins just ahead of the player as
    // they progress, one ground piece at a time, gated by cooldownTime so
    // it can't fire more than once per ground piece even if multiple
    // triggers briefly overlap.
    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.CompareTag("Spawner") && Time.time >= nextSpawnTime) // check if the player reached the next spawn point
        {
            nextSpawnTime = Time.time + cooldownTime; //Cooldown update
            nextSpawnZ += spawndistance; // ground prefab spawn distance

            // Determine which zone this next piece belongs to BEFORE
            // spawning it, so groundPrefab/obstaclePrefab/etc. are already
            // pointing at the right set when SpawnGroundPiece/SpawnRowsForSlot/
            // SpawnForest below actually use them.
            ApplyZoneForNextPiece();

            List<float> groundSlots = new List<float> { nextSpawnZ }; // fallback in case groundPrefab is empty

            // guard: ensure there is at least one ground prefab
            if (groundPrefab != null && groundPrefab.Length > 0)
            {
                var groundToSpawn = groundPrefab[Random.Range(0, groundPrefab.Length)];
                groundSlots = SpawnGroundPiece(groundToSpawn); // handles the flip-and-respawn logic internally, may advance nextSpawnZ and return 2 slots
            }

            // Both flip slots (if 2 were returned) still use the SAME
            // zone's data, since ApplyZoneForNextPiece was only called
            // once above - correct, since they're generated together as
            // one conceptual piece.
            groundPiecesSpawned += groundSlots.Count;

            // Spawn a full set of obstacle/coin rows for EACH ground slot that was placed
            foreach (float slotZ in groundSlots)
            {
                SpawnRowsForSlot(slotZ);
                SpawnForest(slotZ);
                SpawnPowerUps(slotZ);
                SpawnCoinGroup(slotZ);
            }
        }
    }
}