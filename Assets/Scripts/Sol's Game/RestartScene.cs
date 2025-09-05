using UnityEngine;
using UnityEngine.SceneManagement;

public class RestartScene : MonoBehaviour
{
    [Header("재시작 옵션")]
    [Tooltip("재시작 전에 딜레이(초). 0이면 즉시 재시작")]
    public float delay = 0f;

    [Tooltip("재시작 시 Time.timeScale을 1로 되돌릴지 여부(일시정지 해제)")]
    public bool resetTimeScale = true;

    /// <summary>
    /// UI 버튼의 OnClick에 연결하세요.
    /// </summary>
    public void OnClickRestart()
    {
        if (resetTimeScale) Time.timeScale = 1f;

        if (delay <= 0f)
        {
            ReloadActiveScene();
        }
        else
        {
            Invoke(nameof(ReloadActiveScene), delay);
        }
    }

    /// <summary>
    /// (선택) 키보드 단축키로도 재시작하고 싶다면 R 키 사용
    /// </summary>
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            OnClickRestart();
        }
    }

    // 실제 씬 재로딩
    private void ReloadActiveScene()
    {
        var scene = SceneManager.GetActiveScene();
        Debug.Log($"[재시작] '{scene.name}' 씬을 다시 로드합니다.");
        SceneManager.LoadScene(scene.buildIndex);
    }
}
