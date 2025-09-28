// Unity 6 (LTS)
// CSV 기반 다국어 대화 시스템 (한 파일 완성본)
// - 스페이스바: 다음 대사로 진행
// - 한국어/영어/일본어 버튼: 해당 언어만 출력
// - CSV 형식: Id,ko,en,ja
//   예) Dialog_0001,"안녕!","Hello!","やあ！"
//
// 구성 요소
// 1) GameLanguage 열거형
// 2) LocalizationManager: 현재 언어 전역 관리
// 3) DialogueRow, DialogueDatabase: CSV 로드/파싱 및 행 접근
// 4) DialogueRunnerLocalized: UI 연결, 입력 처리, 행 출력
//
// 사용법
// - 빈 GameObject에 본 스크립트를 붙이고, Mode를 "Runner"로 두면 러너로 동작
// - 초기 언어는 LocalizationManager 초기값으로 결정
// - DialogueRunnerLocalized 섹션의 Text, Buttons, csvFile을 인스펙터에서 연결
//
// 주의
// - TextMeshProUGUI 사용 권장. Text도 지원.
// - 따옴표와 콤마를 안전하게 처리하는 간단 CSV 파서 포함.
// - 선택한 언어 칼럼이 비어 있으면 "[해당 언어 대사가 비어 있습니다]" 안내 문구 출력.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

#if TMP_PRESENT
using TMPro;
#endif

public class LocalizedDialogueOneFile : MonoBehaviour
{
    // ------------------------------------------------------------
    // 0) 실행 모드: 하나의 파일로 묶었으므로, 같은 스크립트를
    //    "Manager" 또는 "Runner"로 전환해 재사용 가능
    // ------------------------------------------------------------
    public enum Mode { Manager, Runner }
    [Header("이 스크립트의 실행 모드")]
    [SerializeField] private Mode mode = Mode.Runner;

    // ------------------------------------------------------------
    // 1) 언어 열거형
    // ------------------------------------------------------------
    public enum GameLanguage
    {
        Korean,
        English,
        Japanese
    }

    // ------------------------------------------------------------
    // 2) LocalizationManager: 현재 언어 전역 관리
    // ------------------------------------------------------------
    public class LocalizationManager : MonoBehaviour
    {
        public static LocalizationManager Instance { get; private set; }

        [Header("초기 언어")]
        [SerializeField] private GameLanguage initialLanguage = GameLanguage.Korean;

        public GameLanguage CurrentLanguage { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            // 필요 시 주석 해제하여 씬 전환 유지
            // DontDestroyOnLoad(gameObject);

            CurrentLanguage = initialLanguage;
        }

        public void SetLanguage(GameLanguage lang)
        {
            CurrentLanguage = lang;
            // 필요 시 언어 변경 이벤트를 추가해 즉시 UI 갱신 가능
        }
    }

    // ------------------------------------------------------------
    // 3) 대사 행 구조 및 CSV 데이터베이스
    // ------------------------------------------------------------
    [Serializable]
    public struct DialogueRow
    {
        public string id;
        public string ko;
        public string en;
        public string ja;
    }

    public class DialogueDatabase
    {
        private readonly List<DialogueRow> rows = new List<DialogueRow>();
        private readonly Dictionary<string, int> indexById = new Dictionary<string, int>();

        public int Count => rows.Count;

        public DialogueRow this[int i] => rows[i];

        public bool TryGetById(string id, out DialogueRow row)
        {
            if (indexById.TryGetValue(id, out var idx))
            {
                row = rows[idx];
                return true;
            }
            row = default;
            return false;
        }

        // CSV 로드: UTF-8 기준, 따옴표/콤마 처리
        public void LoadFromCsv(TextAsset csv)
        {
            rows.Clear();
            indexById.Clear();

            if (csv == null)
            {
                Debug.LogError("[DialogueDatabase] csv가 비어 있습니다.");
                return;
            }

            using (var reader = new StringReader(csv.text))
            {
                string line;
                bool isHeaderProcessed = false;

                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var fields = ParseCsvLine(line);
                    if (!isHeaderProcessed)
                    {
                        // 헤더 스킵. 최소 4칼럼(Id,ko,en,ja) 예상
                        isHeaderProcessed = true;
                        if (fields.Count < 4)
                        {
                            Debug.LogError($"[DialogueDatabase] CSV 헤더 칼럼 수가 부족합니다. 라인: {line}");
                        }
                        continue;
                    }

                    if (fields.Count < 4)
                    {
                        Debug.LogError($"[DialogueDatabase] CSV 칼럼 수가 부족합니다. 라인: {line}");
                        continue;
                    }

                    var row = new DialogueRow
                    {
                        id = fields[0],
                        ko = fields[1],
                        en = fields[2],
                        ja = fields[3]
                    };
                    int newIndex = rows.Count;
                    rows.Add(row);

                    if (!string.IsNullOrEmpty(row.id))
                    {
                        if (!indexById.ContainsKey(row.id))
                            indexById.Add(row.id, newIndex);
                    }
                }
            }
        }

        // 간단 CSV 라인 파서: 따옴표로 감싼 필드와 내부 콤마 처리
        private static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        // 연속 쌍따옴표("")는 이스케이프된 따옴표로 처리
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            sb.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                else
                {
                    if (c == ',')
                    {
                        result.Add(sb.ToString());
                        sb.Length = 0;
                    }
                    else if (c == '"')
                    {
                        inQuotes = true;
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
            }
            result.Add(sb.ToString());
            return result;
        }
    }

    // ------------------------------------------------------------
    // 4) DialogueRunnerLocalized: 입력 처리 + UI 출력
    // ------------------------------------------------------------
    [Serializable]
    public class RunnerUIRefs
    {
        [Header("CSV 파일")]
        public TextAsset csvFile;

        [Header("대사 출력 대상")]
        public TextMeshProUGUI legacyText; 


        [Header("언어 선택 버튼(선택 연결)")]
        public Button koreanButton;
        public Button englishButton;
        public Button japaneseButton;

        [Header("초기 재생 옵션")]
        public bool showFirstLineOnStart = true;
    }

    [Header("Runner 전용 UI 참조")]
    [SerializeField] private RunnerUIRefs ui = new RunnerUIRefs();

    private LocalizationManager managerInstance;
    private DialogueDatabase db;
    private int cursor = -1; // -1이면 아직 시작 전 상태

    // ------------------------------------------------------------
    // 공용 수명주기: 모드에 따라 Manager 또는 Runner로 동작
    // ------------------------------------------------------------
    private void Awake()
    {
        if (mode == Mode.Manager)
        {
            // 자신을 LocalizationManager로 변신시킴
            if (GetComponent<LocalizationManager>() == null)
                gameObject.AddComponent<LocalizationManager>();
            return;
        }

        // Runner 모드: LocalizationManager가 없으면 자동 생성
        managerInstance = FindFirstObjectByType<LocalizationManager>();
        if (managerInstance == null)
        {
            managerInstance = gameObject.AddComponent<LocalizationManager>();
        }

        // CSV 로드
        db = new DialogueDatabase();
        db.LoadFromCsv(ui.csvFile);

        // 버튼 이벤트 연결
        WireButtons();
    }

    private void Start()
    {
        if (mode == Mode.Runner && ui.showFirstLineOnStart)
        {
            MoveNextAndRender();
        }
    }

    private void Update()
    {
        if (mode != Mode.Runner) return;

        // 스페이스바 입력으로 다음 대사
        if (Input.GetKeyDown(KeyCode.Space))
        {
            MoveNextAndRender();
        }
    }

    // ------------------------------------------------------------
    // 버튼 연결
    // ------------------------------------------------------------
    private void WireButtons()
    {
        if (ui.koreanButton != null)
            ui.koreanButton.onClick.AddListener(() =>
            {
                managerInstance.SetLanguage(GameLanguage.Korean);
                ReRenderCurrent();
            });

        if (ui.englishButton != null)
            ui.englishButton.onClick.AddListener(() =>
            {
                managerInstance.SetLanguage(GameLanguage.English);
                ReRenderCurrent();
            });

        if (ui.japaneseButton != null)
            ui.japaneseButton.onClick.AddListener(() =>
            {
                managerInstance.SetLanguage(GameLanguage.Japanese);
                ReRenderCurrent();
            });
    }

    // ------------------------------------------------------------
    // 진행 및 렌더
    // ------------------------------------------------------------
    private void MoveNextAndRender()
    {
        if (db == null || db.Count == 0)
        {
            RenderText("[CSV에 대사가 없습니다]");
            return;
        }

        if (cursor < 0) cursor = 0;
        else cursor++;

        if (cursor >= db.Count)
        {
            RenderText("[대사 끝]");
            return;
        }

        var row = db[cursor];
        RenderRow(row);
    }

    private void ReRenderCurrent()
    {
        if (cursor < 0 || db == null || db.Count == 0) return;
        var row = db[cursor];
        RenderRow(row);
    }

    private void RenderRow(DialogueRow row)
    {
        string text = GetTextForCurrentLanguage(row, managerInstance.CurrentLanguage);
        RenderText(text);
    }

    private string GetTextForCurrentLanguage(DialogueRow row, GameLanguage lang)
    {
        switch (lang)
        {
            case GameLanguage.Korean:
                return string.IsNullOrEmpty(row.ko) ? "[해당 언어 대사가 비어 있습니다]" : row.ko;
            case GameLanguage.English:
                return string.IsNullOrEmpty(row.en) ? "[No text for selected language]" : row.en;
            case GameLanguage.Japanese:
                return string.IsNullOrEmpty(row.ja) ? "[選択した言語のテキストがありません]" : row.ja;
            default:
                return row.ko;
        }
    }

    private void RenderText(string s)
    {
        bool printed = false;

#if TMP_PRESENT
        if (ui.tmpText != null)
        {
            ui.tmpText.text = s;
            printed = true;
        }
#endif
        if (ui.legacyText != null)
        {
            ui.legacyText.text = s;
            printed = true;
        }

        if (!printed)
        {
            Debug.LogWarning("[DialogueRunner] 출력 대상(Text 또는 TMP)이 연결되지 않았습니다.");
        }
    }
}
