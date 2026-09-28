using UnityEngine;
using UnityEngine.SceneManagement;

// Tiny static holder remembering which scene the player entered the Shop
// from, so ShopManager's Return button can send them back to the right
// place. A plain static field persists for the whole play session without
// needing a MonoBehaviour/DontDestroyOnLoad singleton - set by whichever
// button loaded the Shop scene (see MainMenuManager.ShopButton and
// GameManager.ShopButton).
public static class ShopReturnContext
{
    public static string ReturnSceneName = "MainMenu"; // sensible default if Shop is ever entered directly
}

// Handles the Shop scene's Return button.
public class ShopManager : MonoBehaviour
{
    // Wire this to the Return button's OnClick().
    public void ReturnButton()
    {
        string returnScene = ShopReturnContext.ReturnSceneName;

        if (returnScene == "World")
        {
            // Came from the game-over screen. Reloading World always
            // re-initializes everything (GameManager, Spawner, the player)
            // fresh, so this naturally starts a brand new run rather than
            // trying to restore the stale game-over state - simpler, and
            // matches what Restart already does. Also re-rolls the
            // Seed/Event Run chance, same as Play/Restart, so a fresh run
            // via the Shop gets its own independent shot at an event trait
            // instead of silently skipping the roll.
            if (EventRunManager.instance != null)
            {
                EventRunManager.instance.RollForEventRun();
            }
        }

        SceneManager.LoadScene(returnScene);
    }
}