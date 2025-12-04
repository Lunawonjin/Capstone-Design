using System.Collections;
using UnityEngine;
using TMPro;
// If you use the new Input System, uncomment the next line
// using UnityEngine.InputSystem;

public class GameCountdownStarter : MonoBehaviour
{
    [Header("UI")]
    public TextMeshProUGUI CountDown_Text;

    [Header("Config")]
    public float countdownSeconds = 3f;
    public bool autoStartOnEnable = true;

    [Header("Target")]
    public BlockSpawnManager targetManager;

    [Header("Freeze Options")]
    [Tooltip("Freeze the whole game by setting Time.timeScale = 0 during countdown.")]
    public bool freezeWithTimeScale = true;

    [Tooltip("Optionally disable player input components during countdown.")]
    public bool disablePlayerInputs = true;

    [Tooltip("Assign components to disable/enable during countdown (e.g., PlayerInput or custom controllers).")]
    public Behaviour[] componentsToDisable;

    Coroutine co;
    float prevTimeScale = 1f;

    void OnEnable()
    {
        if (autoStartOnEnable) StartCountdown();
    }

    public void StartCountdown()
    {
        if (co != null) StopCoroutine(co);
        co = StartCoroutine(CoCountdown());
    }

    IEnumerator CoCountdown()
    {
        // Gate off the gameplay
        if (targetManager != null)
            targetManager.gameCanRun = false;

        // Disable input components if requested
        if (disablePlayerInputs && componentsToDisable != null)
        {
            for (int i = 0; i < componentsToDisable.Length; i++)
            {
                if (componentsToDisable[i] != null)
                    componentsToDisable[i].enabled = false;
            }
        }

        // Freeze with timeScale = 0
        if (freezeWithTimeScale)
        {
            prevTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }

        // Realtime countdown
        float remain = Mathf.Max(0f, countdownSeconds);
        while (remain > 0f)
        {
            int display = Mathf.CeilToInt(remain);
            if (CountDown_Text) CountDown_Text.text = display.ToString();
            // Use unscaled time while frozen
            yield return null;
            remain -= Time.unscaledDeltaTime;
        }

        if (CountDown_Text) CountDown_Text.text = "START";

        // Unfreeze
        if (freezeWithTimeScale)
            Time.timeScale = prevTimeScale;

        // Re-enable input components
        if (disablePlayerInputs && componentsToDisable != null)
        {
            for (int i = 0; i < componentsToDisable.Length; i++)
            {
                if (componentsToDisable[i] != null)
                    componentsToDisable[i].enabled = true;
            }
        }

        // Allow gameplay
        if (targetManager != null)
            targetManager.gameCanRun = true;

        // Optional: small delay then hide the text
        yield return new WaitForSecondsRealtime(0.3f);
        if (CountDown_Text) CountDown_Text.gameObject.SetActive(false);

        co = null;
    }
}
