using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public class BedSleepTrigger : MonoBehaviour
{
    [Header("패널 및 버튼")]
    public GameObject goodNightPanel;
    public Button sleepButton;
    public Button notYetButton;

    [Header("화면 페이드(전체 화면을 덮는 검은 Image)")]
    public Image fadeOverlay;
    public float fadeOutDuration = 0.6f;
    public float blackHoldDuration = 0.8f;
    public float fadeInDuration = 0.6f;

    [Header("플레이어 제어(비우면 자동 탐색)")]
    public PlayerMove playerMove;
    public bool autoFindPlayerMove = true;

    [Header("시작 시 겹쳐있으면 자동 잠금")]
    public bool lockIfPlayerInsideOnStart = true;

    [Header("패널 팝업 애니메이션")]
    [SerializeField] private float panelPopStartScale = 0.72f; // 시작 스케일
    [SerializeField] private float panelPopDuration = 0.18f; // 재생 시간(초)
    [SerializeField] private float panelPopOvershoot = 1.08f; // 최종을 살짝 넘기는 비율(백 이징 느낌)
    private bool _panelAnimating = false;

    [Header("디버그")]
    public bool verboseLog = false;

    private bool isPlayerInside = false;
    private bool requireExitToReopen = false;
    private bool isSleepingRoutine = false;

    private Collider2D _col;
    private const string PlayerTag = "Player";

    private void OnValidate()
    {
        var col = GetComponent<Collider2D>();
        if (col && !col.isTrigger) col.isTrigger = true;
    }

    private void Awake()
    {
        _col = GetComponent<Collider2D>();

        if (goodNightPanel) goodNightPanel.SetActive(false);
        if (fadeOverlay)
        {
            var c = fadeOverlay.color; c.a = 0f; fadeOverlay.color = c;
            fadeOverlay.raycastTarget = false;
            fadeOverlay.gameObject.SetActive(false);
        }
    }

    private void Start()
    {
        if (autoFindPlayerMove && playerMove == null)
            playerMove = FindFirstObjectByType<PlayerMove>(FindObjectsInactive.Include);

        if (sleepButton)
        {
            sleepButton.onClick.RemoveAllListeners();
            sleepButton.onClick.AddListener(OnClickSleep);
        }
        if (notYetButton)
        {
            notYetButton.onClick.RemoveAllListeners();
            notYetButton.onClick.AddListener(OnClickNotYet);
        }

        if (lockIfPlayerInsideOnStart)
            StartCoroutine(LockIfPlayerAlreadyInsideAtStart());
    }

    private IEnumerator LockIfPlayerAlreadyInsideAtStart()
    {
        float remain = 0.5f;
        yield return null; // 한 프레임 대기

        while (remain > 0f)
        {
            remain -= Time.unscaledDeltaTime;
            if (IsPlayerOverlappingMe(out _))
            {
                requireExitToReopen = true;
                isPlayerInside = true;
                if (goodNightPanel) goodNightPanel.SetActive(false);
                if (verboseLog) Debug.Log("[BedSleepTrigger] Start: player already inside → lock until Exit");
                yield break;
            }
            yield return null;
        }
    }

    private bool IsPlayerOverlappingMe(out Collider2D playerCollider)
    {
        playerCollider = null;
        if (_col == null) return false;

        var results = new List<Collider2D>(8);
        var filter = new ContactFilter2D { useTriggers = true };
        _col.Overlap(filter, results);

        for (int i = 0; i < results.Count; i++)
        {
            var c = results[i];
            if (c && c.CompareTag(PlayerTag)) { playerCollider = c; return true; }
        }
        return false;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag(PlayerTag)) return;
        isPlayerInside = true;

        if (!requireExitToReopen && !isSleepingRoutine)
        {
            OpenPanel();
            if (verboseLog) Debug.Log("[BedSleepTrigger] Enter → Panel Open + Freeze");
        }
        else if (verboseLog)
        {
            Debug.Log("[BedSleepTrigger] Enter but locked(requireExitToReopen) → no open");
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag(PlayerTag)) return;
        isPlayerInside = false;
        requireExitToReopen = false;
        if (verboseLog) Debug.Log("[BedSleepTrigger] Exit → reopen unlocked");
    }

    private void OpenPanel()
    {
        if (goodNightPanel != null && !goodNightPanel.activeSelf)
        {
            goodNightPanel.SetActive(true);

            // 팝업 애니메이션
            var rt = goodNightPanel.transform as RectTransform;
            if (rt != null)
            {
                // 중복 재생 방지
                if (_panelAnimating) StopAllCoroutines();
                StartCoroutine(PlayPanelPop(rt));
            }
        }

        if (playerMove != null) playerMove.Freeze();
    }


    private void ClosePanel()
    {
        if (goodNightPanel && goodNightPanel.activeSelf)
            goodNightPanel.SetActive(false);

        if (!isSleepingRoutine && playerMove)
            playerMove.Unfreeze();
    }

    private void OnClickSleep()
    {
        if (isSleepingRoutine) return;
        isSleepingRoutine = true;  // ClosePanel의 Unfreeze 차단
        ClosePanel();
        StartCoroutine(SleepRoutine());
    }

    private void OnClickNotYet()
    {
        ClosePanel();
        requireExitToReopen = true;
        if (verboseLog) Debug.Log("[BedSleepTrigger] Not yet → Close + Unfreeze, requires Exit to reopen");
    }

    private IEnumerator SleepRoutine()
    {
        if (playerMove) playerMove.Freeze();

        // 페이드 아웃
        yield return FadeTo(1f, fadeOutDuration);

        // 암전 중 저장/갱신
        ApplySleepAndSave();

        // 암전 유지
        if (blackHoldDuration > 0f)
            yield return new WaitForSeconds(blackHoldDuration);

        // 페이드 인
        yield return FadeTo(0f, fadeInDuration);

        if (playerMove) playerMove.Unfreeze();

        requireExitToReopen = true;
        isSleepingRoutine = false;
    }


    private void ApplySleepAndSave()
    {
        var dm = DataManager.instance;
        if (dm == null)
        {
            Debug.LogWarning("[BedSleepTrigger] DataManager.instance가 없음. 저장/갱신 생략");
            return;
        }

        dm.AddDay(1);

        Vector3 pos = playerMove ? playerMove.transform.position
                                 : (GameObject.FindGameObjectWithTag("Player")?.transform.position ?? Vector3.zero);
        dm.SetPlayerPosition(pos);

        if (dm.nowSlot >= 0)
        {
            dm.SaveData();
            if (verboseLog) Debug.Log($"[BedSleepTrigger] Saved after sleep. Day={dm.nowPlayer.Day}, Pos=({dm.nowPlayer.Px},{dm.nowPlayer.Py},{dm.nowPlayer.Pz}), Slot={dm.nowSlot}");
        }
        else
        {
            Debug.LogWarning("[BedSleepTrigger] nowSlot 미설정 → 파일 저장 생략 (HUD만 갱신)");
        }
    }

    // 패널 팝업: 언스케일드 시간으로 빠르게 커지며 등장
    private System.Collections.IEnumerator PlayPanelPop(RectTransform rt)
    {
        _panelAnimating = true;

        // 시작 스케일 세팅
        Vector3 from = Vector3.one * Mathf.Max(0.01f, panelPopStartScale);
        Vector3 toOvershoot = Vector3.one * Mathf.Max(1f, panelPopOvershoot); // 1.0 살짝 넘김
        Vector3 toFinal = Vector3.one;                                        // 최종 1.0

        // 첫 프레임에 바로 반영
        rt.localScale = from;
        yield return null;

        float t = 0f;
        float durHalf = panelPopDuration * 0.7f; // 대부분의 시간을 첫 구간에 배분(쫙 피어오르는 느낌)
        float durRest = Mathf.Max(0.0001f, panelPopDuration - durHalf);

        // 1) from → overshoot (EaseOutBack 유사)
        while (t < durHalf)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / durHalf);
            float e = EaseOutBack(k, 1.7f);           // 백 이징 느낌(세기 조절)
            rt.localScale = Vector3.LerpUnclamped(from, toOvershoot, e);
            yield return null;
        }

        // 2) overshoot → final (EaseOutQuad로 살짝 되돌림)
        t = 0f;
        while (t < durRest)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / durRest);
            float e = EaseOutQuad(k);
            rt.localScale = Vector3.LerpUnclamped(toOvershoot, toFinal, e);
            yield return null;
        }

        rt.localScale = toFinal;
        _panelAnimating = false;
    }

    // 튕기듯 나오는 감속 이징(백 이징)
    private float EaseOutBack(float x, float s = 1.70158f)
    {
        x = Mathf.Clamp01(x);
        float inv = x - 1f;
        return (inv * inv * ((s + 1f) * inv + s) + 1f);
    }

    // 부드러운 감속 이징
    private float EaseOutQuad(float x)
    {
        x = Mathf.Clamp01(x);
        return 1f - (1f - x) * (1f - x);
    }

    private IEnumerator FadeTo(float targetAlpha, float duration, bool disableWhenTransparent = true)
    {
        if (!fadeOverlay) yield break;

        if (!fadeOverlay.gameObject.activeSelf)
            fadeOverlay.gameObject.SetActive(true);

        fadeOverlay.raycastTarget = true;

        if (duration <= 0f)
        {
            var c0 = fadeOverlay.color; c0.a = targetAlpha; fadeOverlay.color = c0;
        }
        else
        {
            float start = fadeOverlay.color.a;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / duration);
                float a = Mathf.Lerp(start, targetAlpha, k);
                var c = fadeOverlay.color; c.a = a; fadeOverlay.color = c;
                yield return null;
            }
            var cf = fadeOverlay.color; cf.a = targetAlpha; fadeOverlay.color = cf;
        }

        if (Mathf.Approximately(targetAlpha, 0f))
        {
            fadeOverlay.raycastTarget = false;
            if (disableWhenTransparent)
                fadeOverlay.gameObject.SetActive(false);
        }
    }
}
