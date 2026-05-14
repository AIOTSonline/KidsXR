using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// FrogLifeCycleInteraction
/// ─────────────────────────────────────────────────────────────────────────────
/// Drop this on the root GameObject that gets spawned by PlaceOnIndicator.
///
/// HOW TO SET UP IN THE INSPECTOR
/// ──────────────────────────────
/// 1. Life Cycle Stages  (add as many as you need)
///    Each entry has:
///      • stagePrefab      – the model to show for this stage (Egg, Tadpole, etc.)
///      • tapsToAdvance    – how many taps before moving to the next stage
///      • shakeOnTap       – whether each tap triggers a little shake
///      • shakeIntensity   – how strong the shake is  (0.02 – 0.1 is good)
///      • shakeDuration    – seconds each shake lasts  (0.3 – 0.6 is good)
///
/// 2. Sound Button UI     – assign the Button that plays the frog sound
/// 3. Frog Sound          – the AudioClip to play when that button is pressed
/// 4. Tap Sound           – (optional) small click / bonk sound on every tap
/// 5. Stage Change Sound  – (optional) whoosh / sparkle on transformation
///
/// HOW IT WORKS
/// ────────────
/// • Only the current stage's model is active at a time.
/// • Tap the active model → shake (if enabled) + tap counter increases.
/// • When tap count reaches tapsToAdvance → swap to next stage model.
/// • On the final stage (Frog) the Sound Button appears and plays the croak.
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public class LifeCycleInteraction : MonoBehaviour
{
    // ─── Inspector-visible stage definition ──────────────────────────────────
    [System.Serializable]
    public class LifeCycleStage
    {
        [Tooltip("The 3-D model / prefab for this stage")]
        public GameObject stagePrefab;

        [Tooltip("How many taps are needed to advance to the next stage (ignored on the last stage)")]
        [Min(0)] public int tapsToAdvance = 3;

        [Tooltip("Should the model shake on each tap?")]
        public bool shakeOnTap = true;

        [Tooltip("How far the model shifts during a shake (world units)")]
        [Range(0f, 0.2f)] public float shakeIntensity = 0.04f;

        [Tooltip("How long (seconds) one shake animation lasts")]
        [Range(0.1f, 1f)] public float shakeDuration = 0.4f;
    }

    // ─── Inspector fields ─────────────────────────────────────────────────────
    [Header("Life Cycle Stages")]
    [Tooltip("Define every stage in order: Egg → Tadpole → Tadpole+Legs → Frog")]
    [SerializeField] List<LifeCycleStage> stages = new List<LifeCycleStage>();

    [Header("UI")]
    [Tooltip("The Button shown only on the final stage to play the frog sound")]
    [SerializeField] Button soundButton;

    [Header("Audio")]
    [Tooltip("Frog croak – played when the Sound Button is pressed")]
    [SerializeField] AudioClip frogSound;

    [Tooltip("Short tap feedback sound (optional)")]
    [SerializeField] AudioClip tapSound;

    [Tooltip("Stage-change transition sound (optional)")]
    [SerializeField] AudioClip stageChangeSound;

    // ─── Private state ────────────────────────────────────────────────────────
    int currentStageIndex = 0;
    int tapCount = 0;
    bool isShaking = false;
    bool isTransitioning = false;

    AudioSource audioSource;

    // ─── Unity lifecycle ──────────────────────────────────────────────────────
    void Start()
    {
        // Audio
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f; // 3-D sound in AR

        // Hide sound button at start; only shows on final stage
        if (soundButton != null)
        {
            soundButton.gameObject.SetActive(false);
            soundButton.onClick.AddListener(PlayFrogSound);
        }

        // Activate first stage, hide everything else
        InitialiseStages();
    }

    // ─── Public: called by a UI Button's OnClick OR by a Raycast / Input system
    /// <summary>
    /// Call this from your tap / raycast detection code when the player taps
    /// the current stage model.  Works for both UI Button OnClick and 3-D touch.
    /// </summary>
    public void OnStageTapped()
    {
        if (isTransitioning) return;
        if (currentStageIndex >= stages.Count) return;

        LifeCycleStage stage = stages[currentStageIndex];
        bool isFinalStage = (currentStageIndex == stages.Count - 1);

        // Final stage: no more tapping to advance – button handles interaction
        if (isFinalStage) return;

        tapCount++;
        PlaySound(tapSound);

        if (stage.shakeOnTap && !isShaking)
            StartCoroutine(ShakeModel(stage.stagePrefab, stage.shakeIntensity, stage.shakeDuration));

        if (tapCount >= Mathf.Max(1, stage.tapsToAdvance))
            StartCoroutine(AdvanceStage());
    }

    // ─── Initialisation ───────────────────────────────────────────────────────
    void InitialiseStages()
    {
        for (int i = 0; i < stages.Count; i++)
        {
            if (stages[i].stagePrefab != null)
                stages[i].stagePrefab.SetActive(i == 0);
        }
    }

    // ─── Stage advancement ────────────────────────────────────────────────────
    IEnumerator AdvanceStage()
    {
        isTransitioning = true;

        // Hide current stage
        if (stages[currentStageIndex].stagePrefab != null)
            stages[currentStageIndex].stagePrefab.SetActive(false);

        currentStageIndex++;
        tapCount = 0;

        PlaySound(stageChangeSound);

        // Small delay so the sound / effect lands nicely
        yield return new WaitForSeconds(0.15f);

        // Show next stage
        if (currentStageIndex < stages.Count && stages[currentStageIndex].stagePrefab != null)
        {
            stages[currentStageIndex].stagePrefab.SetActive(true);
        }

        // If this is now the final stage, show the sound button
        bool nowFinal = (currentStageIndex == stages.Count - 1);
        if (nowFinal && soundButton != null)
            soundButton.gameObject.SetActive(true);

        isTransitioning = false;
    }

    // ─── Shake coroutine ──────────────────────────────────────────────────────
    IEnumerator ShakeModel(GameObject model, float intensity, float duration)
    {
        if (model == null) yield break;
        isShaking = true;

        Vector3 origin = model.transform.localPosition;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            float progress = elapsed / duration;
            // Shake diminishes toward the end
            float strength = intensity * (1f - progress);

            model.transform.localPosition = origin + new Vector3(
                Random.Range(-strength, strength),
                Random.Range(-strength * 0.5f, strength * 0.5f),
                Random.Range(-strength, strength)
            );

            elapsed += Time.deltaTime;
            yield return null;
        }

        model.transform.localPosition = origin;
        isShaking = false;
    }

    // ─── Audio helpers ────────────────────────────────────────────────────────
    void PlaySound(AudioClip clip)
    {
        if (clip != null && audioSource != null)
            audioSource.PlayOneShot(clip);
    }

    public void PlayFrogSound()
    {
        PlaySound(frogSound);
    }

    // ─── Tap progress query (useful for showing a tap counter UI) ─────────────
    /// <summary>Returns how many taps are still needed on the current stage.</summary>
    public int TapsRemaining()
    {
        if (currentStageIndex >= stages.Count) return 0;
        return Mathf.Max(0, stages[currentStageIndex].tapsToAdvance - tapCount);
    }

    /// <summary>Returns 0-based index of the active stage.</summary>
    public int CurrentStageIndex() => currentStageIndex;
    public void SetStageModel(int index, GameObject model)
    {
        if (index < 0 || index >= stages.Count) return;

        stages[index].stagePrefab = model;
    }
    public void RefreshStages()
    {
        currentStageIndex = 0;
        tapCount = 0;
        InitialiseStages();
    }
}