using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-100)]
public class SceneBgmRouter : MonoBehaviour
{
    [Serializable]
    public struct Route
    {
        public string sceneName;
        public string bgmKey;
        public float startTime;
        public float fadeSeconds;
    }

    [Header("씬별 BGM 설정")]
    public List<Route> routes = new List<Route>();

    [Header("옵션")]
    public float defaultFadeSeconds = 0.75f;
    public MissingRoutePolicy missingRoutePolicy = MissingRoutePolicy.Keep;

    public enum MissingRoutePolicy { Keep, Stop }

    private static SceneBgmRouter _instance;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void Start()
    {
        ApplyForCurrentScene();
    }

    private void OnDestroy()
    {
        if (_instance == this)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _instance = null;
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // 씬 로드 감지 로그
        Debug.Log($"[BgmRouter] 씬 로드 감지됨: '{scene.name}'");
        ApplyForSceneName(scene.name);
    }

    private void ApplyForCurrentScene()
    {
        ApplyForSceneName(SceneManager.GetActiveScene().name);
    }

    private void ApplyForSceneName(string sceneName)
    {
        if (SoundManager.Instance == null)
        {
            Debug.LogError("[BgmRouter] SoundManager가 없습니다! 시작 씬에 배치되었는지 확인하세요.");
            return;
        }

        Route route;
        if (TryFindRoute(sceneName, out route))
        {
            float fade = route.fadeSeconds > 0f ? route.fadeSeconds : defaultFadeSeconds;

            // 라우팅 성공 로그
            Debug.Log($"[BgmRouter] 설정 발견! 씬: '{sceneName}' -> BGM: '{route.bgmKey}'");

            if (string.IsNullOrEmpty(route.bgmKey))
            {
                SoundManager.Instance.StopBGM(fade);
            }
            else
            {
                SoundManager.Instance.PlayBGM(route.bgmKey, fade, Mathf.Max(0f, route.startTime));
            }
        }
        else
        {
            // 라우팅 실패 로그 (이게 뜨면 Scene Name 오타임)
            if (missingRoutePolicy == MissingRoutePolicy.Stop)
            {
                Debug.Log($"[BgmRouter] '{sceneName}' 설정 없음. 정책에 따라 BGM 정지.");
                SoundManager.Instance.StopBGM(defaultFadeSeconds);
            }
            else
            {
                Debug.Log($"[BgmRouter] '{sceneName}' 설정 없음. 기존 BGM 유지(Keep).");
            }
        }
    }

    private bool TryFindRoute(string sceneName, out Route route)
    {
        foreach (var r in routes)
        {
            // 대소문자 무시하고 비교
            if (string.Equals(r.sceneName, sceneName, StringComparison.OrdinalIgnoreCase))
            {
                route = r;
                return true;
            }
        }
        route = default;
        return false;
    }
}