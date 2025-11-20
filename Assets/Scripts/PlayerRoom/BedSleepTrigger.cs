using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Localization.Settings;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public class BedSleepTrigger : MonoBehaviour
{
    [Header("패널 / 버튼")]
    public GameObject goodNightPanel;   // 전체 패널 루트
    public GameObject goodNightQA;      // 질문/버튼 컨테이너(잘 수 있을 때만 켜짐)
    public Button sleepButton;          // 자러간다
    public Button notYetButton;         // 아직

    [Header("CantGoodNight 텍스트(UI_Table/UI_CantGoodNight)")]
    public TMP_Text cantGoodNightText;  // 잘 수 없을 때 문구

    [Header("플레이어(비우면 자동 탐색)")]
    public PlayerMove playerMove;
    public bool autoFindPlayerMove = true;

    [Header("진입시 바로 다시 못열게 잠금")]
    public bool lockIfPlayerInsideOnStart = true;

    [Header("페이드 설정(없으면 자동 생성)")]
    [SerializeField] private CanvasGroup fadeCanvasGroup;  // 검은 화면 페이드용
    [SerializeField] private float fadeDuration = 0.6f;    // 페이드 시간

    [Header("디버그")]
    public bool verboseLog = false;

    // 내부 상태 값
    private Collider2D _col;
    private bool _cantSleepActive = false;     // "잘 수 없음" 모드인지
    private bool _sleepingRoutine = false;     // 수면 처리 중인지
    private bool _requireExitToReopen = false; // 한번 닫히면 나갔다 다시 들어와야 열리게 하는 플래그
    private bool _sceneLoading = false;        // 씬 로드 중 중복 호출 방지
    private const string PlayerTag = "Player";

    // 씬 이름 상수
    private const string PrologSceneName = "Prolog";
    private const string PlayerRoomSceneName = "Player's Room";

    private void OnValidate()
    {
        // 트리거 콜라이더 강제
        var col = GetComponent<Collider2D>();
        if (col && !col.isTrigger) col.isTrigger = true;
    }

    private void Awake()
    {
        _col = GetComponent<Collider2D>();

        // 페이드 캔버스가 없으면 자동 생성
        if (fadeCanvasGroup == null)
            CreateAutoFadeOverlay();

        if (fadeCanvasGroup != null)
        {
            fadeCanvasGroup.alpha = 0f;
            fadeCanvasGroup.gameObject.SetActive(true);
        }

        // 시작 시 UI 전부 끔
        if (goodNightPanel) goodNightPanel.SetActive(false);
        if (goodNightQA) goodNightQA.SetActive(false);
        if (cantGoodNightText)
        {
            cantGoodNightText.text = "";
            cantGoodNightText.gameObject.SetActive(false);
        }
    }

    private void Start()
    {
        // 플레이어 자동 탐색
        if (autoFindPlayerMove && !playerMove)
            playerMove = FindFirstObjectByType<PlayerMove>(FindObjectsInactive.Include);

        // 버튼 리스너 연결
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

        // 시작하자마자 플레이어가 안에 있으면 재오픈 잠금
        if (lockIfPlayerInsideOnStart)
            StartCoroutine(CoLockIfPlayerAlreadyInsideOnStart());
    }

    private void Update()
    {
        // 패널 열려있는 동안 이동 막기
        if (playerMove)
            playerMove.controlEnabled = !(goodNightPanel && goodNightPanel.activeInHierarchy);

        // "잘 수 없음" 모드일 때 클릭/스페이스로 닫기
        if (_cantSleepActive && (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space)))
            CloseCantSleep();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag(PlayerTag)) return;
        if (_requireExitToReopen || _sleepingRoutine || _sceneLoading) return;

        // 데이터 기반으로 "첫날 + 첫 수면 불가" 조건 체크
        bool cantSleep = false;
        var dm = DataManager.instance;
        if (dm != null && dm.nowPlayer != null)
            cantSleep = (dm.nowPlayer.Day == 1 && dm.nowPlayer.CanFirstSleep == false);

        // 현재 씬이 Prolog면 무조건 QA를 보여주고 "잘 수 없음" 문구는 띄우지 않음
        if (cantSleep && !IsPrologScene())
            ShowCantSleep();
        else
            OpenPanel();
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag(PlayerTag)) return;
        _requireExitToReopen = false;
    }

    // 패널 열기
    private void OpenPanel()
    {
        if (!goodNightPanel) return;

        // 질문/버튼은 켜고, "잘 수 없음" 문구는 끔
        if (goodNightQA) goodNightQA.SetActive(true);
        if (cantGoodNightText)
        {
            cantGoodNightText.text = "";
            cantGoodNightText.gameObject.SetActive(false);
        }

        goodNightPanel.SetActive(true);
        if (verboseLog) Debug.Log("[BedSleepTrigger] OpenPanel");
    }

    // 패널 닫기
    private void ClosePanel()
    {
        if (goodNightPanel) goodNightPanel.SetActive(false);
        if (verboseLog) Debug.Log("[BedSleepTrigger] ClosePanel");
    }

    // "잘 수 없음" 패널 표시
    private void ShowCantSleep()
    {
        // Prolog에서는 Cant 문구를 절대 안 띄우고 QA로 대체
        if (IsPrologScene())
        {
            OpenPanel();
            return;
        }

        _cantSleepActive = true;

        if (goodNightQA) goodNightQA.SetActive(false);
        if (cantGoodNightText)
        {
            string msg = LocalizationSettings.StringDatabase.GetLocalizedString("UI_Table", "UI_CantGoodNight");
            cantGoodNightText.text = msg;
            cantGoodNightText.gameObject.SetActive(true);
        }

        if (goodNightPanel) goodNightPanel.SetActive(true);
        if (verboseLog) Debug.Log("[BedSleepTrigger] CantSleep ON (click/space to close)");
    }

    // "잘 수 없음" 패널 닫기
    private void CloseCantSleep()
    {
        _cantSleepActive = false;

        if (cantGoodNightText)
        {
            cantGoodNightText.text = "";
            cantGoodNightText.gameObject.SetActive(false);
        }

        if (goodNightPanel) goodNightPanel.SetActive(false);

        // 닫히면 나갔다 들어와야 다시 열리게
        _requireExitToReopen = true;
        if (verboseLog) Debug.Log("[BedSleepTrigger] CantSleep CLOSED");
    }

    // 자러간다 버튼
    private void OnClickSleep()
    {
        if (_sleepingRoutine || _sceneLoading) return;
        _sleepingRoutine = true;

        // Prolog에서는 데이터매니저에 어떤 저장도 하지 않음
        if (!IsPrologScene())
        {
            ApplySleepAndSave();
        }

        ClosePanel();

        _requireExitToReopen = true;
        _sleepingRoutine = false;

        // 페이드 아웃 후 Player's Room으로 이동
        StartCoroutine(CoFadeOutAndLoadPlayersRoom());
    }

    // 아직 버튼
    private void OnClickNotYet()
    {
        ClosePanel();
        _requireExitToReopen = true;
    }

    // 하루 넘기고 저장(Prolog에서는 호출되지 않음)
    private void ApplySleepAndSave()
    {
        var dm = DataManager.instance;
        if (dm == null) return;

        dm.AddDay(1);

        Vector3 pos = playerMove
            ? playerMove.transform.position
            : (GameObject.FindGameObjectWithTag("Player")?.transform.position ?? Vector3.zero);

        dm.SetPlayerPosition(pos);

        if (dm.nowSlot >= 0)
            dm.SaveData();
    }

    // 페이드 아웃 -> 씬 로드
    private IEnumerator CoFadeOutAndLoadPlayersRoom()
    {
        if (_sceneLoading) yield break;
        _sceneLoading = true;

        // 페이드 캔버스가 없으면 즉시 로드
        if (fadeCanvasGroup == null)
        {
            SceneManager.LoadScene(PlayerRoomSceneName);
            yield break;
        }

        yield return StartCoroutine(FadeTo(1f, fadeDuration));

        SceneManager.LoadScene(PlayerRoomSceneName);
    }

    // 현재 씬이 Prolog인지 체크
    private bool IsPrologScene()
    {
        return SceneManager.GetActiveScene().name == PrologSceneName;
    }

    // 페이드만 담당
    private IEnumerator FadeTo(float targetAlpha, float duration)
    {
        if (fadeCanvasGroup == null)
            yield break;

        fadeCanvasGroup.gameObject.SetActive(true);

        float startAlpha = fadeCanvasGroup.alpha;
        float time = 0f;

        while (time < duration)
        {
            time += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(time / Mathf.Max(0.0001f, duration));
            fadeCanvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
            yield return null;
        }

        fadeCanvasGroup.alpha = targetAlpha;
    }

    // 시작 시 플레이어가 이미 트리거 안에 있으면 재오픈 잠금
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
                if (verboseLog) Debug.Log("[BedSleepTrigger] Player already inside on start -> require exit");
                yield break;
            }
            yield return null;
        }
    }

    // 플레이어가 트리거 안에 겹쳐있는지 검사
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
            if (c && c.CompareTag(PlayerTag))
            {
                playerCollider = c;
                return true;
            }
        }
        return false;
    }

    // 페이드용 검은 화면 오버레이 자동 생성
    private void CreateAutoFadeOverlay()
    {
        Canvas parentCanvas = FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
        if (parentCanvas == null)
        {
            if (verboseLog) Debug.LogWarning("[BedSleepTrigger] Canvas를 찾지 못해 페이드 오버레이를 만들 수 없습니다.");
            return;
        }

        GameObject fadeObj = new GameObject("AutoFadeOverlay");
        fadeObj.layer = parentCanvas.gameObject.layer;
        fadeObj.transform.SetParent(parentCanvas.transform, false);

        RectTransform rt = fadeObj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
        rt.anchoredPosition = Vector2.zero;

        Image img = fadeObj.AddComponent<Image>();
        img.color = Color.black;
        img.raycastTarget = false;

        fadeCanvasGroup = fadeObj.AddComponent<CanvasGroup>();
        fadeCanvasGroup.alpha = 0f;

        fadeObj.transform.SetAsLastSibling();

        if (verboseLog) Debug.Log("[BedSleepTrigger] 자동 페이드 오버레이 생성 완료");
    }
}
