using UnityEngine;

public enum SoulTrait
{
    None,
    [InspectorName("Bandit Run")] Bravery,
    [InspectorName("Trickster Run")] Justice,
    [InspectorName("Tenacious Tail Run")] Perseverance,
    [InspectorName("Rascal's Gambit Run")] Determination,
    [InspectorName("Washed Paws Run")] Integrity,
    [InspectorName("Dumpster Dive Run")] Kindness,
    [InspectorName("Night Prowler Run")] Patience
}

// Which stat/system a Rascal's Gambit roll affects. Picked randomly
// whenever Determination is activated (see RollDeterminationOutcome).
public enum DeterminationCategory
{
    None,
    Score,
    Coins,
    Speed,
    ArmorLives,
    ObstacleDensity,
    ObstacleCooldown
    // Lore/Trinkets is a planned blessing-only category, not added yet
    // since there's no lore/trinket collection system in the project to
    // hook it into.
}

// Whether the rolled category came out as a good (Blessing) or bad
// (Curse) effect. Rolled independently of the category, weighted by
// determinationBlessingChancePercent.
public enum DeterminationOutcome
{
    None,
    Blessing,
    Curse
}

// Central config for Seed / Event Runs (the 7 soul traits).
// Holds which trait is active for the current run and exposes the
// gameplay modifiers tied to it. Movement, PlayerCollision, GroundSpawner
// and GameManager all read from this instance instead of hardcoding
// event-specific values themselves.
//
// Usage: call EventRunManager.instance.StartEventRun(SoulTrait.Bravery, "seed_001")
// from a menu BEFORE loading the World scene. Call ClearEventRun() before
// loading World normally so a leftover trait doesn't bleed into a normal run.
public class EventRunManager : MonoBehaviour
{
    public static EventRunManager instance;

    [Header("Current Run")]
    public SoulTrait CurrentTrait = SoulTrait.None;
    public string CurrentSeedId = "default";

    [Header("BANDIT RUN (Bravery) - extra hits, but faster/denser hazards")]
    public int braveryBonusArmor = 1;
    public float braverySpeedMultiplier = 1.25f;
    public float braverySpawnChanceMultiplier = 1.3f;
    public float braveryCooldownMultiplier = 0.7f;

    [Header("TRICKSTER RUN (Justice) - a random pattern that repeats consistently within THIS run")]
    public bool justicePatternMode = true;
    [Tooltip("How many rows long the generated pattern is before it repeats. A new random pattern of this length is rolled every time Trickster Run activates.")]
    public int justicePatternLength = 6;
    [Tooltip("Currently rolled pattern for this run (lane indices, 0=left 1=mid 2=right). Set automatically - don't edit at runtime.")]
    public int[] CurrentJusticePattern;
    [Tooltip("Top speed multiplier, reached the same way as a normal run's curve - a reward for mastering a fixed, learnable pattern. Only affects the ceiling, not the starting speed.")]
    public float justiceMaxSpeedMultiplier = 2.0f;

    [Header("TENACIOUS TAIL RUN (Perseverance) - revives instead of a hard stop")]
    public int perseveranceRevives = 1;
    public bool perseveranceDisablePowerUps = true;

    [Header("RASCAL'S GAMBIT RUN (Determination) - a random blessing OR curse, flat for the whole run (no growth/scaling)")]
    [Tooltip("Which category got rolled for the current Determination run. Set automatically - don't edit at runtime.")]
    public DeterminationCategory CurrentDeterminationCategory = DeterminationCategory.None;
    [Tooltip("Whether the rolled category came out a Blessing or a Curse. Set automatically - don't edit at runtime.")]
    public DeterminationOutcome CurrentDeterminationOutcome = DeterminationOutcome.None;
    [Tooltip("Chance (0-100) that the roll comes out a Blessing rather than a Curse.")]
    [Range(0f, 100f)]
    public float determinationBlessingChancePercent = 60f;

    [Tooltip("Score category: Blessing multiplier / Curse multiplier.")]
    public float determinationScoreBlessingMultiplier = 2f;
    public float determinationScoreCurseMultiplier = 0.5f;

    [Tooltip("Coins category: Blessing payout multiplier / Curse payout multiplier.")]
    public float determinationCoinsBlessingMultiplier = 2f;
    public float determinationCoinsCurseMultiplier = 0.5f;

    [Tooltip("Speed category: Blessing = LOWER top speed (safer), Curse = HIGHER top speed (harder to react to).")]
    public float determinationSpeedBlessingMultiplier = 0.7f;
    public float determinationSpeedCurseMultiplier = 1.5f;

    [Tooltip("Armor/Lives category: Blessing grants this many bonus armor charges. The Curse removes the graze allowance entirely instead (see PlayerCollision) - every hit becomes risky from the start.")]
    public int determinationArmorLivesBlessingBonus = 1;

    [Tooltip("Obstacle Density category: Blessing multiplier (fewer) / Curse multiplier (more).")]
    public float determinationObstacleDensityBlessingMultiplier = 0.6f;
    public float determinationObstacleDensityCurseMultiplier = 1.4f;

    [Tooltip("Obstacle Cooldown category: Blessing multiplier (longer gap between hazards) / Curse multiplier (shorter gap).")]
    public float determinationObstacleCooldownBlessingMultiplier = 1.5f;
    public float determinationObstacleCooldownCurseMultiplier = 0.6f;

    [Header("WASHED PAWS RUN (Integrity) - no passive collection")]
    public bool integrityDisableMagnet = true; // read by a future magnet power-up script

    [Header("DUMPSTER DIVE RUN (Kindness) - more coins, lower score reward")]
    public float kindnessCoinMultiplier = 2f;
    public float kindnessScoreMultiplier = 0.5f;

    [Header("NIGHT PROWLER RUN (Patience) - slower start, but reaches a higher top speed as a reward for lasting")]
    public float patienceStartSpeedMultiplier = 0.75f;
    public float patienceMaxSpeedMultiplier = 2.0f;
    [Tooltip("Pushes the point of fastest speed increase further out (e.g. 1.5 = ramp peaks at 1500 distance instead of 1000), so the climb to top speed takes noticeably longer.")]
    public float patienceRampMidpointMultiplier = 1.6f;
    [Tooltip("Widens the S-curve so the speed increase is more gradual (lower = more gradual). Multiplies rampSteepness, so values below 1 widen it.")]
    public float patienceRampSteepnessMultiplier = 0.6f;
    public float patienceObstacleDensityMultiplier = 0.6f;
    public float patienceRowSpacingMultiplier = 1.4f;

    [Header("Random Event Trigger")]
    [Tooltip("Chance (0-100) that pressing Play rolls into a Seed/Event Run instead of a normal run.")]
    [Range(0f, 100f)]
    public float eventRunChancePercent = 5f;

    [Tooltip("Traits eligible to be picked when the roll succeeds.")]
    public SoulTrait[] eligibleTraits =
    {
        SoulTrait.Bravery, SoulTrait.Justice, SoulTrait.Perseverance,
        SoulTrait.Determination, SoulTrait.Integrity, SoulTrait.Kindness, SoulTrait.Patience
    };

    [Tooltip("Seed id used for the daily/rotating event window. Change this (or generate one) whenever the event rotates.")]
    public string dailySeedId = "daily";

    private void Awake()
    {
        // Standard singleton pattern: first instance in the scene wins and
        // survives scene loads (DontDestroyOnLoad); any duplicate that
        // shows up later (e.g. accidentally placed in more than one scene)
        // destroys itself instead of overwriting the original.
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (instance != this)
        {
            Destroy(gameObject);
        }
    }

    // Call from PlayButton instead of manually picking a trait. Rolls
    // eventRunChancePercent; on success, picks a random trait from
    // eligibleTraits and activates it for the upcoming run. On failure,
    // clears any active trait so the run is normal.
    // Returns true if an event run was triggered.
    public bool RollForEventRun()
    {
        float roll = Random.Range(0f, 100f);

        if (roll < eventRunChancePercent && eligibleTraits != null && eligibleTraits.Length > 0)
        {
            SoulTrait trait = eligibleTraits[Random.Range(0, eligibleTraits.Length)];
            StartEventRun(trait, dailySeedId);
            return true;
        }

        ClearEventRun();
        return false;
    }

    // Call from an event-select menu before loading the run scene.
    public void StartEventRun(SoulTrait trait, string seedId = "default")
    {
        CurrentTrait = trait;
        CurrentSeedId = seedId;

        if (trait == SoulTrait.Determination)
        {
            RollDeterminationOutcome();
        }
        else
        {
            // Explicitly clear any leftover roll from a previous
            // Determination run so nothing can read a stale value, even
            // accidentally, while a different trait is active.
            CurrentDeterminationCategory = DeterminationCategory.None;
            CurrentDeterminationOutcome = DeterminationOutcome.None;
        }

        if (trait == SoulTrait.Justice)
        {
            RollJusticePattern();
        }
    }

    // Generates a fresh random lane pattern for Trickster Run. Called
    // automatically whenever Justice is activated (including via
    // RollForEventRun / RestartButton), so each run gets its own pattern
    // rather than reusing the same one every time - it just repeats
    // consistently for the DURATION of this one run, so it's still
    // learnable within it.
    private void RollJusticePattern()
    {
        int length = Mathf.Max(1, justicePatternLength);
        CurrentJusticePattern = new int[length];

        for (int i = 0; i < length; i++)
        {
            CurrentJusticePattern[i] = Random.Range(0, 3); // lane index: 0=left, 1=mid, 2=right
        }
    }

    // Call before loading a normal (non-event) run so no trait carries over.
    public void ClearEventRun()
    {
        CurrentTrait = SoulTrait.None;
        CurrentSeedId = "default";
        CurrentDeterminationCategory = DeterminationCategory.None;
        CurrentDeterminationOutcome = DeterminationOutcome.None;
    }

    // Convenience check, e.g. `if (EventRunManager.instance.IsActive(SoulTrait.Kindness))`.
    public bool IsActive(SoulTrait trait) => CurrentTrait == trait;

    private static readonly DeterminationCategory[] AllDeterminationCategories =
    {
        DeterminationCategory.Score, DeterminationCategory.Coins, DeterminationCategory.Speed,
        DeterminationCategory.ArmorLives, DeterminationCategory.ObstacleDensity, DeterminationCategory.ObstacleCooldown
    };

    // Picks a new random category AND a random Blessing/Curse outcome for
    // Determination. Called automatically by StartEventRun whenever
    // Determination is activated (including via RollForEventRun /
    // RestartButton), so a fresh roll happens every time the trait comes
    // up again - not just once per seed. Unlike the old system, this has
    // no growth over time - whatever's rolled is flat for the whole run.
    private void RollDeterminationOutcome()
    {
        CurrentDeterminationCategory = AllDeterminationCategories[Random.Range(0, AllDeterminationCategories.Length)];

        float roll = Random.Range(0f, 100f);
        CurrentDeterminationOutcome = roll < determinationBlessingChancePercent
            ? DeterminationOutcome.Blessing
            : DeterminationOutcome.Curse;
    }

    // Score multiplier if Score was rolled, otherwise 1x (no effect).
    public float GetDeterminationScoreMultiplier()
    {
        if (CurrentTrait != SoulTrait.Determination || CurrentDeterminationCategory != DeterminationCategory.Score) return 1f;
        return CurrentDeterminationOutcome == DeterminationOutcome.Blessing
            ? determinationScoreBlessingMultiplier
            : determinationScoreCurseMultiplier;
    }

    // Coin payout multiplier if Coins was rolled, otherwise 1x.
    public float GetDeterminationCoinsMultiplier()
    {
        if (CurrentTrait != SoulTrait.Determination || CurrentDeterminationCategory != DeterminationCategory.Coins) return 1f;
        return CurrentDeterminationOutcome == DeterminationOutcome.Blessing
            ? determinationCoinsBlessingMultiplier
            : determinationCoinsCurseMultiplier;
    }

    // Top-speed multiplier if Speed was rolled, otherwise 1x. Blessing
    // LOWERS top speed (safer), Curse RAISES it (harder to react to).
    public float GetDeterminationSpeedMultiplier()
    {
        if (CurrentTrait != SoulTrait.Determination || CurrentDeterminationCategory != DeterminationCategory.Speed) return 1f;
        return CurrentDeterminationOutcome == DeterminationOutcome.Blessing
            ? determinationSpeedBlessingMultiplier
            : determinationSpeedCurseMultiplier;
    }

    // Bonus armor charges to grant at the start of the run, if ArmorLives
    // was rolled AND it came out a Blessing. The Curse side of this
    // category doesn't grant/remove armor - it removes the graze
    // allowance instead, see DeterminationRemovesGraze().
    public int GetDeterminationArmorLivesBonus()
    {
        if (CurrentTrait != SoulTrait.Determination || CurrentDeterminationCategory != DeterminationCategory.ArmorLives) return 0;
        return CurrentDeterminationOutcome == DeterminationOutcome.Blessing ? determinationArmorLivesBlessingBonus : 0;
    }

    // True only when ArmorLives was rolled AND came out a Curse - checked
    // by PlayerCollision.RegisterHit to skip the normal free-graze
    // allowance entirely, making every hit immediately risky.
    public bool DeterminationRemovesGraze()
    {
        return CurrentTrait == SoulTrait.Determination
            && CurrentDeterminationCategory == DeterminationCategory.ArmorLives
            && CurrentDeterminationOutcome == DeterminationOutcome.Curse;
    }

    // Obstacle spawn-chance multiplier if ObstacleDensity was rolled,
    // otherwise 1x. Blessing = fewer obstacles, Curse = more.
    public float GetDeterminationObstacleDensityMultiplier()
    {
        if (CurrentTrait != SoulTrait.Determination || CurrentDeterminationCategory != DeterminationCategory.ObstacleDensity) return 1f;
        return CurrentDeterminationOutcome == DeterminationOutcome.Blessing
            ? determinationObstacleDensityBlessingMultiplier
            : determinationObstacleDensityCurseMultiplier;
    }

    // Spawn-cooldown multiplier if ObstacleCooldown was rolled, otherwise
    // 1x. Blessing = longer gap between hazards, Curse = shorter.
    public float GetDeterminationObstacleCooldownMultiplier()
    {
        if (CurrentTrait != SoulTrait.Determination || CurrentDeterminationCategory != DeterminationCategory.ObstacleCooldown) return 1f;
        return CurrentDeterminationOutcome == DeterminationOutcome.Blessing
            ? determinationObstacleCooldownBlessingMultiplier
            : determinationObstacleCooldownCurseMultiplier;
    }

    // Human-readable summary of the currently rolled category/outcome, for
    // the overlay - always states exactly what was rolled, no hiding it.
    public string GetDeterminationEffectDescription()
    {
        // Defensive fallback: if CurrentTrait is somehow Determination but
        // nothing's been rolled yet (e.g. CurrentTrait was set directly in
        // the Inspector rather than through StartEventRun/RollForEventRun,
        // which is what normally triggers the roll), roll now instead of
        // just returning blank.
        if (CurrentTrait == SoulTrait.Determination && CurrentDeterminationOutcome == DeterminationOutcome.None)
        {
            RollDeterminationOutcome();
        }

        if (CurrentDeterminationOutcome == DeterminationOutcome.None) return "";

        string label = CurrentDeterminationOutcome == DeterminationOutcome.Blessing ? "Blessing" : "Curse";

        switch (CurrentDeterminationCategory)
        {
            case DeterminationCategory.Score:
                return CurrentDeterminationOutcome == DeterminationOutcome.Blessing
                    ? $"{label}: Score x{determinationScoreBlessingMultiplier:0.##}"
                    : $"{label}: Score x{determinationScoreCurseMultiplier:0.##}";

            case DeterminationCategory.Coins:
                return CurrentDeterminationOutcome == DeterminationOutcome.Blessing
                    ? $"{label}: Coins x{determinationCoinsBlessingMultiplier:0.##}"
                    : $"{label}: Coins x{determinationCoinsCurseMultiplier:0.##}";

            case DeterminationCategory.Speed:
                return CurrentDeterminationOutcome == DeterminationOutcome.Blessing
                    ? $"{label}: Lower top speed (x{determinationSpeedBlessingMultiplier:0.##}, safer)"
                    : $"{label}: Higher top speed (x{determinationSpeedCurseMultiplier:0.##}, harder to react to)";

            case DeterminationCategory.ArmorLives:
                return CurrentDeterminationOutcome == DeterminationOutcome.Blessing
                    ? $"{label}: +{determinationArmorLivesBlessingBonus} bonus armor"
                    : $"{label}: Graze removed, every hit is risky";

            case DeterminationCategory.ObstacleDensity:
                return CurrentDeterminationOutcome == DeterminationOutcome.Blessing
                    ? $"{label}: Fewer obstacles"
                    : $"{label}: More obstacles";

            case DeterminationCategory.ObstacleCooldown:
                return CurrentDeterminationOutcome == DeterminationOutcome.Blessing
                    ? $"{label}: Hazards spawn less often"
                    : $"{label}: Hazards spawn more often";

            default:
                return "";
        }
    }

    // Human-readable name/description for the overlay UI.
    public string GetTraitDisplayName(SoulTrait trait)
    {
        switch (trait)
        {
            case SoulTrait.Bravery: return "Bandit Run";
            case SoulTrait.Justice: return "Trickster Run";
            case SoulTrait.Perseverance: return "Tenacious Tail Run";
            case SoulTrait.Determination: return "Rascal's Gambit Run";
            case SoulTrait.Integrity: return "Washed Paws Run";
            case SoulTrait.Kindness: return "Dumpster Dive Run";
            case SoulTrait.Patience: return "Night Prowler Run";
            default: return "";
        }
    }

    // Static flavor-text blurb per trait, shown in the overlay's "Trait
    // Description" text. Note: for Determination specifically, GameManager
    // overrides this with GetDeterminationEffectDescription() instead, since
    // that one needs to show the actual rolled effect, not a generic line.
    public string GetTraitDescription(SoulTrait trait)
    {
        switch (trait)
        {
            case SoulTrait.Bravery: return "Armored up, but hazards appear more often.";
            case SoulTrait.Justice: return "Same tricks, same traps. Learn the routine and outsmart it every time.";
            case SoulTrait.Perseverance: return "Knocked down? Not for long. Get back up and keep raiding.";
            case SoulTrait.Determination: return "A sly trick up your sleeve, one you won't see until you're already running.";
            case SoulTrait.Integrity: return "Nothing sticks to your paws on its own. Only what you grab yourself counts.";
            case SoulTrait.Kindness: return "Dig deep, haul it all in, just don't expect credit for how far you wandered.";
            case SoulTrait.Patience: return "Move careful, stay unseen. Read every step before you take it.";
            default: return "";
        }
    }

    // Matches the soul colors from the reference chart, for coloring the
    // overlay text (and later, matching trail effects/cosmetics).
    public Color GetTraitColor(SoulTrait trait)
    {
        switch (trait)
        {
            case SoulTrait.Bravery: return HexToColor("#FF7B00");        // orange
            case SoulTrait.Justice: return HexToColor("#FFE100");        // yellow
            case SoulTrait.Perseverance: return HexToColor("#C700F0");   // purple
            case SoulTrait.Determination: return HexToColor("#FF0000");  // red
            case SoulTrait.Integrity: return HexToColor("#0026FF");      // blue
            case SoulTrait.Kindness: return HexToColor("#00E000");       // green
            case SoulTrait.Patience: return HexToColor("#00C3FF");       // cyan
            default: return Color.white;
        }
    }

    private static Color HexToColor(string hex)
    {
        if (ColorUtility.TryParseHtmlString(hex, out Color color))
        {
            return color;
        }
        return Color.white;
    }
}