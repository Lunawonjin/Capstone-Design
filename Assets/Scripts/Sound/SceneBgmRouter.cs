// SceneBgmRouter.cs
// Unity 6(LTS) 기준. 씬 로드 시 BGM을 자동으로 전환/정지.
// - Select.Start()에서 새 게임 시작으로 LoadScene 호출
// - StartMenu.Co_OnClickLoadGame()에서 저장 씬 비동기 로드
// 위 두 경우 모두 SceneManager.sceneLoaded에 걸려 자동 전환됨.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-100)] // 가능하면 일찍 초기화
[DisallowMultipleComponent]
public class SceneBgmRouter : MonoBehaviour
{
    [Serializable]
    public struct Route
    {
        [Tooltip("씬 이름(파일명과 동일)")]
        public string sceneName;

        [Tooltip("SoundManager의 BGM 키. 비우면 해당 씬에서 BGM 정지")]
        public string bgmKey;

        [Tooltip("해당 씬 진입 시 시작 지점(초)")]
        public float startTime;

        [Tooltip("크로스페이드 시간(초). 0 이하면 기본값 사용")]
        public float fadeSeconds;
    }

    [Header("씬별 BGM 라우팅")]
    public List<Route> routes = new List<Route>();

    [Header("기본 옵션")]
    [Tooltip("fadeSeconds가 0 이하일 때 사용하는 기본 크로스페이드 시간(초)")]
    public float defaultFadeSeconds = 0.75f;

    [Tooltip("현재 씬이 라우팅 표에 없을 때의 동작")]
    public MissingRoutePolicy missingRoutePolicy = MissingRoutePolicy.Keep;

    public enum MissingRoutePolicy
    {
        Keep,   // 기존 BGM 유지
        Stop    // 기존 BGM 정지
    }

    private static SceneBgmRouter _instance;

    private void Awake()
    {
        // 싱글턴 유지 (프로젝트에 하나만 존재)
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);

        SceneManager.sceneLoaded += OnSceneLoaded;

        // 첫 씬에도 즉시 반영(부트 씬에서 바로 적용하려는 경우)
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
            Debug.LogWarning("[SceneBgmRouter] SoundManager 인스턴스가 없습니다. 먼저 생성하세요.");
            return;
        }

        Route route;
        if (TryFindRoute(sceneName, out route))
        {
            float fade = route.fadeSeconds > 0f ? route.fadeSeconds : defaultFadeSeconds;

            if (string.IsNullOrEmpty(route.bgmKey))
            {
                // 키가 비어 있으면 정지
                SoundManager.Instance.StopBGM(fade);
            }
            else
            {
                // 해당 키로 크로스페이드 재생(무한 반복은 SoundManager에서 처리)
                SoundManager.Instance.PlayBGM(route.bgmKey, fade, Mathf.Max(0f, route.startTime));
            }
        }
        else
        {
            // 라우팅 표에 없을 때 정책 적용
            if (missingRoutePolicy == MissingRoutePolicy.Stop)
            {
                SoundManager.Instance.StopBGM(defaultFadeSeconds);
            }
            // Keep이면 아무 것도 하지 않음(현재 BGM 유지)
        }
    }

    private bool TryFindRoute(string sceneName, out Route route)
    {
        for (int i = 0; i < routes.Count; i++)
        {
            if (routes[i].sceneName == sceneName)
            {
                route = routes[i];
                return true;
            }
        }
        route = default;
        return false;
    }
}
