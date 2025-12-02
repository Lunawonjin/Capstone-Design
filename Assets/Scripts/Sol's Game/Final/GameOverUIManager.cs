using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class GameOverUIManager : MonoBehaviour
{
    [Header("UI 오브젝트")]
    [SerializeField] private GameObject deadUI;
    [SerializeField] private GameObject dialogueImage;
    [SerializeField] private RectTransform defeatTextRect;
    [SerializeField] private GameObject restartButton;

    [Header("설정")]
    [SerializeField] private float textMoveDuration = 0.5f;
    [SerializeField] private float targetY = 200f;

    private bool isDead = false;
    private bool hasClicked = false;
    private string lastKillerTag = ""; // 누가 죽였는지 저장

    private void Awake()
    {
        if (deadUI != null) deadUI.SetActive(false);
        if (restartButton != null) restartButton.SetActive(false);
    }

    private void Update()
    {
        if (isDead && !hasClicked && Input.GetMouseButtonDown(0))
        {
            StartCoroutine(HandleClickSequence());
        }
    }

    // [변경] 죽은 이유(태그)를 받아옴
    public void ShowDeadUI(string killerTag)
    {
        isDead = true;
        hasClicked = false;
        lastKillerTag = killerTag; // 태그 저장 ("Enemy" 또는 "Boss")

        if (deadUI != null) deadUI.SetActive(true);
        if (dialogueImage != null) dialogueImage.SetActive(true);
        if (restartButton != null) restartButton.SetActive(false);
    }

    private IEnumerator HandleClickSequence()
    {
        hasClicked = true;
        if (dialogueImage != null) dialogueImage.SetActive(false);

        if (defeatTextRect != null)
        {
            float timer = 0f;
            Vector2 startPos = defeatTextRect.anchoredPosition;
            Vector2 targetPos = new Vector2(startPos.x, targetY);

            while (timer < textMoveDuration)
            {
                timer += Time.deltaTime;
                float t = timer / textMoveDuration;
                t = t * t * (3f - 2f * t);
                defeatTextRect.anchoredPosition = Vector2.Lerp(startPos, targetPos, t);
                yield return null;
            }
            defeatTextRect.anchoredPosition = targetPos;
        }

        if (restartButton != null) restartButton.SetActive(true);
    }

    // [핵심 로직 변경] 재시작 버튼 클릭 시 분기 처리
    public void OnClickRestart()
    {
        // 1. 보스(Boss)한테 죽었으면 -> 저장된 위치 불러오기
        if (lastKillerTag == "Boss")
        {

                // 매니저가 없으면 그냥 재시작
               SceneManager.LoadScene(SceneManager.GetActiveScene().name);

        }
        // 2. 일반 적(Enemy)한테 죽었으면 -> 그냥 씬 재시작 (초기화)
        else
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
    }
}