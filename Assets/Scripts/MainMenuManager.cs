using UnityEngine;
using UnityEngine.SceneManagement;

// Button handlers for the Main Menu scene's UI. Each public method here is
// meant to be wired to a Button's OnClick() event in the Inspector.
public class MainMenuManager : MonoBehaviour
{
    // Loads the World scene to start a run.
    public void PlayButton()
    {
        // Rolls EventRunManager's inspector-set % chance. On success this
        // activates a random Seed/Event Run trait for the upcoming run;
        // otherwise it clears any leftover trait so the run is normal.
        // The World scene's overlay (see GameManager) picks up the result.
        if (EventRunManager.instance != null)
        {
            EventRunManager.instance.RollForEventRun();
        }

        SceneManager.LoadScene("World");
    }

    // Loads the Shop scene, where players spend Takeout on skins/upgrades/etc.
    public void ShopButton()
    {
        ShopReturnContext.ReturnSceneName = "MainMenu";
        SceneManager.LoadScene("Shop");
    }

    // Closes the game. Only has an effect in a built/standalone player -
    // does nothing in the Unity Editor (Application.Quit() is a no-op there).
    public void QuitButton()
    {
        Application.Quit();
    }
}