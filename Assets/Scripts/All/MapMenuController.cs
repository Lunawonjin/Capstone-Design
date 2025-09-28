// MapMenuController.cs
// Unity 6 (LTS)
// Feature: Map toggles with M and Esc, and now also closes via Exit button.

using System;
using System.Linq;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class MapMenuController : MonoBehaviour
{
    public enum SceneIdMode { ByName, ByBuildIndex }

    [Header("Menu Items (clickable)")]
    [SerializeField] private GameObject[] menuItems = Array.Empty<GameObject>();

    [Header("Scene Id Mode")]
    [SerializeField] private SceneIdMode sceneIdMode = SceneIdMode.ByBuildIndex;

    [Header("Target Scene Names (align with menuItems)")]
    [SerializeField] private string[] sceneNames = Array.Empty<string>();

    [Header("Target Scene Build Indices (align with menuItems)")]
    [SerializeField] private int[] sceneBuildIndices = Array.Empty<int>();

    [Header("Hover Visuals")]
    [SerializeField, Range(1.0f, 2.0f)] private float hoverScale = 1.08f;
    [SerializeField] private Color hoverColor = new Color(1.05f, 1.05f, 1.05f, 1f);
    [SerializeField] private float normalScale = 1.0f;
    [SerializeField] private bool revertToOriginalColor = true;

    [Header("Map / MapPanel")]
    [SerializeField] private GameObject map;      // animated ON/OFF
    [SerializeField] private GameObject mapPanel; // immediate ON/OFF

    [Header("UI Exclusive Group (optional)")]
    [SerializeField] private UIExclusiveManager uiGroup;

    [Header("Hotkeys")]
    [SerializeField] private KeyCode openKey = KeyCode.M;
    [SerializeField] private KeyCode closeKey = KeyCode.Escape;
    [SerializeField] private KeyCode[] extraCloseKeys = { KeyCode.M, KeyCode.Escape };

    [Header("Buttons")]
    [SerializeField] private Button exitButton;   // NEW: click to close map

    [Header("Map Animation (Unscaled Time)")]
    [SerializeField] private Vector3 openStartScale = new Vector3(0.9f, 0.9f, 1f);
    [SerializeField] private Vector3 openEndScale = Vector3.one;
    [SerializeField, Min(0.01f)] private float openDuration = 0.14f;
    [SerializeField] private Vector3 closeStartScale = Vector3.one;
    [SerializeField] private Vector3 closeEndScale = new Vector3(0.9f, 0.9f, 1f);
    [SerializeField, Min(0.01f)] private float closeDuration = 0.12f;

    [Header("Alpha Fade (CanvasGroup on Map)")]
    [SerializeField] private bool useAlphaFade = true;

    [Header("Panel Watchdog (sync external SetActive)")]
    [SerializeField] private bool syncWithPanelActive = true;

    // Shopping Center restriction
    [Header("Shopping Center (weekend only)")]
    [SerializeField] private int shopItemIndex = 2;
    [SerializeField] private string shopSceneName = "Shopping Center";

    [Header("Notification (block on weekdays)")]
    [SerializeField] private GameObject notificationRoot;
    [SerializeField] private Button okButton;
    [SerializeField] private bool notificationBlocksClicks = true;
    [SerializeField, Min(0.01f)] private float notifOpenDuration = 0.14f;
    [SerializeField, Min(0.01f)] private float notifCloseDuration = 0.12f;
    [SerializeField] private Vector3 notifStartScale = new Vector3(0.9f, 0.9f, 1f);
    [SerializeField] private Vector3 notifEndScale = Vector3.one;

    // internals
    RectTransform _mapRT; CanvasGroup _mapCG;
    RectTransform _notifRT; CanvasGroup _notifCG;
    bool _isOpen, _animating, _notifAnimating;

    // notification state
    bool _notifOpen;

    Coroutine _openCo, _closeCo, _notifOpenCo, _notifCloseCo;

    void Awake()
    {
        if (!map || !mapPanel)
        {
            Debug.LogError("[MapMenuController] map / mapPanel reference required.");
            enabled = false; return;
        }

        _mapRT = map.GetComponent<RectTransform>();
        _mapCG = map.GetComponent<CanvasGroup>();

        // init states
        mapPanel.SetActive(false);
        map.SetActive(false);
        if (_mapRT) _mapRT.localScale = openEndScale;
        if (_mapCG && useAlphaFade)
        {
            _mapCG.alpha = 0f;
            _mapCG.interactable = false;
            _mapCG.blocksRaycasts = false;
        }
        _isOpen = _animating = false;

        // notification init
        if (notificationRoot)
        {
            _notifRT = notificationRoot.GetComponent<RectTransform>();
            _notifCG = notificationRoot.GetComponent<CanvasGroup>();
            if (_notifRT) _notifRT.localScale = notifEndScale;
            if (_notifCG)
            {
                _notifCG.alpha = 0f;
                _notifCG.interactable = true;
                _notifCG.blocksRaycasts = notificationBlocksClicks;
            }
            notificationRoot.SetActive(false);
            _notifOpen = false;

            if (okButton) okButton.onClick.AddListener(HideNotification);
        }

#if UNITY_2023_1_OR_NEWER
        if (!uiGroup) uiGroup = UnityEngine.Object.FindAnyObjectByType<UIExclusiveManager>() ?? UnityEngine.Object.FindFirstObjectByType<UIExclusiveManager>();
#else
        if (!uiGroup) uiGroup = UnityEngine.Object.FindObjectOfType<UIExclusiveManager>();
#endif

        // bind hover items
        for (int i = 0; i < menuItems.Length; i++)
        {
            var go = menuItems[i]; if (!go) continue;
            var hover = go.GetComponent<HoverableMenuItem>() ?? go.AddComponent<HoverableMenuItem>();
            hover.SetVisualParams(normalScale, hoverScale, hoverColor, revertToOriginalColor);
            int idx = i;
            hover.onClick = () => OnMenuClick(idx);
        }

        // auto map build indices
        AutoResolveBuildIndices();

        // NEW: Exit button wiring
        if (exitButton) exitButton.onClick.AddListener(OnClickExit);
    }

    void OnDestroy()
    {
        if (okButton) okButton.onClick.RemoveListener(HideNotification);
        if (exitButton) exitButton.onClick.RemoveListener(OnClickExit);
    }

    void Update()
    {
        // if notification is open, Esc closes only the notification
        if (_notifOpen && Input.GetKeyDown(KeyCode.Escape))
        {
            HideNotification();
            return;
        }

        if (Input.GetKeyDown(openKey)) OpenMap();

        // toggle/close by keys
        if ((_isOpen && Input.GetKeyDown(closeKey)) || extraCloseKeys.Any(Input.GetKeyDown))
            CloseMap();

        // watchdog sync
        if (syncWithPanelActive)
        {
            if (mapPanel.activeSelf && !_isOpen && !_animating) OpenMap(fromPanelWatchdog: true);
            else if (!mapPanel.activeSelf && (_isOpen || _animating)) ForceCloseImmediately();
        }
    }

    // NEW: Exit button handler (closes map)
    void OnClickExit()
    {
        // if notification is open and it blocks clicks, ignore
        if (_notifOpen) return;
        CloseMap();
    }

    // menu click
    void OnMenuClick(int idx)
    {
        if (_notifOpen) return;

        // weekend restriction for shopping
        if (idx == shopItemIndex && IsShopping(idx))
        {
            bool weekend = DataManager.instance != null && DataManager.instance.IsWeekend;
            if (!weekend) { ShowNotification(); return; }
        }

        var active = SceneManager.GetActiveScene();

        if (sceneIdMode == SceneIdMode.ByBuildIndex)
        {
            int build = (sceneBuildIndices != null && idx >= 0 && idx < sceneBuildIndices.Length) ? sceneBuildIndices[idx] : -1;

            if (build >= 0 && build < SceneManager.sceneCountInBuildSettings)
            {
                if (active.buildIndex == build) { CloseMap(); return; }
                SceneManager.LoadScene(build);
                return;
            }
            // fallback by name
        }

        string name = (sceneNames != null && idx >= 0 && idx < sceneNames.Length) ? sceneNames[idx] : null;
        if (string.IsNullOrWhiteSpace(name))
        {
            Debug.LogWarning($"[MapMenu] scene name missing at index {idx}");
            return;
        }

        if (SceneNameEqualsRobust(active.name, name)) { CloseMap(); return; }
        SceneManager.LoadScene(name);
    }

    bool IsShopping(int idx)
    {
        string name = (sceneNames != null && idx >= 0 && idx < sceneNames.Length) ? sceneNames[idx] : null;
        return !string.IsNullOrEmpty(name) && SceneNameEqualsRobust(name, shopSceneName);
    }

    // public API
    public void OpenMap(bool fromPanelWatchdog = false)
    {
        if (_animating || _isOpen) return;
        if (!fromPanelWatchdog && uiGroup != null && !uiGroup.TryActivate(mapPanel)) return;

        if (!mapPanel.activeSelf) mapPanel.SetActive(true);

        if (_closeCo != null) StopCoroutine(_closeCo);
        _openCo = StartCoroutine(Co_OpenMap());
    }

    public void CloseMap()
    {
        if (_animating || !_isOpen) return;
        if (_openCo != null) StopCoroutine(_openCo);
        _closeCo = StartCoroutine(Co_CloseMap());
    }

    void ForceCloseImmediately()
    {
        if (_openCo != null) StopCoroutine(_openCo);
        if (_closeCo != null) StopCoroutine(_closeCo);
        _animating = false; _isOpen = false;

        if (map.activeSelf) map.SetActive(false);
        if (mapPanel.activeSelf) mapPanel.SetActive(false);

        if (_mapCG && useAlphaFade) { _mapCG.alpha = 0f; _mapCG.interactable = false; _mapCG.blocksRaycasts = false; }
        if (_mapRT) _mapRT.localScale = closeEndScale;
    }

    IEnumerator Co_OpenMap()
    {
        _isOpen = true; _animating = true;

        if (!map.activeSelf) map.SetActive(true);
        if (_mapRT) _mapRT.localScale = openStartScale;
        if (_mapCG && useAlphaFade) { _mapCG.alpha = 0f; _mapCG.interactable = false; _mapCG.blocksRaycasts = false; }

        float t = 0f, d = Mathf.Max(0.01f, openDuration);
        while (t < d)
        {
            float u = t / d, e = 1f - Mathf.Pow(1f - u, 3f);
            if (_mapRT) _mapRT.localScale = Vector3.LerpUnclamped(openStartScale, openEndScale, e);
            if (_mapCG && useAlphaFade) _mapCG.alpha = Mathf.LerpUnclamped(0f, 1f, e);
            t += Time.unscaledDeltaTime; yield return null;
        }

        if (_mapRT) _mapRT.localScale = openEndScale;
        if (_mapCG && useAlphaFade) { _mapCG.alpha = 1f; _mapCG.interactable = true; _mapCG.blocksRaycasts = true; }

        _animating = false;
    }

    IEnumerator Co_CloseMap()
    {
        _animating = true;
        if (_mapCG && useAlphaFade) { _mapCG.interactable = false; _mapCG.blocksRaycasts = false; }

        float t = 0f, d = Mathf.Max(0.01f, closeDuration);
        float startAlpha = (_mapCG && useAlphaFade) ? _mapCG.alpha : 1f;

        while (t < d)
        {
            float u = t / d, e = Mathf.Pow(u, 3f);
            if (_mapRT) _mapRT.localScale = Vector3.LerpUnclamped(closeStartScale, closeEndScale, e);
            if (_mapCG && useAlphaFade) _mapCG.alpha = Mathf.LerpUnclamped(startAlpha, 0f, e);
            t += Time.unscaledDeltaTime; yield return null;
        }

        if (_mapRT) _mapRT.localScale = closeEndScale;
        if (_mapCG && useAlphaFade) _mapCG.alpha = 0f;

        if (map.activeSelf) map.SetActive(false);
        if (mapPanel.activeSelf) mapPanel.SetActive(false);

        _animating = false; _isOpen = false;
    }

    // notification
    void ShowNotification()
    {
        if (!notificationRoot || _notifAnimating) return;
        if (_notifCloseCo != null) StopCoroutine(_notifCloseCo);

        _notifOpen = true;

        notificationRoot.SetActive(true);
        if (_notifRT) _notifRT.localScale = notifStartScale;
        if (_notifCG)
        {
            _notifCG.alpha = 0f;
            _notifCG.interactable = true;
            _notifCG.blocksRaycasts = notificationBlocksClicks;
        }

        _notifOpenCo = StartCoroutine(Co_ShowNotification());
    }

    void HideNotification()
    {
        if (!notificationRoot || _notifAnimating || !notificationRoot.activeSelf) return;
        if (_notifOpenCo != null) StopCoroutine(_notifOpenCo);
        _notifCloseCo = StartCoroutine(Co_HideNotification());
    }

    IEnumerator Co_ShowNotification()
    {
        _notifAnimating = true;
        float t = 0f, d = Mathf.Max(0.01f, notifOpenDuration);
        while (t < d)
        {
            float u = t / d, e = 1f - Mathf.Pow(1f - u, 3f);
            if (_notifRT) _notifRT.localScale = Vector3.LerpUnclamped(notifStartScale, notifEndScale, e);
            if (_notifCG) _notifCG.alpha = Mathf.LerpUnclamped(0f, 1f, e);
            t += Time.unscaledDeltaTime; yield return null;
        }
        if (_notifRT) _notifRT.localScale = notifEndScale;
        if (_notifCG) { _notifCG.alpha = 1f; }
        _notifAnimating = false;
    }

    IEnumerator Co_HideNotification()
    {
        _notifAnimating = true;
        float t = 0f, d = Mathf.Max(0.01f, notifCloseDuration);
        float startAlpha = _notifCG ? _notifCG.alpha : 1f;
        while (t < d)
        {
            float u = t / d, e = Mathf.Pow(u, 3f);
            if (_notifRT) _notifRT.localScale = Vector3.LerpUnclamped(notifEndScale, notifStartScale, e);
            if (_notifCG) _notifCG.alpha = Mathf.LerpUnclamped(startAlpha, 0f, e);
            t += Time.unscaledDeltaTime; yield return null;
        }
        if (_notifRT) _notifRT.localScale = notifStartScale;
        if (_notifCG) _notifCG.alpha = 0f;

        notificationRoot.SetActive(false);
        _notifAnimating = false;
        _notifOpen = false;
    }

    // utils
    void AutoResolveBuildIndices()
    {
        if (sceneIdMode != SceneIdMode.ByBuildIndex) return;
        if (sceneBuildIndices == null || sceneBuildIndices.Length != menuItems.Length)
            sceneBuildIndices = Enumerable.Repeat(-1, menuItems.Length).ToArray();

        int count = SceneManager.sceneCountInBuildSettings;
        var nameToIndex = new System.Collections.Generic.Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < count; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (!nameToIndex.ContainsKey(name)) nameToIndex.Add(name, i);
        }

        for (int i = 0; i < sceneBuildIndices.Length && i < sceneNames.Length; i++)
        {
            if (sceneBuildIndices[i] >= 0) continue;
            string want = sceneNames[i];
            if (string.IsNullOrWhiteSpace(want)) continue;
            string norm = Normalize(want);
            if (nameToIndex.TryGetValue(norm, out int idx)) sceneBuildIndices[i] = idx;
            else
            {
                foreach (var kv in nameToIndex)
                {
                    if (SceneNameEqualsRobust(kv.Key, norm)) { sceneBuildIndices[i] = kv.Value; break; }
                }
            }
        }
    }

    static bool SceneNameEqualsRobust(string a, string b)
    {
        string na = Normalize(a), nb = Normalize(b);
        return string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
    }
    static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        s = s.Trim();
        return new string(s.Where(ch => ch != ' ' && ch != '\'' && ch != '’').ToArray());
    }
}

[RequireComponent(typeof(RectTransform))]
public class HoverableMenuItem : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    public System.Action onClick;

    [SerializeField] private float _normalScale = 1f;
    [SerializeField] private float _hoverScale = 1.08f;
    [SerializeField] private Color _hoverColor = new Color(1.05f, 1.05f, 1.05f, 1f);
    [SerializeField] private bool _revertToOriginalColor = true;

    private RectTransform _rect;
    private Graphic _graphic;

    private Color _baseColor;
    private bool _hasBaseColor;

    private void Reset()
    {
        _normalScale = 1f;
        _hoverScale = 1.08f;
        _hoverColor = new Color(1.05f, 1.05f, 1.05f, 1f);
        _revertToOriginalColor = true;
    }

    private void Awake()
    {
        _rect = GetComponent<RectTransform>();
        _graphic = GetComponent<Graphic>();

        if (_graphic != null)
        {
            _baseColor = _graphic.color;
            _hasBaseColor = true;
        }
        else
        {
            Debug.LogWarning($"[HoverableMenuItem] '{name}' has no Graphic, color effect disabled", this);
        }

        SetScale(_normalScale);
    }

    private void OnEnable()
    {
        if (_graphic != null && _hasBaseColor)
            _graphic.color = _baseColor;
        SetScale(_normalScale);
    }

    public void SetVisualParams(float normalScale, float hoverScale, Color hoverColor, bool revertToOriginalColor)
    {
        _normalScale = Mathf.Max(0.0001f, normalScale);
        _hoverScale = Mathf.Max(_normalScale, hoverScale);
        _hoverColor = hoverColor;
        _revertToOriginalColor = revertToOriginalColor;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        SetScale(_hoverScale);
        if (_graphic != null && _hasBaseColor)
            _graphic.color = MultiplyColor(_baseColor, _hoverColor);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        SetScale(_normalScale);
        if (_graphic != null && _revertToOriginalColor && _hasBaseColor)
            _graphic.color = _baseColor;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData != null && eventData.button != PointerEventData.InputButton.Left) return;
        onClick?.Invoke();
    }

    private void SetScale(float s)
    {
        if (_rect != null) _rect.localScale = new Vector3(s, s, 1f);
        else transform.localScale = new Vector3(s, s, 1f);
    }

    private static Color MultiplyColor(Color baseColor, Color mul)
    {
        return new Color(baseColor.r * mul.r, baseColor.g * mul.g, baseColor.b * mul.b, baseColor.a * mul.a);
    }
}
