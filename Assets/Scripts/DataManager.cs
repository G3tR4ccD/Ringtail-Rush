using UnityEngine;

// Persistent singleton that carries AND SAVES the player's Takeout (coin)
// balance and (later) shop purchases - both across scenes within a play
// session, and across entirely separate sessions (survives fully closing
// and reopening the game). Uses Unity's PlayerPrefs, a simple key-value
// store Unity automatically writes to disk (the Windows Registry, a
// plist on Mac, a file on Linux/mobile - platform-specific, handled for
// us). Marked DontDestroyOnLoad so it survives scene loads within a
// session; PlayerCollision reads/writes TransportValue whenever coins are
// collected or a new run starts.
public class DataManager : MonoBehaviour
{
    // Global access point - other scripts call DataManager.instance.TransportValue
    // instead of needing a scene reference to this object.
    public static DataManager instance;

    // Prefix for every PlayerPrefs key this script writes, so save data
    // doesn't collide with keys any other script might use (e.g. if
    // something else ever calls PlayerPrefs directly for unrelated settings).
    private const string SaveKeyPrefix = "SaveData_";
    private const string TransportValueKey = SaveKeyPrefix + "TransportValue";
    private const string OwnedItemKeyPrefix = SaveKeyPrefix + "Owned_";
    private const string HighScoreKey = SaveKeyPrefix + "HighScore";

    // The actual Takeout total. The setter writes straight to disk every
    // time it changes, so a crash or force-quit can lose at most the most
    // recent single change, not the whole session's earnings.
    private int transportValue;
    public int TransportValue
    {
        get => transportValue;
        set
        {
            transportValue = value;
            PlayerPrefs.SetInt(TransportValueKey, transportValue);
            PlayerPrefs.Save();
        }
    }

    // Best distance/score ever reached, across every past session. Same
    // save-immediately pattern as TransportValue. Use SubmitScore() rather
    // than setting this directly, so the "is this actually a new best"
    // comparison always happens in one place.
    private int highScore;
    public int HighScore
    {
        get => highScore;
        private set
        {
            highScore = value;
            PlayerPrefs.SetInt(HighScoreKey, highScore);
            PlayerPrefs.Save();
        }
    }

    // Call with the current run's score (e.g. every frame, or just at
    // game over) - updates and saves HighScore only if this one actually
    // beats it. Returns true the moment it becomes a new best, so callers
    // can trigger a "NEW BEST!" indicator if wanted.
    public bool SubmitScore(int score)
    {
        if (score > highScore)
        {
            HighScore = score;
            return true;
        }

        return false;
    }

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

            // Load whatever was saved from a previous session. Runs only
            // once, on the very first DataManager to ever exist this
            // session - a duplicate that gets destroyed above never needs
            // to load anything since it's never used.
            transportValue = PlayerPrefs.GetInt(TransportValueKey, 0);
            highScore = PlayerPrefs.GetInt(HighScoreKey, 0);
        }
        else if (instance != this)
        {
            Destroy(gameObject);
        }
    }

    // --- Shop purchases ---
    // Each purchasable item (skin, trail, upgrade, etc.) is identified by
    // a unique string ID you choose (e.g. "skin_red_panda", "trail_confetti").
    // Ownership is stored as its own PlayerPrefs key, so items can be
    // checked/unlocked independently without needing a predefined list of
    // every possible item up front. Call these once the shop's purchase
    // flow actually exists.

    // True if the player has already bought/unlocked this item.
    public bool IsItemOwned(string itemId)
    {
        return PlayerPrefs.GetInt(OwnedItemKeyPrefix + itemId, 0) == 1;
    }

    // Marks an item as owned (or revokes it, though that's an unusual case -
    // mainly here for symmetry/testing). Saves immediately, same as TransportValue.
    public void SetItemOwned(string itemId, bool owned)
    {
        PlayerPrefs.SetInt(OwnedItemKeyPrefix + itemId, owned ? 1 : 0);
        PlayerPrefs.Save();
    }

    // Wipes every bit of save data this script has ever written (Takeout
    // balance + all owned items) and resets the in-memory balance to
    // match. Useful for a "reset save" debug/settings option later.
    // NOTE: PlayerPrefs.DeleteAll() wipes EVERYTHING saved via PlayerPrefs
    // by this entire project, not just DataManager's own keys - if any
    // other script ever starts using PlayerPrefs directly for something
    // unrelated (e.g. audio/graphics settings), swap this for deleting
    // only this script's own known keys instead.
    public void ResetAllSaveData()
    {
        PlayerPrefs.DeleteAll();
        transportValue = 0;
        highScore = 0;
    }
}