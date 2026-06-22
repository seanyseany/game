using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RageTransformFreezeController : MonoBehaviour
{
    private struct RigidbodyState
    {
        public bool simulated;
        public Vector2 velocity;
        public float angularVelocity;
    }

    private struct PlayerBodyState
    {
        public float gravityScale;
        public Vector2 velocity;
        public float angularVelocity;
        public RigidbodyConstraints2D constraints;
    }

    private struct RageMoverState
    {
        public ObstacleRageMover mover;
        public bool wasFrozen;
    }

    public static RageTransformFreezeController Instance
    {
        get
        {
            if (instance == null)
                CreateRuntimeInstance();
            return instance;
        }
    }

    public static bool IsGameplayPauseActive => instance != null && instance.freezeActive;

    private static RageTransformFreezeController instance;

    private readonly Dictionary<Behaviour, bool> behaviourStates = new Dictionary<Behaviour, bool>(128);
    private readonly Dictionary<Animator, float> animatorSpeeds = new Dictionary<Animator, float>(128);
    private readonly Dictionary<Rigidbody2D, RigidbodyState> rigidbodyStates = new Dictionary<Rigidbody2D, RigidbodyState>(128);
    private readonly List<RageMoverState> rageMoverStates = new List<RageMoverState>(32);
    private readonly List<IRageTransformPauseHandler> pauseHandlers = new List<IRageTransformPauseHandler>(64);

    private Player activePlayer;
    private Coroutine activeRoutine;
    private PlayerBodyState playerBodyState;
    private bool playerBodyStateCaptured;
    private bool freezeActive;

    private static void CreateRuntimeInstance()
    {
        GameObject go = new GameObject(nameof(RageTransformFreezeController));
        DontDestroyOnLoad(go);
        instance = go.AddComponent<RageTransformFreezeController>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    public void Begin(Player player, float freezeDuration, float fadeInDuration = -1f, float fadeOutDuration = -1f)
    {
        if (player == null)
            return;

        if (activeRoutine != null)
            StopCoroutine(activeRoutine);

        RestoreFrozenState();

        activeRoutine = StartCoroutine(CoRunFreeze(
            player,
            Mathf.Max(0f, freezeDuration)));
    }

    public void EndNow(Player player = null)
    {
        if (player != null && activePlayer != null && player != activePlayer)
            return;

        if (activeRoutine != null)
        {
            StopCoroutine(activeRoutine);
            activeRoutine = null;
        }

        RestoreFrozenState();
    }

    public static IEnumerator WaitForSecondsRespectingGameplayPause(float seconds)
    {
        float remaining = Mathf.Max(0f, seconds);
        while (remaining > 0f)
        {
            if (IsGameplayPauseActive)
            {
                yield return null;
                continue;
            }

            remaining -= Time.deltaTime;
            yield return null;
        }
    }

    public static bool ShouldSkipGameplayFrame()
    {
        return IsGameplayPauseActive;
    }

    private IEnumerator CoRunFreeze(Player player, float freezeDuration)
    {
        activePlayer = player;

        CaptureAndFreezeWorld(player);

        float remaining = freezeDuration;
        while (remaining > 0f)
        {
            remaining -= Time.deltaTime;
            yield return null;
        }

        RestoreFrozenState();
        activeRoutine = null;
    }

    private void CaptureAndFreezeWorld(Player player)
    {
        freezeActive = true;
        activePlayer = player;
        behaviourStates.Clear();
        animatorSpeeds.Clear();
        rigidbodyStates.Clear();
        rageMoverStates.Clear();
        pauseHandlers.Clear();

        CachePlayerBodyState(player);
        FreezeStageSystems(true);
        CachePauseHandlers();
        FreezeSpecialRageMovers();
        FreezeRigidbodies(player);
        FreezeAnimators(player);
        DisableBehaviourGroup<Mover>(player);
        DisableBehaviourGroup<DownUpObstacle>(player);
        DisableBehaviourGroup<UpDownObstacle>(player);
        DisableBehaviourGroup<LeftRightObstacle>(player);
        DisableBehaviourGroup<RightLeftObstacle>(player);
        DisableBehaviourGroup<ObstacleMover>(player);
        DisableBehaviourGroup<MissileSpawner>(player);
        DisableBehaviourGroup<BackgroundScroller>(player);
        DisableBehaviourGroup<BackgroundScrollerException>(player);
        DisableBehaviourGroup<BackgroundLaneSpawner>(player);
        DisableBehaviourGroup<BackgroundPiece>(player);
        DisableBehaviourGroup<stage2prefabSpawner>(player);
        DisableBehaviourGroup<RageScrollSpeedModifier>(player);
        DisableBehaviourGroup<MechaLeg>(player);
        DisableBehaviourGroup<trainbody>(player);
        DisableBehaviourGroup<obstacleStaticMove>(player);
    }

    private void RestoreFrozenState()
    {
        if (!freezeActive)
            return;

        RestorePlayerBodyState();
        RestoreBehaviours();
        RestoreRigidbodies();
        RestoreAnimators();
        RestoreSpecialRageMovers();
        RestorePauseHandlers();
        FreezeStageSystems(false);

        freezeActive = false;
        activePlayer = null;
        playerBodyStateCaptured = false;
        rageMoverStates.Clear();
        pauseHandlers.Clear();
    }

    private void FreezeStageSystems(bool paused)
    {
        if (StageManager.Instance != null)
            StageManager.Instance.SetGameplayPause(paused);
    }

    private void CachePlayerBodyState(Player player)
    {
        if (player == null || player.rb == null)
            return;

        playerBodyState = new PlayerBodyState
        {
            gravityScale = player.rb.gravityScale,
            velocity = player.rb.linearVelocity,
            angularVelocity = player.rb.angularVelocity,
            constraints = player.rb.constraints
        };
        playerBodyStateCaptured = true;

        player.rb.linearVelocity = Vector2.zero;
        player.rb.angularVelocity = 0f;
        player.rb.gravityScale = 0f;
        player.rb.constraints |= RigidbodyConstraints2D.FreezePositionX | RigidbodyConstraints2D.FreezePositionY;
    }

    private void RestorePlayerBodyState()
    {
        if (!playerBodyStateCaptured || activePlayer == null || activePlayer.rb == null)
            return;

        activePlayer.rb.gravityScale = playerBodyState.gravityScale;
        activePlayer.rb.linearVelocity = Vector2.zero;
        activePlayer.rb.angularVelocity = 0f;
        activePlayer.rb.constraints = playerBodyState.constraints;
    }

    private void CachePauseHandlers()
    {
        var handlers = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < handlers.Length; i++)
        {
            if (handlers[i] is IRageTransformPauseHandler handler)
            {
                pauseHandlers.Add(handler);
                handler.OnRageTransformPauseStarted();
            }
        }
    }

    private void RestorePauseHandlers()
    {
        for (int i = 0; i < pauseHandlers.Count; i++)
        {
            if (pauseHandlers[i] != null)
                pauseHandlers[i].OnRageTransformPauseEnded();
        }
    }

    private void FreezeSpecialRageMovers()
    {
        var movers = FindObjectsByType<ObstacleRageMover>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < movers.Length; i++)
        {
            ObstacleRageMover mover = movers[i];
            if (mover == null)
                continue;

            rageMoverStates.Add(new RageMoverState
            {
                mover = mover,
                wasFrozen = mover.IsMovementFrozen
            });

            if (!mover.IsMovementFrozen)
                mover.FreezeAtCurrentPosition();
        }
    }

    private void RestoreSpecialRageMovers()
    {
        for (int i = 0; i < rageMoverStates.Count; i++)
        {
            RageMoverState state = rageMoverStates[i];
            if (state.mover == null || state.wasFrozen)
                continue;

            state.mover.ResumeMovementForCurrentState();
        }
    }

    private void FreezeRigidbodies(Player player)
    {
        Rigidbody2D[] bodies = FindObjectsByType<Rigidbody2D>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < bodies.Length; i++)
        {
            Rigidbody2D body = bodies[i];
            if (body == null || (player != null && body == player.rb))
                continue;

            rigidbodyStates[body] = new RigidbodyState
            {
                simulated = body.simulated,
                velocity = body.linearVelocity,
                angularVelocity = body.angularVelocity
            };

            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.simulated = false;
        }
    }

    private void RestoreRigidbodies()
    {
        foreach (KeyValuePair<Rigidbody2D, RigidbodyState> pair in rigidbodyStates)
        {
            Rigidbody2D body = pair.Key;
            if (body == null)
                continue;

            RigidbodyState state = pair.Value;
            body.simulated = state.simulated;
            body.linearVelocity = state.velocity;
            body.angularVelocity = state.angularVelocity;
        }
    }

    private void FreezeAnimators(Player player)
    {
        Animator[] animators = FindObjectsByType<Animator>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < animators.Length; i++)
        {
            Animator animator = animators[i];
            if (animator == null || animator == player.anim)
                continue;

            if (animator.GetComponentInParent<Canvas>() != null)
                continue;

            if (IsTransformSmokeAnimator(animator))
                continue;

            animatorSpeeds[animator] = animator.speed;
            animator.speed = 0f;
        }
    }

    private void RestoreAnimators()
    {
        foreach (KeyValuePair<Animator, float> pair in animatorSpeeds)
        {
            if (pair.Key != null)
                pair.Key.speed = pair.Value;
        }
    }

    private void DisableBehaviourGroup<T>(Player player) where T : Behaviour
    {
        T[] behaviours = FindObjectsByType<T>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < behaviours.Length; i++)
        {
            T behaviour = behaviours[i];
            if (behaviour == null || !behaviour.enabled)
                continue;

            if (player != null && behaviour.transform.IsChildOf(player.transform))
                continue;

            behaviourStates[behaviour] = true;
            behaviour.enabled = false;
        }
    }

    private void RestoreBehaviours()
    {
        foreach (KeyValuePair<Behaviour, bool> pair in behaviourStates)
        {
            if (pair.Key != null)
                pair.Key.enabled = pair.Value;
        }
    }

    private static bool IsTransformSmokeAnimator(Animator animator)
    {
        if (animator == null)
            return false;

        SmokeMover smokeMover = animator.GetComponent<SmokeMover>() ?? animator.GetComponentInParent<SmokeMover>();
        if (smokeMover != null && smokeMover.IsTransformRageSmoke())
            return true;

        string objectName = animator.gameObject.name;
        return !string.IsNullOrEmpty(objectName) && objectName.Contains("TransformSmoke");
    }
}

public interface IRageTransformPauseHandler
{
    void OnRageTransformPauseStarted();
    void OnRageTransformPauseEnded();
}
