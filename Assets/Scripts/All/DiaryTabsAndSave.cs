// Unity 6 (LTS)
// 탭 전환(기록/호감도/그림일기) + SaveQA 모달(팝인/축소) + Yes 저장/No·Esc 닫기
// SaveQA 활성 중에는 도감 Esc 닫기 차단, 비활성 시 복귀
using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public class DiaryTabsAndSave : MonoBehaviour
{
    [Header("도감 본체")]
    [SerializeField] private DiaryUI diaryUI; // Esc 차단 연동

    [Header("탭 버튼")]
    [SerializeField] private Button btnRecord;    // 기록
    [SerializeField] private Button btnAffinity;  // 호감도
    [SerializeField] private Button btnSketch;    // 그림일기

    [Header("탭 패널")]
    [SerializeField] private GameObject panelRecord;
    [SerializeField] private GameObject panelAffinity;
    [SerializeField] private GameObject panelSketch;

    [Header("저장 버튼(도감 내부)")]
    [SerializeField] private Button btnSave;

    [Header("SaveQA 모달(최상위)")]
    [SerializeField] private GameObject saveQAPanel;           // 비활성 시작 권장
    [SerializeField] private Button btnYes;
    [SerializeField] private Button btnNo;

    [Header("SaveQA 연출(CanvasGroup 권장)")]
    [SerializeField] private CanvasGroup saveQACanvasGroup;     // null이면 알파 생략
    [SerializeField] private RectTransform saveQARectTransform;
    [SerializeField] private Vector3 qaOpenStartScale = new Vector3(0.9f, 0.9f, 1f);
    [SerializeField] private Vector3 qaOpenEndScale = Vector3.one;
    [SerializeField, Min(0.01f)] private float qaOpenDuration = 0.14f;

    [SerializeField] private Vector3 qaCloseStartScale = Vector3.one;
    [SerializeField] private Vector3 qaCloseEndScale = new Vector3(0.9f, 0.9f, 1f);
    [SerializeField, Min(0.01f)] private float qaCloseDuration = 0.12f;

    [Header("저장 데이터 소스")]
    [Tooltip("플레이어 Transform(없으면 Tag \"Player\" 자동 탐색)")]
    [SerializeField] private Transform playerTransform;

    private bool _qaAnimating = false;
    private bool _qaOpen = false;

    void Awake()
    {
        // 탭 버튼 연결
        if (btnRecord != null) btnRecord.onClick.AddListener(() => ShowTab(panelRecord));
        if (btnAffinity != null) btnAffinity.onClick.AddListener(() => ShowTab(panelAffinity));
        if (btnSketch != null) btnSketch.onClick.AddListener(() => ShowTab(panelSketch));

        // 저장 버튼
        if (btnSave != null) btnSave.onClick.AddListener(OpenSaveQA);

        // SaveQA 버튼
        if (btnYes != null) btnYes.onClick.AddListener(OnClickYes_SaveAllViaDataManager);
        if (btnNo != null) btnNo.onClick.AddListener(CloseSaveQA);

        // 기본 탭: 기록
        ShowTab(panelRecord);

        // SaveQA 초기화
        if (saveQAPanel != null)
        {
            if (saveQACanvasGroup != null)
            {
                saveQACanvasGroup.alpha = 0f;
                saveQACanvasGroup.interactable = false;
                saveQACanvasGroup.blocksRaycasts = false;
            }
            if (saveQARectTransform != null) saveQARectTransform.localScale = qaOpenEndScale;
            saveQAPanel.SetActive(false);
        }
    }

    void Update()
    {
#if ENABLE_INPUT_SYSTEM
        if (_qaOpen && Keyboard.current != null && !_qaAnimating)
        {
            if (Keyboard.current.escapeKey.wasPressedThisFrame)
                CloseSaveQA(); // 모달만 닫힘
        }
#else
        if (_qaOpen && !_qaAnimating)
        {
            if (Input.GetKeyDown(KeyCode.Escape))
                CloseSaveQA();
        }
#endif

        // Player transform 자동 탐색
        if (playerTransform == null)
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go) playerTransform = go.transform;
        }
    }

    // ---------------- 탭 전환 ----------------
    private void ShowTab(GameObject target)
    {
        if (panelRecord != null) panelRecord.SetActive(panelRecord == target);
        if (panelAffinity != null) panelAffinity.SetActive(panelAffinity == target);
        if (panelSketch != null) panelSketch.SetActive(panelSketch == target);
    }

    // ---------------- SaveQA 열기/닫기 ----------------
    private void OpenSaveQA()
    {
        if (saveQAPanel == null || _qaAnimating || _qaOpen) return;

        _qaOpen = true;
        _qaAnimating = true;

        if (diaryUI != null) diaryUI.externalEscBlocked = true; // 도감 Esc 닫기 차단

        saveQAPanel.SetActive(true);

        if (saveQARectTransform != null) saveQARectTransform.localScale = qaOpenStartScale;
        if (saveQACanvasGroup != null)
        {
            saveQACanvasGroup.alpha = 0f;
            saveQACanvasGroup.interactable = false;
            saveQACanvasGroup.blocksRaycasts = false;
        }

        StartCoroutine(Co_QAOpen());
    }

    private void CloseSaveQA()
    {
        if (saveQAPanel == null || _qaAnimating || !_qaOpen) return;

        _qaOpen = false;
        _qaAnimating = true;

        if (saveQARectTransform != null) saveQARectTransform.localScale = qaCloseStartScale;
        if (saveQACanvasGroup != null)
        {
            saveQACanvasGroup.interactable = false;
            saveQACanvasGroup.blocksRaycasts = false;
        }

        StartCoroutine(Co_QAClose());
    }

    private System.Collections.IEnumerator Co_QAOpen()
    {
        float t = 0f, d = Mathf.Max(0.01f, qaOpenDuration);
        while (t < d)
        {
            float u = t / d;
            float e = 1f - Mathf.Pow(1f - u, 3f); // EaseOutCubic
            if (saveQARectTransform != null)
                saveQARectTransform.localScale = Vector3.LerpUnclamped(qaOpenStartScale, qaOpenEndScale, e);
            if (saveQACanvasGroup != null)
                saveQACanvasGroup.alpha = Mathf.LerpUnclamped(0f, 1f, e);
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        if (saveQARectTransform != null) saveQARectTransform.localScale = qaOpenEndScale;
        if (saveQACanvasGroup != null)
        {
            saveQACanvasGroup.alpha = 1f;
            saveQACanvasGroup.interactable = true;
            saveQACanvasGroup.blocksRaycasts = true;
        }
        _qaAnimating = false;
    }

    private System.Collections.IEnumerator Co_QAClose()
    {
        float t = 0f, d = Mathf.Max(0.01f, qaCloseDuration);
        float startAlpha = (saveQACanvasGroup != null) ? saveQACanvasGroup.alpha : 1f;

        while (t < d)
        {
            float u = t / d;
            float e = Mathf.Pow(u, 3f); // EaseInCubic
            if (saveQARectTransform != null)
                saveQARectTransform.localScale = Vector3.LerpUnclamped(qaCloseStartScale, qaCloseEndScale, e);
            if (saveQACanvasGroup != null)
                saveQACanvasGroup.alpha = Mathf.LerpUnclamped(startAlpha, 0f, e);
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        if (saveQARectTransform != null) saveQARectTransform.localScale = qaCloseEndScale;
        if (saveQACanvasGroup != null) saveQACanvasGroup.alpha = 0f;

        saveQAPanel.SetActive(false);
        _qaAnimating = false;

        // 모달 닫힘 → 도감 Esc 복귀
        if (diaryUI != null) diaryUI.externalEscBlocked = false;
    }

    // ---------------- Yes: DataManager를 통해 저장 ----------------
    private void OnClickYes_SaveAllViaDataManager()
    {
        var dm = DataManager.instance;
        if (dm == null)
        {
            Debug.LogError("[DiaryTabsAndSave] DataManager.instance is null.");
            CloseSaveQA();
            return;
        }

        // 슬롯 확인
        if (dm.nowSlot < 0)
        {
            Debug.LogError("[DiaryTabsAndSave] nowSlot not selected.");
            CloseSaveQA();
            return;
        }

        // 현재 씬 이름
        string sceneName = SceneManager.GetActiveScene().name;
        dm.SetSceneName(sceneName);

        // 플레이어 위치 스냅샷
        if (playerTransform == null)
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go) playerTransform = go.transform;
        }
        if (playerTransform != null)
            dm.SetPlayerPosition(playerTransform.position);

        // 날짜/코인 등은 dm.nowPlayer가 최신이면 그대로 저장됨
        // 필요 시 이곳에 추가 스냅샷 로직 확장

        // 기록
        dm.SaveData();

        // 완료 후 SaveQA 닫기
        CloseSaveQA();
    }
}
