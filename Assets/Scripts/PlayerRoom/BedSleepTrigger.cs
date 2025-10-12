using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Localization.Settings;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public class BedSleepTrigger : MonoBehaviour
{
    [Header("패널 / 버튼")]
    public GameObject goodNightPanel;   // 전체 패널
    public GameObject goodNightQA;      // 질문/버튼 컨테이너(잘 수 있을 때만 ON)
    public Button sleepButton;          // 자러간다
    public Button notYetButton;         // 아직

    [Header("CantGoodNight 텍스트(UI_Table/UI_CantGoodNight)")]
    public TMP_Text cantGoodNightText;  // 잘 수 없을 때 문구

    [Header("플레이어(비우면 자동 탐색)")]
    public PlayerMove playerMove;
    public bool autoFindPlayerMove = true;

    [Header("진입시 바로 다시 못열게 잠금")]
    public bool lockIfPlayerInsideOnStart = true;

    [Header("디버그")]
    public bool verboseLog = false;

    // 내부 상태
    private Collider2D _col;
    private bool _cantSleepActive = false;     // "잘 수 없음" 모드
    private bool _sleepingRoutine = false;     // 수면 연출 중
    private bool _requireExitToReopen = false; // 나갔다가 다시 들어와야 재오픈
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
        if (goodNightQA) goodNightQA.SetActive(false);
        if (cantGoodNightText) { cantGoodNightText.text = ""; cantGoodNightText.gameObject.SetActive(false); }
    }

    private void Start()
    {
        if (autoFindPlayerMove && !playerMove)
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
            StartCoroutine(CoLockIfPlayerAlreadyInsideOnStart());
    }

    private void Update()
    {
        // ✅ 핵심: 패널 ON이면 이동 막기 / OFF면 이동 허용
        if (playerMove)
            playerMove.controlEnabled = !(goodNightPanel && goodNightPanel.activeInHierarchy);

        // CantSleep은 마우스 클릭 또는 스페이스로 닫기
        if (_cantSleepActive && (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space)))
            CloseCantSleep();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag(PlayerTag)) return;
        if (_requireExitToReopen || _sleepingRoutine) return;

        // Day==1 && CanFirstSleep==false → 잘 수 없음
        bool cantSleep = false;
        var dm = DataManager.instance;
        if (dm != null && dm.nowPlayer != null)
            cantSleep = (dm.nowPlayer.Day == 1 && dm.nowPlayer.CanFirstSleep == false);

        if (cantSleep) ShowCantSleep();
        else OpenPanel();
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag(PlayerTag)) return;
        _requireExitToReopen = false;
    }

    // ─── UI 열기/닫기 ───
    private void OpenPanel()
    {
        if (!goodNightPanel) return;

        // 질문/버튼 영역 ON, Cant 텍스트 OFF
        if (goodNightQA) goodNightQA.SetActive(true);
        if (cantGoodNightText) { cantGoodNightText.text = ""; cantGoodNightText.gameObject.SetActive(false); }

        goodNightPanel.SetActive(true);
        if (verboseLog) Debug.Log("[BedSleepTrigger] OpenPanel");
    }

    private void ClosePanel()
    {
        if (goodNightPanel) goodNightPanel.SetActive(false);
        if (verboseLog) Debug.Log("[BedSleepTrigger] ClosePanel");
    }

    // ─── CantSleep 플로우 ───
    private void ShowCantSleep()
    {
        _cantSleepActive = true;

        if (goodNightQA) goodNightQA.SetActive(false); // 질문 영역 OFF
        if (cantGoodNightText)
        {
            string msg = LocalizationSettings.StringDatabase.GetLocalizedString("UI_Table", "UI_CantGoodNight");
            cantGoodNightText.text = msg;
            cantGoodNightText.gameObject.SetActive(true);
        }

        if (goodNightPanel) goodNightPanel.SetActive(true);
        if (verboseLog) Debug.Log("[BedSleepTrigger] CantSleep ON (click/space to close)");
    }

    private void CloseCantSleep()
    {
        _cantSleepActive = false;

        if (cantGoodNightText) { cantGoodNightText.text = ""; cantGoodNightText.gameObject.SetActive(false); }
        if (goodNightPanel) goodNightPanel.SetActive(false);

        _requireExitToReopen = true; // 나갔다 다시 들어와야 재오픈
        if (verboseLog) Debug.Log("[BedSleepTrigger] CantSleep CLOSED");
    }

    // ─── 버튼 콜백 ───
    private void OnClickSleep()
    {
        if (_sleepingRoutine) return;
        _sleepingRoutine = true;

        // 여기서는 “간단 모드”니까 연출 없이 끄고 하루 넘겼다고만 처리
        ApplySleepAndSave();
        ClosePanel();

        _requireExitToReopen = true;
        _sleepingRoutine = false;
    }

    private void OnClickNotYet()
    {
        ClosePanel();
        _requireExitToReopen = true;
    }

    // ─── 데이터 갱신 ───
    private void ApplySleepAndSave()
    {
        var dm = DataManager.instance;
        if (dm == null) return;

        dm.AddDay(1);

        Vector3 pos = playerMove ? playerMove.transform.position
                                 : (GameObject.FindGameObjectWithTag("Player")?.transform.position ?? Vector3.zero);
        dm.SetPlayerPosition(pos);

        if (dm.nowSlot >= 0) dm.SaveData();
    }

    // ─── 보조 ───
    private IEnumerator CoLockIfPlayerAlreadyInsideOnStart()
    {
        yield return null;
        float timer = 0.4f;

        while (timer > 0f)
        {
            timer -= Time.unscaledDeltaTime;
            if (IsPlayerOverlappingMe(out _))
            {
                _requireExitToReopen = true;
                if (verboseLog) Debug.Log("[BedSleepTrigger] Player already inside on start → require exit");
                yield break;
            }
            yield return null;
        }
    }

    private bool IsPlayerOverlappingMe(out Collider2D playerCollider)
    {
        playerCollider = null;
        var col = GetComponent<Collider2D>();
        if (!col) return false;

        var results = new List<Collider2D>(8);
        var filter = new ContactFilter2D { useTriggers = true };
        col.Overlap(filter, results);

        for (int i = 0; i < results.Count; i++)
        {
            var c = results[i];
            if (c && c.CompareTag(PlayerTag)) { playerCollider = c; return true; }
        }
        return false;
    }
}
