using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

// Drives all player movement: forward speed (with the difficulty ramp
// curve), lane-changing (dodge left/right), jumping, and playing the
// matching Animator triggers. Also applies Seed/Event Run speed/curve
// modifiers (Bravery, Justice, Patience, Determination) on top of the base
// values. PlayerCollision (Collision.cs) calls into this for hit reactions
// (GrazeBounce/SurviveHit) and to end the run (TriggerGameOverAnimation).
public class Movement : MonoBehaviour
{
    // Optional Rigidbody, only used to freeze physics on game over (see
    // TriggerGameOver) - the character's actual movement is entirely
    // CharacterController-driven, not Rigidbody-driven.
    public Rigidbody rb;
    private Animator animator;

    [Header("Speed Ramp")]
    [Tooltip("Starting (minimum) forward speed, at the very beginning of a run.")]
    public float speed = 8f;
    [Tooltip("Top speed the curve approaches at very long distances.")]
    public float maxSpeed = 25f;
    [Tooltip("Distance (Z) where speed is increasing fastest - the midpoint of the S-curve. Was 1000 by default.")]
    public float rampMidpoint = 1000f;
    [Tooltip("Controls how wide/narrow the ramp-up is. Lower = more gradual (wider curve), higher = more sudden (steeper curve).")]
    public float rampSteepness = 0.005f;

    private float referenceSpeedForAnimation = 12.5f; // movement speed at which the run animation should play at its normal (1x) rate
    private float minAnimationSpeed = 0.3f; // floor so the run animation never looks frozen at low speeds
    private float jumpHeight = 1.5f; // peak height of a jump, in units - used to derive the initial upward velocity
    private float gravity = -9.8f; // downward acceleration applied to velocity.y every frame
    private float laneChangeSpeed = 15f; // how fast the player slides to the target lane

    // The 3 lane X-positions the player can occupy. currentLane/previousLane
    // are indices into this array (0=left, 1=mid, 2=right).
    private readonly float[] lanes = { -3.15f, 0f, 3.15f };
    private int currentLane = 1; // start in the middle lane
    private int previousLane = 1; // lane to revert to on a graze hit

    private CharacterController controller;
    private Vector3 moveInput; // raw input from OnMove, only the X component (left/right) is used
    private Vector3 velocity; // only velocity.y is actually used - vertical speed for jumping/falling

    private bool isGameOver = false; // set once the run ends; most methods early-return once this is true

    // Animation priority tiers - a higher tier can interrupt a lower one,
    // but never the reverse. Matches the required order: Rac_Lie to Sleep
    // (game over) > Rac_Jump in Place > Rac_GetHit Front Left1 >
    // Rac_Dodge Front Left/Right > Rac_Run Forward (default/lowest).
    private const int PriorityRun = 0;
    private const int PriorityDodge = 1;
    private const int PriorityHit = 2;
    private const int PriorityJump = 3;
    private const int PriorityGameOver = 4;

    // Tracks which tier is currently playing, so a lower-priority
    // animation request can't interrupt a higher one already in progress.
    // Reset back to PriorityRun automatically once the current one-shot
    // animation finishes and the Animator settles back into Locomotion
    // (see the check in Update()).
    private int currentAnimationPriority = PriorityRun;

    // Animator parameter hashes, cached once at Start() instead of using
    // the string name every call (StringToHash avoids a string comparison
    // on every SetTrigger/ResetTrigger).
    private int dodgeLeftHash;
    private int dodgeRightHash;
    private int jumpHash;
    private int gameOverHash; // add this
    private int hitHash;

    [Tooltip("Name of the Animator STATE (not the trigger parameter) that plays the jump animation. Used to force an instant, non-blended cut into it when jump interrupts an already-playing dodge/hit animation, instead of going through the Controller's normal crossfade - with Root Motion enabled, blending two clips' root motion together during a transition can shave off some of the jump's actual height. Must match the exact state name in your Animator Controller.")]
    public string jumpStateName = "Rac_Jump in Place";

    [Header("Dumpster Dive Power-Up")]
    [Tooltip("Garbage bin prefab spawned during Dumpster Dive. NOT parented to the player - a floor collider that's a child of the player's own hierarchy wouldn't register as valid ground for the player's OWN CharacterController to land on. Instead this follows the player's X/Z each frame (see LateUpdate) while staying at a fixed world Y, so the player can land on its floor collider (tag it 'Ground') via completely normal physics.")]
    public GameObject dumpsterBinPrefab;
    [Tooltip("X/Z offset from the player for the spawned bin (e.g. to sit it slightly behind them). Y is ignored here - see Dumpster Dive Sit Height below.")]
    public Vector3 dumpsterBinOffset = Vector3.zero;
    [Tooltip("Extra delay (seconds) AFTER the player becomes airborne before the bin actually appears - gives the jump-in animation more time to be clearly visible before it's spawned.")]
    public float dumpsterBinAppearDelay = 0.3f;
    [Tooltip("Animator BOOL parameter name toggled true while 'sitting in the bin', false otherwise. Kept true for the whole sitting phase so your Animator Controller's own transitions can use it as a condition for returning to Sitting after Dodge interrupts it - Dodge should still be able to interrupt and return to it normally through your own transitions, same as it already does with Locomotion.")]
    public string dumpsterSittingBoolName = "IsDumpsterDiving";
    [Tooltip("Name of the Animator STATE (not the bool parameter) that plays the sitting pose. Used to force an instant, guaranteed transition into it the moment the sitting phase starts, rather than relying solely on the bool - since the bool gets set right when controller.isGrounded becomes true (a physics event), the Animator could still technically be mid-transition out of Jump at that exact moment, with no valid graph path from there straight into Sitting. Must match the exact state name in your Animator Controller.")]
    public string sittingStateName = "Sit_Mount";

    [Header("Start Screen")]
    [Tooltip("Name of the Animator STATE for the idle pose shown while waiting on the start screen, before the run begins. Played via PlayStartScreenAnimation(), called externally by GameManager.")]
    public string startScreenStateName = "Rac_Lie 2";
    [Tooltip("Name of the Animator STATE for normal running/locomotion. Used to force an instant, guaranteed transition out of Start Screen State Name the moment the run begins - relying on the Controller's own transition graph wasn't reliable here for the same reason it wasn't for Jump/Sitting (see their tooltips).")]
    public string runningStateName = "Rac_Run Forward";
    [Tooltip("How long the transition from Start Screen State Name into Running takes, in seconds - smoothly blends rather than instantly swapping. Unlike Jump/Sitting's forced instant cuts, there's no Root Motion/height conflict here, so a blend is safe to use.")]
    public float startScreenExitBlendDuration = 0.3f;
    [Tooltip("How long AFTER the run begins before forward motion actually starts being applied. Independent of Start Screen Exit Blend Duration above - can be set longer if the character still looks like they're getting up/transitioning even after the animation blend itself has finished.")]
    public float forwardMotionDelay = 0.5f;
    // Set in OnEnable() to Time.time + forwardMotionDelay - forward
    // motion is suppressed (see Update()) until this time passes, so the
    // character doesn't start sliding forward while still visually
    // transitioning out of the lying pose.
    private float forwardMotionSuppressedUntil = -999f;

    private int dumpsterSittingHash;
    private bool jumpDisabledForDumpsterDive = false;
    private GameObject activeDumpsterBin;
    private Coroutine dumpsterDiveCoroutine;
    private float dumpsterDiveEndTime = -999f;
    [Tooltip("Y the player rises to from ground level and holds at for the sitting phase. The bin stays at ground level (Y=0) the whole time - only the player moves.")]
    public float dumpsterDiveSitHeight = 1f;
    [Tooltip("How long the player takes to rise from ground level up to Dumpster Dive Sit Height when the power-up starts, and to descend back down when it ends.")]
    public float dumpsterDiveRiseDuration = 0.5f;
    // True for the whole rise-in through descend-out sequence - while
    // true, Update() skips its normal gravity/velocity-driven vertical
    // motion entirely, since the DumpsterDiveRoutine/RiseOrDescend
    // coroutine has full direct authority over the player's Y instead.
    private bool dumpsterDiveControllingY = false;
    [Tooltip("Speed multiplier applied for the WHOLE Dumpster Dive sequence (rise-in through descend-out), on top of the normal speed curve - e.g. 5 = 5x normal speed.")]
    public float dumpsterDiveSpeedMultiplier = 5f;
    private bool dumpsterDiveSpeedBoostActive = false;

    // Multiplies the base/max speed curve for Seed/Event Runs (Bravery
    // speeds it up; Determination's Speed category, if rolled, applies its
    // flat Blessing/Curse multiplier here too). Stays 1x for normal runs.
    private float eventSpeedMultiplier = 1f;

    // Fires every time this component becomes enabled - specifically the
    // moment GameManager re-enables Movement after the start screen (see
    // BeginRun), but this works automatically for any future case that
    // re-enables Movement too. Starts the forward-motion suppression
    // window so the character doesn't immediately start moving forward
    // while still visually mid-blend from Start Screen State Name into
    // Running.
    void OnEnable()
    {
        forwardMotionSuppressedUntil = Time.time + forwardMotionDelay;
    }

    // Reference caching lives here specifically (not Start()) - Unity
    // guarantees EVERY script's Awake() runs before ANY script's Start()
    // begins, system-wide, regardless of GameObject/component order. This
    // matters because GameManager calls PlayStartScreenAnimation() (and
    // later PlayRunningAnimation()) on this component during ITS OWN
    // Start() - if animator/controller were still cached here in Start()
    // instead, and GameManager's Start() happened to run before this
    // one's, those calls would silently do nothing (their null checks
    // would just quietly fail), which was causing the start screen
    // animation to not show immediately and the post-start transition
    // back to running to not take effect either.
    void Awake()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();

        if (animator != null)
        {
            // Cache each trigger parameter's hash once, matching the exact
            // parameter names set up on the Animator Controller.
            dodgeLeftHash = Animator.StringToHash("dodgeLeft");
            dodgeRightHash = Animator.StringToHash("dodgeRight");
            jumpHash = Animator.StringToHash("jumpTrigger");
            gameOverHash = Animator.StringToHash("gameOverTrigger");
            hitHash = Animator.StringToHash("hitTrigger");
            dumpsterSittingHash = Animator.StringToHash(dumpsterSittingBoolName);
        }
    }

    void Start()
    {
        ApplyEventModifiers();
    }

    // Reads the active soul trait (if any) from EventRunManager and adjusts
    // the speed curve accordingly. Safe to call even if no EventRunManager
    // exists in the scene (normal runs).
    private void ApplyEventModifiers()
    {
        if (EventRunManager.instance == null) return;

        switch (EventRunManager.instance.CurrentTrait)
        {
            case SoulTrait.Bravery:
                eventSpeedMultiplier *= EventRunManager.instance.braverySpeedMultiplier;
                break;
            case SoulTrait.Justice:
                // Only the ceiling is raised - starting speed and the ramp
                // curve shape are untouched, since the reward here is for
                // mastering the fixed pattern over a long run, not an
                // easier/harder start.
                maxSpeed *= EventRunManager.instance.justiceMaxSpeedMultiplier;
                break;
            case SoulTrait.Patience:
                // Scaled separately (not via eventSpeedMultiplier) so the
                // slower start doesn't also drag down the top speed - a
                // Patience run should ramp up slower but reward sticking
                // it out with a HIGHER ceiling than a normal run.
                speed *= EventRunManager.instance.patienceStartSpeedMultiplier;
                maxSpeed *= EventRunManager.instance.patienceMaxSpeedMultiplier;

                // rampMidpoint/rampSteepness control the CURVE SHAPE, not
                // just the speed values - without touching these, a bigger
                // maxSpeed on the same steep curve just made the ramp-up
                // feel even more abrupt (still hitting top speed around
                // the same ~1000 distance). Push the midpoint out and widen
                // the curve so the climb is genuinely more gradual.
                rampMidpoint *= EventRunManager.instance.patienceRampMidpointMultiplier;
                rampSteepness *= EventRunManager.instance.patienceRampSteepnessMultiplier;
                break;
            case SoulTrait.Determination:
                // Flat for the whole run - Blessing lowers top speed
                // (safer), Curse raises it (harder to react to). Returns
                // 1x if Speed wasn't the rolled category, so this is safe
                // to always apply.
                eventSpeedMultiplier *= EventRunManager.instance.GetDeterminationSpeedMultiplier();
                break;
        }
    }

    // True while the Animator is in a state tagged "Busy" (Jump, DodgeLeft,
    // DodgeRight, Hit), OR currently transitioning INTO one. Checking only
    // the current state missed the transition/crossfade period itself -
    // animator.speed would stay scaled down by movement speed for the
    // entire blend into the new animation (since the current state was
    // still Locomotion until the transition fully completed), making the
    // crossfade visibly play out in slow motion at low speeds. Also used
    // to stop new dodge/jump input from queuing up behind a one-shot
    // animation that's still playing.
    private bool IsAnimationBusy()
    {
        if (animator == null) return false;

        if (animator.GetCurrentAnimatorStateInfo(0).IsTag("Busy")) return true;

        if (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsTag("Busy")) return true;

        return false;
    }

    // True only while the Hit reaction is the CURRENTLY active animation
    // tier (not once Jump has interrupted it - that's a separate,
    // already-resolved situation). Used by PlayerCollision to ignore any
    // new hit entirely while still visually reacting to the previous one,
    // so the player can't die (or take another hit) mid-animation without
    // ever seeing it coming.
    public bool IsPlayingHitReaction()
    {
        return currentAnimationPriority == PriorityHit;
    }

    // Bound to the Input System's Move action - fires whenever the player
    // pushes the movement stick/keys left or right, used here purely as a
    // "dodge one lane" trigger rather than continuous analog movement.
    public void OnMove(InputAction.CallbackContext context)
    {
        if (isGameOver) return;
        moveInput = context.ReadValue<Vector2>();

        // Only react on the initial press, not every frame the stick is held
        if (context.started)
        {
            if (moveInput.x < -0.1f)
            {
                TryChangeLane(-1);

                // Lane change always happens instantly regardless of animation
                // state, but the dodge ANIMATION only makes sense while grounded -
                // there's no air-dodge pose, so playing it mid-jump looked wrong.
                // Also gated by priority - Dodge is the lowest one-shot tier,
                // so it can't interrupt a Hit reaction currently playing.
                if (animator != null && controller.isGrounded && PriorityDodge >= currentAnimationPriority)
                {
                    animator.ResetTrigger(dodgeRightHash);
                    // Deliberately does NOT reset jumpHash. If jump and dodge
                    // land in the same frame, controller.isGrounded is still
                    // true here (the jump hasn't moved the character
                    // positionally yet, only set velocity.y/queued its
                    // trigger) - resetting jumpHash in that instant would
                    // cancel the jump animation before it ever plays, and
                    // with Root Motion enabled, the Jump clip's own
                    // contribution to vertical movement would be lost,
                    // reducing the actual jump height even though
                    // velocity.y itself was set correctly.
                    animator.SetTrigger(dodgeLeftHash);
                    currentAnimationPriority = PriorityDodge;
                }
            }
            else if (moveInput.x > 0.1f)
            {
                TryChangeLane(1);

                if (animator != null && controller.isGrounded && PriorityDodge >= currentAnimationPriority)
                {
                    animator.ResetTrigger(dodgeLeftHash);
                    animator.SetTrigger(dodgeRightHash);
                    currentAnimationPriority = PriorityDodge;
                }
            }
        }
    }

    // Moves currentLane by +1/-1 if the result is still a valid lane index
    // (does nothing at either edge - you can't dodge off the left/right
    // lanes). Records the lane you were in as previousLane, which
    // GrazeBounce reverts to on a forgiven hit.
    private void TryChangeLane(int direction)
    {
        int newLane = currentLane + direction;
        if (newLane >= 0 && newLane < lanes.Length)
        {
            previousLane = currentLane;
            currentLane = newLane;
        }
    }

    // Bound to the Input System's Jump action. Only works while grounded
    // (no double-jump/air-jump). Sets the initial upward velocity using the
    // standard v = sqrt(h * -2g) formula so the character reaches exactly
    // jumpHeight before gravity pulls it back down.
    public void OnJump(InputAction.CallbackContext context)
    {
        if (isGameOver) return;
        if (jumpDisabledForDumpsterDive) return; // can't jump while sitting in the Dumpster Dive bin - dodging still works, this only blocks jump

        // Normally requires controller.isGrounded, but ALSO allowed while
        // a Hit reaction is playing even if isGrounded happens to read
        // false in that moment - with Root Motion enabled, the Hit clip's
        // own reactive motion (a flinch/stumble) can occasionally lift the
        // CharacterController's grounded check for an instant even though
        // the player hasn't actually left the ground gameplay-wise, which
        // was blocking jump (and its physics velocity) entirely while a
        // hit reaction played.
        bool canJump = controller.isGrounded || currentAnimationPriority == PriorityHit;

        if (context.performed && canJump)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);

            // Always fire in sync with the physics jump, even if a dodge/hit
            // animation is still playing - previously this was skipped
            // entirely while IsAnimationBusy() was true, so the character
            // could launch into the air with no jump animation ever playing,
            // making it look like the jump had already finished by the time
            // the animator caught up. Jump is the second-highest priority
            // tier (only game over outranks it), so this always wins over
            // a currently-playing Hit or Dodge - the controller.isGrounded
            // requirement above already rules out interrupting an
            // in-progress jump with another jump.
            if (animator != null)
            {
                animator.ResetTrigger(dodgeLeftHash);
                animator.ResetTrigger(dodgeRightHash);
                // Also clear any pending Hit trigger. SetTrigger() only
                // gets consumed once an actual transition edge uses it as
                // a condition - the Play() call below bypasses the
                // transition graph entirely when interrupting, so it never
                // consumes a queued Hit trigger. Left unset, that trigger
                // would just sit there and fire LATE once the Animator
                // eventually settles back into Locomotion (e.g. after the
                // player stops jumping) - even though the hit reaction was
                // meant to be skipped entirely, not delayed.
                animator.ResetTrigger(hitHash);

                if (IsAnimationBusy())
                {
                    // Interrupting an already-playing non-Locomotion
                    // animation (e.g. jump pressed shortly after a dodge
                    // that's still mid-clip). SetTrigger would go through
                    // the Controller's normal crossfade, blending the two
                    // clips' root motion together for the transition's
                    // duration - if the dodge clip has ANY root motion of
                    // its own, that blend can shave off some of the jump's
                    // actual height. Play() cuts instantly into the Jump
                    // state instead, skipping the blend entirely.
                    animator.Play(jumpStateName, 0, 0f);
                }
                else
                {
                    animator.SetTrigger(jumpHash);
                }

                currentAnimationPriority = PriorityJump;
            }
        }
    }

    // Runs every frame: handles lane-sliding, gravity/jump physics, the
    // difficulty speed ramp (+ any active event-run modifiers), animator
    // speed syncing, and the single combined CharacterController.Move()
    // call. Also the fall-out-of-world safety net at the bottom.
    private void Update()
    {
        if (isGameOver) return;

        // Once a one-shot animation (Jump/Hit/Dodge) finishes and the
        // Animator has settled back into Locomotion, clear the priority
        // lock so the next action - of any tier - is free to play.
        if (!IsAnimationBusy())
        {
            currentAnimationPriority = PriorityRun;
        }

        // Slide horizontally toward the target lane's X position
        float targetX = lanes[currentLane];
        float currentX = transform.position.x;
        float newX = Mathf.MoveTowards(currentX, targetX, laneChangeSpeed * Time.deltaTime);
        float deltaX = newX - currentX;

        // While grounded, clamp the downward velocity to a small constant instead of
        // letting gravity accumulate indefinitely. Without this, velocity.y keeps
        // growing more negative every frame the player is on the ground, forcing the
        // CharacterController to push down harder than it needs each Move() call and
        // fight its own ground collision correction - which was the source of the jitter.
        //
        // While a Dumpster Dive rise/sit/descend sequence is directly
        // controlling Y (see RiseOrDescend/SetPlayerY), skip normal
        // gravity/grounded handling entirely - otherwise this would fight
        // the direct position sets happening there.
        if (!dumpsterDiveControllingY)
        {
            if (controller.isGrounded && velocity.y < 0f)
            {
                velocity.y = -2f;
            }

            velocity.y += gravity * Time.deltaTime;
        }

        // Difficulty ramp: speed follows a sigmoid (S-curve) based on distance traveled.
        // Its rate of increase is bell-curve-shaped - slow near the start, fastest
        // around rampMidpoint, then tapering off as it approaches maxSpeed - rather
        // than climbing forever at a constant rate.
        float dumpsterSpeedFactor = dumpsterDiveSpeedBoostActive ? dumpsterDiveSpeedMultiplier : 1f;
        float currentSpeed = (speed + (maxSpeed - speed) / (1f + Mathf.Exp(-rampSteepness * (transform.position.z - rampMidpoint)))) * eventSpeedMultiplier * dumpsterSpeedFactor;

        // Scale the run animation's playback speed to match, so it visibly speeds
        // up as the difficulty ramps instead of the character's legs staying at a
        // fixed animation rate while actually moving faster.
        // Only applied while the run/locomotion state is playing - dodge, jump,
        // hit, and game-over are one-shot animations tagged "Busy" and always
        // play at normal (1x) speed, independent of movement speed.
        if (animator != null)
        {
            animator.speed = IsAnimationBusy()
                ? 1f
                : Mathf.Max(minAnimationSpeed, currentSpeed / referenceSpeedForAnimation);
        }

        // Applied as two SEPARATE Move() calls, resolved one after the
        // other within this SAME Update() (not split across different
        // update loops/rates - that was the actual cause of the old jitter
        // this section used to have, per the history below). A single
        // COMBINED diagonal Move() (lateral + vertical + forward all at
        // once) has a real CharacterController quirk: if that combined
        // sweep clips the edge/corner of an obstacle - e.g. jumping over
        // one while also mid-lane-change - Unity's collision resolution
        // can shave off some of the requested VERTICAL displacement along
        // with the horizontal, even though velocity.y itself never
        // changed. Resolving vertical+forward on its own first prevents a
        // simultaneous lateral collision from reducing jump height.
        //
        // (History: this used to be ONE combined Move() call specifically
        // to avoid camera jitter from lateral and vertical/forward motion
        // being applied across two DIFFERENT update loops running at
        // different rates. That's not what's happening here - both calls
        // below still run in the same Update() at the same rate - but if
        // jitter reappears, this split is the first thing to revert.)
        //
        float verticalMotionY = dumpsterDiveControllingY ? 0f : velocity.y * Time.deltaTime;

        // Suppressed for a moment right after Movement gets re-enabled
        // (see OnEnable) - without this, the character starts sliding
        // forward the instant input is detected, while still visually
        // mid-blend from the lying pose into running, which looked
        // disconnected from what was actually happening on screen.
        float forwardMotion = Time.time < forwardMotionSuppressedUntil ? 0f : currentSpeed * Time.deltaTime;
        Vector3 verticalForwardMotion = new Vector3(0f, verticalMotionY, forwardMotion);
        controller.Move(verticalForwardMotion);

        Vector3 lateralMotion = new Vector3(deltaX, 0f, 0f);
        controller.Move(lateralMotion);

        if (transform.position.y < -10f)
        {
            // Safety net: if the player somehow falls through the ground
            // (tunneling through a gap, physics glitch, etc.), end the run
            // instead of letting them fall forever.
            TriggerGameOver();
        }
    }

    // Runs after Update() has finished moving the player for this frame -
    // keeps the Dumpster Dive bin's X/Z in sync with the player every
    // frame, so it stays with them as the world scrolls forward. Y is
    // deliberately left untouched here - it's fully controlled by
    // RiseOrDescend/SetPlayerY in the DumpsterDiveRoutine coroutine
    // instead (rising/descending together with the player, and held
    // steady during the sitting phase).
    private void LateUpdate()
    {
        if (activeDumpsterBin == null) return;

        Vector3 binPosition = activeDumpsterBin.transform.position;
        binPosition.x = transform.position.x + dumpsterBinOffset.x;
        binPosition.z = transform.position.z + dumpsterBinOffset.z;
        activeDumpsterBin.transform.position = binPosition;
    }

    // Called externally by GameManager while the player is waiting on the
    // start screen, before the run begins - this still works even though
    // Movement itself is disabled at that point (GameManager disables
    // this whole component so Update() never runs during the wait), since
    // only Unity's own callback methods are skipped on a disabled
    // component, not regular method calls like this one.
    public void PlayStartScreenAnimation()
    {
        if (animator != null)
        {
            animator.Play(startScreenStateName, 0, 0f);
        }
    }

    // Called externally by GameManager the moment the run begins (any
    // button pressed on the start screen) - forces an instant, guaranteed
    // transition out of Start Screen State Name into normal running,
    // since nothing would otherwise cause the Animator to leave it (it
    // was entered the same forced way, bypassing the Controller's own
    // transition graph, so there's no automatic "exit" path back either).
    public void PlayRunningAnimation()
    {
        if (animator != null)
        {
            animator.CrossFade(runningStateName, startScreenExitBlendDuration, 0);
        }
    }

    // Called on a graze: a side-scrape forgiven for free (no armor/revive
    // consumed) because it landed more than hitWindow after the previous
    // hit. Reverts to the lane the player was in before their last dodge
    // (if different) - that lane is guaranteed clear since they were just
    // standing in it - and plays the hit reaction animation.
    public void GrazeBounce()
    {
        if (isGameOver) return;

        if (previousLane != currentLane)
        {
            currentLane = previousLane;
        }

        PlayHitReactionAnimation();
    }

    // Called whenever a hit is survived by consuming an armor/revive
    // charge. No lane change - the player instead phases through the
    // specific obstacle that hit them (see PlayerCollision.PhaseThroughObstacle),
    // so there's no need to reposition them, and no risk of landing near a
    // different obstacle the way a knockback/lane-snap could.
    public void SurviveHit()
    {
        if (isGameOver) return;

        PlayHitReactionAnimation();
    }

    // Called by PlayerCollision when a Dumpster Dive power-up is picked
    // up. Runs the full jump-in -> sit -> jump-out sequence. If a
    // sequence is already running (picked up another Dumpster Dive mid-effect),
    // this just extends how long the sitting phase lasts instead of
    // restarting the whole jump-in/bin-spawn from scratch.
    public void PlayDumpsterDiveSequence(float duration)
    {
        float newEndTime = Time.time + duration;

        if (dumpsterDiveCoroutine != null)
        {
            dumpsterDiveEndTime = Mathf.Max(dumpsterDiveEndTime, newEndTime);
            return;
        }

        dumpsterDiveEndTime = newEndTime;
        dumpsterDiveCoroutine = StartCoroutine(DumpsterDiveRoutine());
    }

    // Toggles just the VISUAL appearance of the active bin (its
    // Renderers), without touching its collider/physics at all - lets the
    // floor collider stay functional immediately on spawn while the
    // visible mesh appears later, per the timing explanation in
    // DumpsterDiveRoutine below.
    private void SetDumpsterBinVisible(bool visible)
    {
        if (activeDumpsterBin == null) return;

        foreach (Renderer rend in activeDumpsterBin.GetComponentsInChildren<Renderer>())
        {
            rend.enabled = visible;
        }
    }

    private IEnumerator DumpsterDiveRoutine()
    {
        // Active for the WHOLE sequence, rise-in through descend-out - see
        // the speed calculation in Update().
        dumpsterDiveSpeedBoostActive = true;

        // Script has full authority over Y for the WHOLE sequence now -
        // see the check in Update(). This sidesteps every
        // CharacterController-collision/Root-Motion issue the
        // jump-into-a-pre-positioned-bin approach kept running into: the
        // bin and player now rise/descend TOGETHER as directly-scripted
        // motion, never relying on jump physics to reach a specific height.
        dumpsterDiveControllingY = true;
        jumpDisabledForDumpsterDive = true;

        if (animator != null)
        {
            animator.applyRootMotion = false;

            // Still plays the normal jump animation to accompany the
            // rise, purely for visual flavor - TriggerScriptedJump also
            // sets velocity.y, but that has no effect while
            // dumpsterDiveControllingY is true (Update() ignores it
            // entirely), so it's harmless to still call this for the
            // animation-triggering logic alone.
            TriggerScriptedJump(requireGrounded: false);
        }

        if (dumpsterBinPrefab != null)
        {
            // Spawned at GROUND level (Y=0) directly at the player's
            // current position - it rises together with the player below,
            // rather than the player needing to physically jump up into a
            // pre-positioned bin.
            Vector3 spawnPosition = new Vector3(
                transform.position.x + dumpsterBinOffset.x,
                0f,
                transform.position.z + dumpsterBinOffset.z);

            activeDumpsterBin = Instantiate(dumpsterBinPrefab, spawnPosition, Quaternion.identity);

            // Explicit reference instead of any automatic lookup - see
            // DumpsterBinCollector.cs for why.
            DumpsterBinCollector collector = activeDumpsterBin.GetComponent<DumpsterBinCollector>();
            if (collector != null)
            {
                collector.playerCollision = GetComponent<PlayerCollision>();
            }

            // Same reasoning as before - the bin follows the player's
            // position continuously, so make sure it can never physically
            // interfere with GroundSpawner's own follower object.
            GroundSpawner spawner = FindAnyObjectByType<GroundSpawner>();
            if (spawner != null)
            {
                Collider[] binColliders = activeDumpsterBin.GetComponentsInChildren<Collider>();
                Collider[] spawnerColliders = spawner.GetComponentsInChildren<Collider>();

                foreach (Collider binCol in binColliders)
                {
                    foreach (Collider spawnerCol in spawnerColliders)
                    {
                        Physics.IgnoreCollision(binCol, spawnerCol, true);
                    }
                }
            }

            // The player's Y is now fully scripted, never physics-driven,
            // for this whole sequence - there's no longer any need for the
            // player to physically collide with the bin at all, so ignore
            // every collider on it entirely.
            foreach (Collider binCol in activeDumpsterBin.GetComponentsInChildren<Collider>())
            {
                Physics.IgnoreCollision(controller, binCol, true);
            }

            SetDumpsterBinVisible(false);
        }

        if (dumpsterBinAppearDelay > 0f)
        {
            yield return new WaitForSeconds(dumpsterBinAppearDelay);
        }

        SetDumpsterBinVisible(true);

        // Player rises from ground level up to the sit height - the bin stays put.
        yield return RiseOrDescend(0f, dumpsterDiveSitHeight, dumpsterDiveRiseDuration);

        if (animator != null)
        {
            animator.SetBool(dumpsterSittingHash, true);

            // Force an instant, guaranteed transition into Sitting rather
            // than relying solely on the bool above - see the tooltip on
            // Sitting State Name for why this matters.
            animator.Play(sittingStateName, 0, 0f);
        }

        // Re-checked every frame (instead of a single flat WaitForSeconds)
        // so picking up ANOTHER Dumpster Dive mid-sequence - which
        // extends dumpsterDiveEndTime above - correctly stretches out the
        // sitting phase instead of ending on the original duration.
        while (Time.time < dumpsterDiveEndTime)
        {
            yield return null;
        }

        if (animator != null)
        {
            animator.SetBool(dumpsterSittingHash, false);
            TriggerScriptedJump(requireGrounded: false); // visual flavor only, same as the rise-in
        }

        // Player descends back down to ground level.
        yield return RiseOrDescend(dumpsterDiveSitHeight, 0f, dumpsterDiveRiseDuration);

        if (animator != null)
        {
            animator.applyRootMotion = true;
        }

        dumpsterDiveControllingY = false;
        jumpDisabledForDumpsterDive = false;
        velocity.y = 0f;

        if (activeDumpsterBin != null)
        {
            Destroy(activeDumpsterBin);
            activeDumpsterBin = null;
        }

        dumpsterDiveSpeedBoostActive = false;
        dumpsterDiveCoroutine = null;
    }

    // Smoothly moves the player's Y position from fromY to toY over
    // duration seconds, directly (not physics-driven) - used for both the
    // rise-in and descend-out of the Dumpster Dive sequence. The bin
    // itself stays put at ground level throughout - only the player moves.
    private IEnumerator RiseOrDescend(float fromY, float toY, float duration)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float y = Mathf.Lerp(fromY, toY, Mathf.Clamp01(elapsed / duration));
            SetPlayerY(y);
            yield return null;
        }

        // Ensure the exact final value, avoiding any accumulated float error.
        SetPlayerY(toY);
    }

    // Only moves the PLAYER's Y - the bin stays at whatever Y it spawned
    // at (ground level, 0) the whole sequence, since it doesn't need to
    // rise to still visually work with the sitting pose.
    private void SetPlayerY(float y)
    {
        Vector3 pos = transform.position;
        pos.y = y;
        transform.position = pos;
        Physics.SyncTransforms();
    }

    // Performs a jump identical to a player-initiated one (same physics,
    // same animation handling/priority), but callable from script without
    // needing an InputAction.CallbackContext - used for the Dumpster
    // Dive's scripted jump-in/jump-out, since those aren't triggered by
    // player input.
    private void TriggerScriptedJump(bool requireGrounded = true)
    {
        // The grounded check can be bypassed for the Dumpster Dive
        // jump-out specifically - right after releasing the height lock,
        // controller.isGrounded might not immediately read true (the
        // player was held above actual ground contact at
        // dumpsterDiveSitHeight), which would otherwise silently skip the
        // jump-out entirely.
        if (requireGrounded && !controller.isGrounded) return;

        velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);

        if (animator != null)
        {
            animator.ResetTrigger(dodgeLeftHash);
            animator.ResetTrigger(dodgeRightHash);
            animator.ResetTrigger(hitHash);

            if (IsAnimationBusy())
            {
                animator.Play(jumpStateName, 0, 0f);
            }
            else
            {
                animator.SetTrigger(jumpHash);
            }

            currentAnimationPriority = PriorityJump;
        }
    }

    // Shared by GrazeBounce and SurviveHit - both just play the same hit
    // reaction, only the lane-handling around them differs. Clears any
    // pending dodge/jump trigger first, same as OnJump/OnMove do for each
    // other - otherwise a still-queued trigger from a moment ago can end
    // up consuming its transition instead of (or ahead of) this one,
    // making the hit reaction not play, or play late.
    // Plays the Hit reaction, UNLESS a higher-priority animation (only
    // Jump or game over outrank Hit) is currently playing - per the
    // required priority order, Hit can interrupt Dodge/Run but must never
    // interrupt Jump. The underlying hit consequences (armor/revive
    // consumption, phase-through, etc., handled in PlayerCollision) still
    // happen regardless - only the VISUAL reaction is skipped here.
    private void PlayHitReactionAnimation()
    {
        if (animator == null) return;
        if (PriorityHit < currentAnimationPriority) return; // e.g. currently jumping - don't interrupt

        animator.ResetTrigger(dodgeLeftHash);
        animator.ResetTrigger(dodgeRightHash);
        // Deliberately does NOT reset jumpHash - Hit must never cancel a
        // Jump already in progress, per the priority order above.
        animator.SetTrigger(hitHash);
        currentAnimationPriority = PriorityHit;
    }

    // Called externally by PlayerCollision.TriggerGameOver() (a fatal hit
    // with no charges left) as well as internally by this script's own
    // TriggerGameOver() (falling out of the world). Just handles the
    // animation side - stops future triggers from firing via isGameOver.
    public void TriggerGameOverAnimation()
    {
        if (isGameOver) return; // avoid double-triggering if already game over

        isGameOver = true;

        if (animator != null)
        {
            // Game over (Rac_Lie to Sleep) is the highest priority tier -
            // always wins unconditionally, no gate needed.
            animator.SetTrigger(gameOverHash);
            currentAnimationPriority = PriorityGameOver;
        }
    }

    // Internal game-over path, specifically for falling out of the world
    // (see the check in Update()). Plays the animation, notifies
    // GameManager to show the game-over UI, and freezes the Rigidbody (if
    // one exists) so the ragdoll/physics body doesn't keep sliding/falling
    // after the run has ended.
    private void TriggerGameOver()
    {
        TriggerGameOverAnimation();
        FindAnyObjectByType<GameManager>().GameOver();

        if (rb != null)
        {
            rb.constraints = RigidbodyConstraints.FreezeAll;
            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
    }
}