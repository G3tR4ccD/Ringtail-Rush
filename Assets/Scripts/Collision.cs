using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Handles everything related to the player taking hits and collecting
// coins: the graze/escalation hit system, armor/revive charges (including
// Seed/Event Run bonuses), phasing through survived obstacles, the
// lives/armor HUD, coin pickup (with Kindness/Determination payout
// multipliers), and ending the run. Works together with Movement.cs, which
// handles the actual physical movement and plays the matching animations.
public class PlayerCollision : MonoBehaviour
{
    // Reference to this same character's Movement script - used to trigger
    // hit-reaction animations/lane changes and to read current distance
    // (transform.position.z) for Determination's distance-based growth.
    public Movement movement;

    // Current Takeout balance for this run, mirrored into DataManager so it
    // persists into the Shop scene.
    private int funds;
    public TextMeshProUGUI coinText;

    [Header("Lives / Armor Display")]
    [Tooltip("Pre-place enough icon Image objects here (e.g. under a Horizontal Layout Group) to cover the highest armor count you realistically expect - icons beyond the current count are simply hidden, so you never need more than you have slots for.")]
    public Image[] armorIcons;
    [Tooltip("Same idea as Armor Icons, but for lives (baseline 1 + any extra revives).")]
    public Image[] livesIcons;
    [Tooltip("Sprite shown on a lives icon that's still available.")]
    public Sprite livesFullSprite;
    [Tooltip("Sprite shown on a lives icon that's already been used, instead of hiding it entirely.")]
    public Sprite livesUsedSprite;

    // The most lives ever granted this run (starts at the baseline 1,
    // bumped whenever revives are granted). Icon slots up to this count
    // stay visible and show full/used depending on whether they're still
    // available - slots beyond it are hidden entirely, since they
    // represent lives that were never granted this run at all.
    private int maxLivesThisRun = 1;

    // The player's own collider (CharacterController is itself a Collider).
    // Used to make the player ignore collision with a specific obstacle
    // once a hit against it has been survived - see PhaseThroughObstacle.
    private Collider playerCollider;

    private bool isGameOver = false;

    // A hit landing more than hitWindow after the previous one starts a
    // fresh window and is always a free graze. Any further hit within
    // hitWindow of the previous one escalates: needs an armor/revive
    // charge to survive, or ends the run. The graze allowance itself
    // resets only when the window resets - it doesn't refill mid-window.
    private float hitWindow = 5f;
    private float lastHitTime = -999f;
    private bool hasGrazedInThisWindow = false;

    // Full invincibility (no hit registers at all, not even a graze)
    // starting the moment a graze happens. Separate from
    // Movement.IsPlayingHitReaction() - that one only lasts as long as
    // the Hit animation clip itself, this is a flat, deliberately longer
    // window meant to give the player a real chance to react and recover
    // after a graze specifically.
    [Tooltip("How long the player is fully invincible after a graze (a forgiven hit that didn't cost armor/revives).")]
    public float grazeInvincibilityDuration = 2.5f;
    private float invincibleUntil = -999f;
    private bool IsInvincible => Time.time < invincibleUntil;

    // Seed/Event Run state: Bravery grants bonus armor hits, Perseverance
    // grants revives instead of a hard stop. Both stay at 0 for normal runs.
    private int eventArmorHits = 0;
    private int eventRevivesRemaining = 0;

    // CharacterController doesn't fire OnCollisionEnter/Exit for solid collisions -
    // it uses OnControllerColliderHit instead, which is called every frame contact
    // continues (not just once on first touch). This tracks the last frame each
    // obstacle collider was hit so continuous contact with the same obstacle still
    // only counts as one hit, the same way the old OnCollisionEnter/Exit pair did.
    private readonly Dictionary<Collider, int> touchingObstacleFrames = new Dictionary<Collider, int>();

    // How aligned a hit's surface normal needs to be with "straight back
    // toward the player" to count as a head-on collision (RegisterFatalHit)
    // instead of a glancing side hit (RegisterHit). 1 = normal points
    // exactly backward (a flat wall dead ahead); 0 = normal points
    // perpendicular (a pure side scrape). 0.5 covers roughly the front
    // ~60 degrees of an obstacle's surface.
    private float frontHitAlignmentThreshold = 0.5f;

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hit.collider.tag != "Obstacle") return;

        bool isContinuousContact = touchingObstacleFrames.TryGetValue(hit.collider, out int lastFrame)
            && lastFrame >= Time.frameCount - 1;

        touchingObstacleFrames[hit.collider] = Time.frameCount;

        // Already in continuous contact with this specific obstacle - not a new hit.
        if (isContinuousContact) return;

        GameObject obstacleRoot = hit.collider.transform.root.gameObject;

        // While invincible (the graze grace window), phase straight
        // through ANY obstacle touched instead of physically colliding
        // with it. Without this, the player could get physically
        // stuck/blocked against back-to-back obstacles (e.g. two
        // jump-required rows in a row) at higher speeds, even though
        // they can't actually take damage during this window - the
        // invincibility check in RegisterHit/RegisterFatalHit only
        // prevents the DAMAGE, not the CharacterController's normal solid
        // collision with the geometry itself.
        if (IsInvincible)
        {
            PhaseThroughObstacle(obstacleRoot);
            return;
        }

        // A normal pointing mostly back toward the player (opposite their
        // forward direction) means they ran into a wall-like face dead
        // ahead - a head-on hit. Anything more sideways is a glancing hit.
        float frontAlignment = Vector3.Dot(hit.normal, -transform.forward);

        if (frontAlignment >= frontHitAlignmentThreshold)
        {
            RegisterFatalHit(obstacleRoot);
        }
        else
        {
            RegisterHit(obstacleRoot);
        }
    }

    // Called whenever the player hits an obstacle's main body. A hit
    // landing more than hitWindow after the previous one starts a fresh
    // window and is always a free graze. Any hit within hitWindow of the
    // previous one needs an armor/revive charge to survive, or ends the run.
    // If Determination's ArmorLives Curse is active, the graze allowance
    // is skipped entirely - every hit goes straight to the armor/revive/
    // game-over check, with no free pass at all.
    public void RegisterHit(GameObject obstacleRoot)
    {
        if (isGameOver) return;

        // Ignore entirely (not even as a graze) while still visually
        // reacting to a previous hit - prevents dying (or taking another
        // hit) mid-animation, before the player could realistically react.
        if (movement.IsPlayingHitReaction()) return;

        // Full invincibility window from a recent graze - see grazeInvincibilityDuration.
        if (IsInvincible) return;

        bool grazeDisabled = EventRunManager.instance != null && EventRunManager.instance.DeterminationRemovesGraze();

        if (!grazeDisabled)
        {
            bool isFreshWindow = (Time.time - lastHitTime) > hitWindow;
            if (isFreshWindow)
            {
                hasGrazedInThisWindow = false;
            }

            lastHitTime = Time.time;

            if (!hasGrazedInThisWindow)
            {
                hasGrazedInThisWindow = true;
                invincibleUntil = Time.time + grazeInvincibilityDuration;
                movement.GrazeBounce();
                return;
            }
        }
        else
        {
            lastHitTime = Time.time;
        }

        // Bravery's bonus armor absorbs what would otherwise be fatal.
        if (eventArmorHits > 0)
        {
            eventArmorHits--;
            UpdateHitStatusUI();
            DoSurvive(obstacleRoot);
            return;
        }

        // Perseverance/Determination revives instead of an immediate game over.
        if (eventRevivesRemaining > 0)
        {
            eventRevivesRemaining--;
            UpdateHitStatusUI();
            DoSurvive(obstacleRoot);
            return;
        }

        TriggerGameOver();
    }

    // Called for a head-on collision (the FrontTrigger on an obstacle - the
    // player ran straight into it without dodging in time). Unlike
    // RegisterHit, this never gets the free graze allowance - a head-on
    // hit is meant to be immediately punishing. Bravery's bonus armor and
    // Perseverance's/Determination's revives can still absorb it though,
    // consuming one before falling back to game over. Also marks the
    // window as "already grazed" so a side hit shortly after doesn't get
    // an undeserved fresh graze.
    public void RegisterFatalHit(GameObject obstacleRoot)
    {
        if (isGameOver) return;
        if (movement.IsPlayingHitReaction()) return;
        if (IsInvincible) return; // full invincibility window from a recent graze protects against head-on hits too

        lastHitTime = Time.time;
        hasGrazedInThisWindow = true;

        if (eventArmorHits > 0)
        {
            eventArmorHits--;
            UpdateHitStatusUI();
            DoSurvive(obstacleRoot);
            return;
        }

        if (eventRevivesRemaining > 0)
        {
            eventRevivesRemaining--;
            UpdateHitStatusUI();
            DoSurvive(obstacleRoot);
            return;
        }

        TriggerGameOver();
    }

    // A hit survived by spending an armor/revive charge: makes the
    // player's collider permanently ignore every collider under this
    // specific obstacle, so the player simply passes straight through it
    // from here on - no lane change needed, and no risk of a second
    // collision event from the same obstacle. The obstacle gets destroyed
    // by Trashbin shortly after anyway, so this never needs to be undone.
    private void DoSurvive(GameObject obstacleRoot)
    {
        PhaseThroughObstacle(obstacleRoot);
        movement.SurviveHit();
    }

    // Makes the player's collider ignore every collider under obstacleRoot,
    // so it stops generating any further collision/trigger events (both
    // OnControllerColliderHit and FrontTrigger's OnTriggerEnter) for this
    // specific obstacle instance.
    private void PhaseThroughObstacle(GameObject obstacleRoot)
    {
        if (playerCollider == null || obstacleRoot == null) return;

        Collider[] obstacleColliders = obstacleRoot.GetComponentsInChildren<Collider>();
        foreach (Collider obstacleCollider in obstacleColliders)
        {
            Physics.IgnoreCollision(playerCollider, obstacleCollider, true);
        }
    }

    // Refreshes the lives/armor HUD icons to match the current
    // eventArmorHits/eventRevivesRemaining values.
    private void UpdateHitStatusUI()
    {
        UpdateIconRow(armorIcons, eventArmorHits);

        // Every run has a baseline of 1 life (you) - extra revive charges
        // from Perseverance/Determination add on top of that, and each
        // one consumed brings this back down by 1.
        UpdateLivesIconRow(1 + eventRevivesRemaining);
    }

    // Shows lives icons using two sprites: livesFullSprite for lives
    // still remaining, livesUsedSprite for ones already spent (rather
    // than hiding them entirely) - up to maxLivesThisRun (the most lives
    // ever granted this run). Icon slots beyond maxLivesThisRun are
    // hidden entirely, since they represent lives that were never
    // granted this run at all.
    private void UpdateLivesIconRow(int currentCount)
    {
        if (livesIcons == null) return;

        for (int i = 0; i < livesIcons.Length; i++)
        {
            if (livesIcons[i] == null) continue;

            bool granted = i < maxLivesThisRun;
            livesIcons[i].gameObject.SetActive(granted);

            if (granted)
            {
                livesIcons[i].sprite = (i < currentCount) ? livesFullSprite : livesUsedSprite;
            }
        }
    }

    // Shows exactly `count` icons starting from the first slot, hides the
    // rest. Lets you pre-place more icon slots than you'll ever realistically
    // need in the Inspector and simply not use the extras, instead of
    // needing to dynamically instantiate/destroy icons at runtime.
    private void UpdateIconRow(Image[] icons, int count)
    {
        if (icons == null) return;

        for (int i = 0; i < icons.Length; i++)
        {
            if (icons[i] != null)
            {
                icons[i].gameObject.SetActive(i < count);
            }
        }
    }

    public void TriggerGameOver()
    {
        if (isGameOver) return;
        isGameOver = true;

        // The baseline "1" in UpdateHitStatusUI represents the life
        // currently being played - once it's actually lost, that no
        // longer applies, so set the HUD to 0 directly instead of going
        // through UpdateHitStatusUI (which would keep showing 1).
        UpdateLivesIconRow(0);
        UpdateIconRow(armorIcons, 0);

        movement.TriggerGameOverAnimation(); // <-- this line was missing, sets isGameOver + fires the trigger
        movement.enabled = false;
        FindAnyObjectByType<GameManager>().GameOver();
    }

    // Initializes the coin HUD from DataManager's persisted balance, caches
    // the player's own collider (for PhaseThroughObstacle), and grants any
    // Seed/Event Run starting armor/revives.
    private void Start()
    {
        funds = DataManager.instance.TransportValue;
        coinText.text = funds.ToString() + " Takeout";

        playerCollider = GetComponent<CharacterController>();
        if (playerCollider == null)
        {
            Debug.LogWarning("PlayerCollision: no CharacterController found on this GameObject - survived obstacles won't be phased through, so repeat hits from the same obstacle may still occur.");
        }

        ApplyEventModifiers();
        UpdateHitStatusUI();
    }

    // Reads the active soul trait (if any) and grants the bonus armor /
    // revives tied to it. Safe to call with no EventRunManager in the scene.
    private void ApplyEventModifiers()
    {
        if (EventRunManager.instance == null) return;

        if (EventRunManager.instance.CurrentTrait == SoulTrait.Bravery)
        {
            eventArmorHits += EventRunManager.instance.braveryBonusArmor;
        }
        else if (EventRunManager.instance.CurrentTrait == SoulTrait.Perseverance)
        {
            eventRevivesRemaining += EventRunManager.instance.perseveranceRevives;
            maxLivesThisRun = Mathf.Max(maxLivesThisRun, 1 + eventRevivesRemaining);
        }
        else if (EventRunManager.instance.CurrentTrait == SoulTrait.Determination)
        {
            // Flat, granted once - if ArmorLives wasn't rolled, or it was
            // rolled as a Curse, this returns 0 and nothing happens here.
            // The Curse side (removing the graze allowance) is handled
            // separately in RegisterHit via DeterminationRemovesGraze().
            eventArmorHits += EventRunManager.instance.GetDeterminationArmorLivesBonus();
        }

        UpdateHitStatusUI();
    }

    // Fires on any trigger overlap; only acts on objects tagged "Coin".
    // Applies Kindness's flat payout multiplier or Determination's Coins
    // effect (if rolled) before adding to the running Takeout total.
    private void OnTriggerEnter(Collider other)
    {
        if (other.transform.tag == "Coin")
        {
            // Destroy() doesn't remove the object until the end of the
            // frame - if the coin prefab has more than one collider, or the
            // player overlaps it across two physics steps in the same
            // frame, OnTriggerEnter can fire again before that happens,
            // double-counting the same coin. Disabling the collider
            // immediately makes this the last time it can ever trigger.
            other.enabled = false;

            CollectCoin();
            Destroy(other.gameObject);
        }
        else if (other.CompareTag("PowerUp"))
        {
            // Same double-trigger guard as coins.
            other.enabled = false;

            PowerUpPickup pickup = other.GetComponent<PowerUpPickup>();
            if (pickup != null)
            {
                ApplyPowerUp(pickup);
            }

            Destroy(other.gameObject);
        }
    }

    // Applies whichever effect a picked-up power-up grants. Only
    // DumpsterDive is implemented so far - add cases here as the other
    // three (JungMagnet, TrashTornado, BubbleWrap) get built.
    private void ApplyPowerUp(PowerUpPickup pickup)
    {
        switch (pickup.powerUpType)
        {
            case PowerUpType.DumpsterDive:
                // Reuses the exact same invincibility/phase-through system
                // built for the graze grace window - ghost mode is just a
                // longer version of the same effect, triggered by a
                // pickup instead of a graze. Mathf.Max so picking one up
                // mid-graze-window never SHORTENS whatever invincibility
                // is already active.
                invincibleUntil = Mathf.Max(invincibleUntil, Time.time + pickup.duration);

                // The visual jump-in/sit-in-bin/jump-out sequence - runs
                // in parallel with (and for the same duration as) the
                // invincibility above.
                movement.PlayDumpsterDiveSequence(pickup.duration);
                break;

            default:
                Debug.LogWarning($"PlayerCollision: picked up a {pickup.powerUpType} power-up, but it isn't implemented yet.");
                break;
        }
    }

    // Credits one coin's worth of Takeout (applying Kindness/Determination
    // payout multipliers, same as always) and updates the HUD. Public so
    // DumpsterBinCollector (the bin's own pickup script, active during
    // Dumpster Dive) can credit coins it collects through the same path
    // normal coin pickup uses, rather than duplicating this logic -
    // callers are responsible for destroying the coin object themselves.
    public void CollectCoin()
    {
        int coinValue = 1;

        if (EventRunManager.instance != null)
        {
            // Kindness seed runs pay out more per coin.
            if (EventRunManager.instance.CurrentTrait == SoulTrait.Kindness)
            {
                coinValue = Mathf.RoundToInt(coinValue * EventRunManager.instance.kindnessCoinMultiplier);
            }
            // Determination's Coins category (if rolled) pays out more (Blessing) or less (Curse), flat for the whole run.
            else if (EventRunManager.instance.CurrentTrait == SoulTrait.Determination)
            {
                coinValue = Mathf.RoundToInt(coinValue * EventRunManager.instance.GetDeterminationCoinsMultiplier());
            }
        }

        funds += coinValue;
        DataManager.instance.TransportValue = funds;
        coinText.text = funds.ToString() + " Takeout";
    }
}