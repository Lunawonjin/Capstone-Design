using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Sol's Game Puzzle 씬 전환 및 퍼즐 클리어 이벤트 관리
/// Starest 씬의 빈 오브젝트에 부착하여 사용
/// </summary>
public class PuzzleSceneManager : MonoBehaviour
{
    [Header("씬 이름")]
    [SerializeField] private string puzzleSceneName = "Sol's Game Puzzle";
    [SerializeField] private string starestSceneName = "Starest";

    [Header("퍼즐 클리어 이벤트")]
    [SerializeField] private string puzzleClearOwner = "Sol";
    [SerializeField] private string puzzleClearEvent = "Sol_Puzzle_Clear";

    [Header("플래그 이름")]
    [SerializeField] private string secondMeetFlag = "Sol_Second_Meet";
    [SerializeField] private string puzzleClearFlag = "Sol_Puzzle_Clear";

    [Header("디버그")]
    [SerializeField] private bool verboseLog = true;

    private string _previousSceneName = "";
    private bool _hasTriggeredClearEvent = false;

    private void Start()
    {
        _previousSceneName = SceneManager.GetActiveScene().name;
    }

    private void Update()
    {
        CheckForPuzzleReturn();
    }

    private void CheckForPuzzleReturn()
    {
        string currentScene = SceneManager.GetActiveScene().name;

        // 씬이 바뀌지 않았으면 리턴
        if (string.Equals(_previousSceneName, currentScene, StringComparison.Ordinal))
            return;

        // 퍼즐 → Starest 복귀 체크
        bool returnedFromPuzzle =
            string.Equals(_previousSceneName, puzzleSceneName, StringComparison.Ordinal) &&
            string.Equals(currentScene, starestSceneName, StringComparison.Ordinal);

        _previousSceneName = currentScene;

        if (!returnedFromPuzzle) return;
        if (_hasTriggeredClearEvent) return;

        // 조건 체크 및 이벤트 실행
        StartCoroutine(CheckAndRunPuzzleClearEvent());
    }

    private IEnumerator CheckAndRunPuzzleClearEvent()
    {
        // DataManager 대기
        float timeout = 3f;
        float elapsed = 0f;
        while (DataManager.instance == null && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (DataManager.instance == null || DataManager.instance.nowPlayer == null)
        {
            if (verboseLog)
                Debug.LogWarning("[PuzzleSceneManager] DataManager를 찾을 수 없습니다.");
            yield break;
        }

        var pd = DataManager.instance.nowPlayer;

        // Sol_Second_Meet 체크
        bool secondMeet = GetBoolValue(pd, secondMeetFlag);
        if (!secondMeet)
        {
            if (verboseLog)
                Debug.Log($"[PuzzleSceneManager] {secondMeetFlag}가 false이므로 이벤트를 실행하지 않습니다.");
            yield break;
        }

        // Sol_Puzzle_Clear 체크 (이미 true면 스킵)
        bool alreadyCleared = GetBoolValue(pd, puzzleClearFlag);
        if (alreadyCleared)
        {
            if (verboseLog)
                Debug.Log($"[PuzzleSceneManager] {puzzleClearFlag}가 이미 true입니다. 스킵.");
            yield break;
        }

        // NpcEventDebugLoader 찾기
        var eventLoader = FindFirstObjectByType<NpcEventDebugLoader>();
        if (eventLoader == null)
        {
            Debug.LogError("[PuzzleSceneManager] NpcEventDebugLoader를 찾을 수 없습니다.");
            yield break;
        }

        // 이벤트 실행
        _hasTriggeredClearEvent = true;

        if (verboseLog)
            Debug.Log($"[PuzzleSceneManager] 퍼즐 클리어 이벤트 실행: {puzzleClearOwner}/{puzzleClearEvent}");

        bool success = eventLoader.RunEventByName_External(
            puzzleClearOwner,
            puzzleClearEvent,
            onComplete: () =>
            {
                // 이벤트 완료 후 플래그 설정
                SetBoolValue(pd, puzzleClearFlag, true);

                // 이벤트 완료 후 플레이어 위치를 (32.2, 18)로 고정
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    player.transform.position = new Vector3(32.2f, 18f, 0f);

                    // Rigidbody2D가 있으면 속도도 초기화
                    var rb2d = player.GetComponent<Rigidbody2D>();
                    if (rb2d != null)
                    {
                        rb2d.linearVelocity = Vector2.zero;
                        rb2d.angularVelocity = 0f;
                    }

                    if (verboseLog)
                        Debug.Log($"[PuzzleSceneManager] 이벤트 완료 후 플레이어 위치를 (32.2, 18)로 고정했습니다.");
                }

                // Sol NPC 활성화
                var solNpc = GameObject.Find("Sol");
                if (solNpc != null)
                {
                    solNpc.SetActive(true);
                    if (verboseLog)
                        Debug.Log("[PuzzleSceneManager] Sol NPC를 활성화했습니다.");
                }
                else if (verboseLog)
                {
                    Debug.LogWarning("[PuzzleSceneManager] Sol_Npc를 찾을 수 없습니다.");
                }

                if (verboseLog)
                    Debug.Log($"[PuzzleSceneManager] {puzzleClearFlag} 플래그를 true로 설정했습니다.");
            }
        );

        if (!success)
        {
            Debug.LogError($"[PuzzleSceneManager] 이벤트 실행 실패: {puzzleClearOwner}/{puzzleClearEvent}");
            _hasTriggeredClearEvent = false;
        }
    }

    // Reflection을 사용하여 bool 값 가져오기
    private bool GetBoolValue(object obj, string fieldName)
    {
        if (obj == null || string.IsNullOrEmpty(fieldName))
            return false;

        var type = obj.GetType();
        var flags = System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic;

        var field = type.GetField(fieldName, flags);
        if (field != null && field.FieldType == typeof(bool))
            return (bool)field.GetValue(obj);

        var prop = type.GetProperty(fieldName, flags);
        if (prop != null && prop.PropertyType == typeof(bool) && prop.CanRead)
            return (bool)prop.GetValue(obj, null);

        return false;
    }

    // Reflection을 사용하여 bool 값 설정하기
    private void SetBoolValue(object obj, string fieldName, bool value)
    {
        if (obj == null || string.IsNullOrEmpty(fieldName))
            return;

        var type = obj.GetType();
        var flags = System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic;

        var field = type.GetField(fieldName, flags);
        if (field != null && field.FieldType == typeof(bool))
        {
            field.SetValue(obj, value);
            return;
        }

        var prop = type.GetProperty(fieldName, flags);
        if (prop != null && prop.PropertyType == typeof(bool) && prop.CanWrite)
        {
            prop.SetValue(obj, value, null);
        }
    }

    /// <summary>
    /// 외부에서 호출: Starest → Puzzle 이동 시 위치 저장
    /// </summary>
    public void GoToPuzzleSceneWithSave()
    {
        if (DataManager.instance == null)
        {
            Debug.LogError("[PuzzleSceneManager] DataManager가 없습니다.");
            return;
        }

        // 플레이어 위치를 고정된 좌표(32.2, 18)로 저장
        Vector3 returnPosition = new Vector3(32.2f, 18f, 0f);
        DataManager.instance.SetPlayerPosition(returnPosition);

        if (verboseLog)
            Debug.Log($"[PuzzleSceneManager] 복귀 위치 저장: {returnPosition}");

        // 씬 정보 및 스냅샷 저장
        DataManager.instance.SetSceneName(starestSceneName);
        DataManager.instance.SubSaveCommitSceneSnapshotAllObjects();
        DataManager.instance.SubSaveCommit();

        if (verboseLog)
            Debug.Log($"[PuzzleSceneManager] {puzzleSceneName}으로 이동합니다.");

        // 씬 로드
        SceneManager.LoadScene(puzzleSceneName);
    }
}