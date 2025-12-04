using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Components; // LocalizeStringEvent

public class LanguageSelectUI : MonoBehaviour
{
    [Header("버튼(한/영/일)")]
    [SerializeField] private Button buttonKorean;
    [SerializeField] private Button buttonEnglish;
    [SerializeField] private Button buttonJapanese;

    [Header("선택 언어 표시(선택)")]
    [SerializeField] private TMP_Text currentLangText; // 예: "언어: ko"

    [Header("선택 후 자동 저장(DataManager)")]
    [SerializeField] private bool saveImmediately = true;

    [Header("시작화면 동기화 옵션")]
    [Tooltip("시작 시: 최신 세이브가 있으면 그 언어를 따르고, 없으면 fallbackLang을 사용")]
    [SerializeField] private bool syncFromLatestSaveOnStart = true;

    [Tooltip("세이브가 없다면 사용할 기본 언어(ko/en/jp)")]
    [SerializeField] private string fallbackLang = "ko";

    [Header("게임 시작 플로우(선택)")]
    [Tooltip("새 게임 버튼(선택)")]
    [SerializeField] private Button buttonNewGame;
    [Tooltip("이어하기 버튼(선택)")]
    [SerializeField] private Button buttonContinue;
    [Tooltip("게임 씬 이름(새 게임/이어하기 성공 시 로드)")]
    [SerializeField] private string gameSceneName = "Game";
    [Tooltip("새 게임 시 기본 슬롯(차있으면 다음 빈 슬롯 탐색)")]
    [SerializeField] private int defaultNewSlot = 0;

    // 내부 상태: 현재 앱 언어코드(ko/en/jp로 유지)
    private string _currentLang = "ko";

    void Awake()
    {
        if (buttonKorean) buttonKorean.onClick.AddListener(() => StartCoroutine(SwitchLanguage("ko")));
        if (buttonEnglish) buttonEnglish.onClick.AddListener(() => StartCoroutine(SwitchLanguage("en")));
        if (buttonJapanese) buttonJapanese.onClick.AddListener(() => StartCoroutine(SwitchLanguage("jp")));

        if (buttonNewGame) buttonNewGame.onClick.AddListener(() => StartCoroutine(StartNewGameFlow()));
        if (buttonContinue) buttonContinue.onClick.AddListener(() => StartCoroutine(ContinueFlow()));
    }

    IEnumerator Start()
    {
        // Localization 초기화 대기
        var init = LocalizationSettings.InitializationOperation;
        if (!init.IsDone) yield return init;

        // 시작화면 언어 동기화
        if (syncFromLatestSaveOnStart && DataManager.instance != null)
        {
            string startCode = fallbackLang;

            // [수정됨] 최신 세이브가 있으면 그 언어를 '읽어오기만' 함 (전체 데이터 로드 X)
            if (DataManager.instance.HasAnySave())
            {
                int recentSlot = DataManager.instance.GetMostRecentSaveSlot();
                if (recentSlot >= 0)
                {
                    // 새로 추가된 PeekLanguageFromSlot 함수를 사용
                    string langFromFile = DataManager.instance.PeekLanguageFromSlot(recentSlot);
                    if (!string.IsNullOrEmpty(langFromFile))
                    {
                        startCode = SafeNormalizeAppCode(langFromFile);
                    }
                }
            }

            // UI/로케일 즉시 반영(세이브에는 아직 쓰지 않음)
            yield return SwitchLanguage(startCode, writeToSave: false);
        }
        else
        {
            // 최소 한 번은 라벨 표기
            RefreshLabel();
        }
    }

    // ====== 언어 스위치 ======

    public IEnumerator SwitchLanguage(string code) => SwitchLanguage(code, writeToSave: true);

    private IEnumerator SwitchLanguage(string code, bool writeToSave)
    {
        // 1) Localization 초기화 대기
        var init = LocalizationSettings.InitializationOperation;
        if (!init.IsDone) yield return init;

        // 2) 앱 포맷(ko/en/jp) 보정 + Unity 로케일 코드 매핑
        _currentLang = SafeNormalizeAppCode(code);
        string localeCode = (_currentLang == "jp") ? "ja" : _currentLang;

        // 3) 대상 Locale 찾기
        Locale target = null;
        foreach (var loc in LocalizationSettings.AvailableLocales.Locales)
        {
            if (string.Equals(loc.Identifier.Code, localeCode, System.StringComparison.OrdinalIgnoreCase))
            {
                target = loc; break;
            }
        }
        if (target == null)
        {
            Debug.LogWarning($"[LanguageSelectUI] Locale을 찾을 수 없습니다: {localeCode} (Locales에 ko/en/ja 등록 필요)");
            yield break;
        }

        // 4) 로케일 변경
        LocalizationSettings.SelectedLocale = target;

        // 5) 모든 LocalizeStringEvent 강제 새로고침 (버전 호환 안전)
        ForceRefreshAllLocalizedStrings();

        // 6) DataManager 세이브에도 기록(옵션)
        if (writeToSave && DataManager.instance != null)
        {
            string dmCode = _currentLang; // DM은 ko/en/jp 포맷 그대로 저장
            DataManager.instance.SetLanguageCode(dmCode, saveImmediately);
        }

        // 7) 표시 라벨 갱신
        RefreshLabel();
        Debug.Log($"[LanguageSelectUI] Switched to {target.Identifier.Code} ({target.LocaleName})");
    }

    private void ForceRefreshAllLocalizedStrings()
    {
        var events = Resources.FindObjectsOfTypeAll<LocalizeStringEvent>();
        foreach (var ev in events)
        {
            if (!ev || !ev.gameObject.scene.IsValid() || !ev.gameObject.scene.isLoaded) continue;
            ev.RefreshString();
        }
    }

    private void RefreshLabel()
    {
        if (!currentLangText) return;

        // DataManager가 있으면 세이브된 값을, 없으면 현재 앱 상태를 보여줌
        string code = _currentLang;
        if (DataManager.instance != null)
        {
            var dm = DataManager.instance.GetLanguageCode();
            if (!string.IsNullOrEmpty(dm)) code = SafeNormalizeAppCode(dm);
        }

        currentLangText.text = $"언어: {code}";
    }

    // ====== 새 게임 / 이어하기 플로우 ======

    private IEnumerator StartNewGameFlow()
    {
        if (DataManager.instance == null)
        {
            Debug.LogError("[LanguageSelectUI] DataManager instance가 필요합니다.");
            yield break;
        }

        // 현재 UI 언어(_currentLang)를 세이브/DB에 기록하고 새 게임 시작
        int slot = PickNewSaveSlot(defaultNewSlot);
        DataManager.instance.nowSlot = slot;

        // PlayerData 초기화 후 언어 세팅
        DataManager.instance.DataClear(); // nowSlot 초기화되므로 다시 지정
        DataManager.instance.nowSlot = slot;
        DataManager.instance.SetLanguageCode(_currentLang, saveImmediately: false);

        // 기본 초기값(원하면 커스터마이즈)
        // DataManager.instance.SetPlayerName("Player");
        // DataManager.instance.SetLevel(1);

        // 세이브 생성
        DataManager.instance.SaveData();

        // DB에도 언어 저장(실구현으로 교체)
        yield return SaveLanguageToDB(_currentLang);

        // 게임 씬 로드
        yield return LoadGameScene();
    }

    private IEnumerator ContinueFlow()
    {
        if (DataManager.instance == null || !DataManager.instance.HasAnySave())
        {
            Debug.LogWarning("[LanguageSelectUI] 이어하기 가능한 세이브가 없습니다.");
            yield break;
        }

        // '이어하기'에서는 데이터 전체를 로드하는 것이 맞습니다.
        if (!DataManager.instance.TryLoadMostRecentSave())
        {
            Debug.LogError("[LanguageSelectUI] 최신 세이브 로드 실패");
            yield break;
        }

        // 세이브 언어로 UI 보정(시작화면 텍스트도 반영)
        string dmCode = SafeNormalizeAppCode(DataManager.instance.GetLanguageCode());
        yield return SwitchLanguage(dmCode, writeToSave: false);

        // 게임 씬 로드
        yield return LoadGameScene();
    }

    // ====== DB/씬/헬퍼 ======

    private IEnumerator SaveLanguageToDB(string code)
    {
        // TODO: UnityWebRequest / Firebase / PlayFab 등으로 교체
        yield return null; // 데모: 한 프레임 뒤 성공 처리
        // 실패하더라도 게임 진행은 가능하도록 설계
    }

    private IEnumerator LoadGameScene()
    {
        if (string.IsNullOrEmpty(gameSceneName))
        {
            Debug.LogError("[LanguageSelectUI] gameSceneName이 비었습니다.");
            yield break;
        }
        var op = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(gameSceneName);
        while (!op.isDone) yield return null;
    }

    private int PickNewSaveSlot(int preferred)
    {
        if (DataManager.instance == null) return Mathf.Max(0, preferred);
        if (!DataManager.instance.ExistsSlot(preferred)) return Mathf.Max(0, preferred);

        for (int i = 0; i < 100; i++)
            if (!DataManager.instance.ExistsSlot(i)) return i;

        return Mathf.Max(0, preferred); // 다 차면 기본 슬롯 덮어쓰기
    }

    // 앱 내부 표준 포맷(ko/en/jp) 보정
    private string SafeNormalizeAppCode(string code)
    {
        if (string.IsNullOrEmpty(code)) return "ko";
        code = code.ToLowerInvariant();
        if (code == "ja") return "jp"; // 외부에서 ja 들어오면 앱은 jp로 통일
        if (code is "ko" or "en" or "jp") return code;
        return "ko";
    }
}