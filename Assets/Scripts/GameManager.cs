using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Lives in the World scene (NOT persistent - gets destroyed/recreated with
// it on every load/restart, unlike EventRunManager/DataManager). Handles
// the score/distance HUD, the game-over screen, the Seed/Event Run overlay
// banner, and all the scene-navigation buttons (Restart/MainMenu/Shop/Quit).
public class GameManager : MonoBehaviour
{
    // The game-over panel, shown via GameOver() when PlayerCollision ends the run.
    public GameObject gameOverUI;

    // The player's Transform, used to read distance traveled (position.z) for the score.
    public Transform player;

    // Score number HUD text (no "Distance:" label), updated every frame in Update().
    public TextMeshProUGUI scoreText;

    [Header("High Score")]
    [Tooltip("Optional. Shows the best score ever reached (persisted via DataManager, survives closing the game). Updates live the moment the current run's score passes it, not just at game over.")]
    public TextMeshProUGUI highScoreText;
    [Tooltip("Text shown before the number, e.g. \"Highest Score: \" or \"Best: \".")]
    public string highScoreLabel = "";

    [Header("Event Run Overlay")]
    [Tooltip("Drag in the 'Trait Name' parent object. It needs a TMP text component on itself (for the trait name) and a child named exactly 'Trait Description' with its own TMP text component. Turned on (like gameOverUI) only when the run is a Seed/Event Run.")]
    public GameObject eventTraitPanel;
    [Tooltip("How long the overlay stays fully visible before it starts fading out.")]
    public float eventOverlayVisibleDuration = 4f;
    [Tooltip("How long the fade-out itself takes, once it starts.")]
    public float eventOverlayFadeDuration = 1f;

    private CanvasGroup eventTraitPanelCanvasGroup;

    [Header("Determination Status (always visible, no fade)")]
    [Tooltip("Optional. Separate from the event overlay banner above - shown ONLY during a Rascal's Gambit (Determination) run, and stays visible for the WHOLE run (no fade timer). States exactly which blessing/curse is active.")]
    public TextMeshProUGUI determinationStatusText;

    [Header("Pause")]
    [Tooltip("Structured like gameOverUI, but with a Continue/Resume button instead of Restart. Shown when the player presses Escape mid-run.")]
    public GameObject pauseUI;

    private bool isPaused = false;

    [Header("Start Screen")]
    [Tooltip("Shown at the very start of a run, before the player has control - press any button to begin. Structured like gameOverUI/pauseUI. Leave empty to skip this requirement entirely (run starts immediately, as before).")]
    public GameObject startScreenUI;

    private Movement playerMovement;
    private bool runStarted = false;

    public void Start()

    {
        //Hides the cursor and locks it to the center of the screen
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Shows the event overlay banner if EventRunManager rolled a trait
        // for this run (see PlayButton/RestartButton in this same file).
        SetupEventOverlay();

        // Separate, permanent (non-fading) status text - only for Determination.
        SetupDeterminationStatusText();

        // Show whatever the saved best is, before this run has had a
        // chance to beat it (Update() takes over from here).
        UpdateHighScoreText();

        // Requires "any button" input before the run actually begins.
        // While waiting, the player has no control at all (Movement is
        // disabled outright, so its Update() never runs) and plays a
        // dedicated idle animation instead.
        playerMovement = player != null ? player.GetComponent<Movement>() : null;

        if (startScreenUI != null)
        {
            startScreenUI.SetActive(true);

            if (playerMovement != null)
            {
                playerMovement.PlayStartScreenAnimation();
                playerMovement.enabled = false;
            }
        }
        else
        {
            runStarted = true; // no start screen configured - begin immediately, as before
        }
    }

    // True the first frame any keyboard key or mouse button is pressed -
    // used to detect "press any button" to begin the run.
    private bool AnyButtonPressed()
    {
        if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) return true;
        if (Mouse.current != null && (Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame)) return true;
        return false;
    }

    // Hides the start screen and hands control back to the player.
    private void BeginRun()
    {
        runStarted = true;

        if (startScreenUI != null)
        {
            startScreenUI.SetActive(false);
        }

        if (playerMovement != null)
        {
            // Forces the transition out of the start-screen idle pose into
            // running - must happen before re-enabling Movement below (or
            // at least in this order), since PlayRunningAnimation() still
            // works fine on a disabled component (only Unity's own
            // callback methods are skipped when disabled, not regular
            // method calls), and this way the Animator is already
            // correctly set before Update() starts running again.
            playerMovement.PlayRunningAnimation();
            playerMovement.enabled = true;
        }
    }

    // Refreshes the high score HUD text from DataManager's current value.
    private void UpdateHighScoreText()
    {
        if (highScoreText == null || DataManager.instance == null) return;

        highScoreText.text = highScoreLabel + DataManager.instance.HighScore.ToString("0");
    }

    // Turns eventTraitPanel on/off based on whatever EventRunManager decided
    // (via RollForEventRun) before this scene loaded, and fills in/colors
    // the "Trait Name" text (on the panel itself) and the "Trait
    // Description" text (on its child).
    private void SetupEventOverlay()
    {
        if (eventTraitPanel == null) return;

        bool isEventRun = EventRunManager.instance != null && EventRunManager.instance.CurrentTrait != SoulTrait.None;
        eventTraitPanel.SetActive(isEventRun);

        if (!isEventRun) return;

        SoulTrait trait = EventRunManager.instance.CurrentTrait;
        Color traitColor = EventRunManager.instance.GetTraitColor(trait);

        TextMeshProUGUI nameText = eventTraitPanel.GetComponent<TextMeshProUGUI>();
        if (nameText != null)
        {
            nameText.text = EventRunManager.instance.GetTraitDisplayName(trait);
            nameText.color = traitColor;
        }

        Transform descriptionTransform = eventTraitPanel.transform.Find("Trait Description");
        TextMeshProUGUI descriptionText = descriptionTransform != null ? descriptionTransform.GetComponent<TextMeshProUGUI>() : null;
        if (descriptionText != null)
        {
            // Determination's description is dynamic - it shows whichever
            // effect was randomly rolled for this run, not a static blurb.
            descriptionText.text = trait == SoulTrait.Determination
                ? EventRunManager.instance.GetDeterminationEffectDescription()
                : EventRunManager.instance.GetTraitDescription(trait);
            descriptionText.color = traitColor;
        }

        // Fade the whole panel out (title + description together) after a
        // delay, rather than leaving the announcement on screen all run.
        eventTraitPanelCanvasGroup = eventTraitPanel.GetComponent<CanvasGroup>();
        if (eventTraitPanelCanvasGroup == null)
        {
            eventTraitPanelCanvasGroup = eventTraitPanel.AddComponent<CanvasGroup>();
        }
        eventTraitPanelCanvasGroup.alpha = 1f;

        StartCoroutine(FadeOutEventOverlay());
    }

    // Shows determinationStatusText only during a Determination run, and
    // leaves it visible for the whole run - unlike SetupEventOverlay's
    // banner above, this never fades out. A persistent reminder of which
    // blessing/curse is currently active.
    private void SetupDeterminationStatusText()
    {
        if (determinationStatusText == null) return;

        bool isDeterminationRun = EventRunManager.instance != null && EventRunManager.instance.CurrentTrait == SoulTrait.Determination;
        determinationStatusText.gameObject.SetActive(isDeterminationRun);

        if (!isDeterminationRun) return;

        determinationStatusText.text = EventRunManager.instance.GetDeterminationEffectDescription();
        determinationStatusText.color = EventRunManager.instance.GetTraitColor(SoulTrait.Determination);
    }

    // Waits eventOverlayVisibleDuration, then fades eventTraitPanel's alpha
    // to 0 over eventOverlayFadeDuration, and deactivates it once fully
    // transparent.
    private System.Collections.IEnumerator FadeOutEventOverlay()
    {
        yield return new WaitForSeconds(eventOverlayVisibleDuration);

        float elapsed = 0f;
        while (elapsed < eventOverlayFadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = eventOverlayFadeDuration > 0f ? elapsed / eventOverlayFadeDuration : 1f;
            eventTraitPanelCanvasGroup.alpha = Mathf.Lerp(1f, 0f, t);
            yield return null;
        }

        eventTraitPanelCanvasGroup.alpha = 0f;
        eventTraitPanel.SetActive(false);
    }

    // Runs every frame: updates the distance/score HUD text, applying
    // Kindness's reduced score or Determination's Score category (if rolled).
    void Update()
    {
        // Waiting on the start screen - only check for input to begin,
        // skip score/pause logic entirely until the run actually starts.
        if (!runStarted)
        {
            if (AnyButtonPressed())
            {
                BeginRun();
            }
            return;
        }

        if (player == null) return;

        float distanceScore = player.position.z;

        // Kindness seed runs favor collecting over distance, so score is reduced.
        if (EventRunManager.instance != null)
        {
            if (EventRunManager.instance.CurrentTrait == SoulTrait.Kindness)
            {
                distanceScore *= EventRunManager.instance.kindnessScoreMultiplier;
            }
            else if (EventRunManager.instance.CurrentTrait == SoulTrait.Determination)
            {
                // Returns 1x if Score wasn't the rolled category.
                distanceScore *= EventRunManager.instance.GetDeterminationScoreMultiplier();
            }
        }

        scoreText.text = distanceScore.ToString("0");

        // Live-updates HighScore (and its saved value) the moment this
        // run's score actually passes it - SubmitScore is a no-op
        // whenever it doesn't, so this is safe to call every frame.
        if (DataManager.instance != null && DataManager.instance.SubmitScore(Mathf.RoundToInt(distanceScore)))
        {
            UpdateHighScoreText();
        }

        HandlePauseInput();
    }

    // Checks for the Escape key each frame and toggles the pause overlay.
    // Ignored once the game-over screen is already showing (checking for
    // Escape there doesn't make sense - the run's already over), and
    // Update() still runs normally even while paused (Time.timeScale only
    // affects deltaTime/physics, not the Update loop itself), so this can
    // detect Escape being pressed again to resume.
    private void HandlePauseInput()
    {
        if (gameOverUI != null && gameOverUI.activeSelf) return;
        if (Keyboard.current == null) return;

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (isPaused)
            {
                ResumeButton();
            }
            else
            {
                PauseGame();
            }
        }
    }

    // Freezes the game (Time.timeScale = 0, so all deltaTime-driven
    // movement/animation naturally stops) and shows the pause overlay.
    private void PauseGame()
    {
        isPaused = true;
        Time.timeScale = 0f;

        if (pauseUI != null)
        {
            pauseUI.SetActive(true);
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    // Wire this to the pause overlay's Continue/Resume button (also
    // triggered by pressing Escape again).
    public void ResumeButton()
    {
        isPaused = false;
        Time.timeScale = 1f;

        if (pauseUI != null)
        {
            pauseUI.SetActive(false);
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    //Restart the game by rerolling the seed/event run chance, then reloading the current scene
    public void RestartButton()
    {
        Time.timeScale = 1f; // in case Restart is pressed from the pause menu

        if (EventRunManager.instance != null)
        {
            EventRunManager.instance.RollForEventRun();
        }

        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    //Load the main menu scene
    public void MainMenuButton()
    {
        Time.timeScale = 1f; // in case Main Menu is pressed from the pause menu
        SceneManager.LoadScene("MainMenu");
    }

    //Quit the application
    public void QuitButton()
    {
        Application.Quit();
    }

    //Load the shop scene
    public void ShopButton()
    {
        Time.timeScale = 1f; // in case Shop is pressed from the pause menu
        ShopReturnContext.ReturnSceneName = "World";
        SceneManager.LoadScene("Shop");
    }
    //Display the game over UI and unlock the cursor
    public void GameOver()
    {
        gameOverUI.SetActive(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}