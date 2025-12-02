using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public class FramePuzzleInteractor : MonoBehaviour
{
    [Header("플레이어 설정")]
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private KeyCode downKey = KeyCode.F;
    [SerializeField] private KeyCode upKey = KeyCode.Escape;

    [Header("퍼즐 이미지(UI)")]
    [Tooltip("위아래로 움직일 퍼즐 이미지(UI Image)")]
    [SerializeField] private Image puzzleImage;

    [Tooltip("애니메이션 시작 Y 값 (예: 1000, 위쪽)")]
    [SerializeField] private float topY = 1000f;

    [Tooltip("애니메이션 도착 Y 값 (예: 0, 화면 안쪽)")]
    [SerializeField] private float bottomY = 0f;

    [Tooltip("Y 값 이동 시간(초)")]
    [SerializeField] private float moveDuration = 0.8f;

    private RectTransform puzzleRect;

    // 플레이어와 부딪혔는지
    private bool isPlayerColliding = false;

    // 퍼즐 상태
    private enum PuzzleState
    {
        Top,        // 위에 고정
        MovingDown, // 내려오는 중
        Bottom,     // 아래에 고정
        MovingUp    // 올라가는 중
    }

    private PuzzleState state = PuzzleState.Top;

    private void Awake()
    {
        if (puzzleImage != null)
        {
            puzzleRect = puzzleImage.GetComponent<RectTransform>();

            if (puzzleRect != null)
            {
                // 시작 위치를 항상 위쪽(topY)으로 설정
                Vector2 pos = puzzleRect.anchoredPosition;
                pos.y = topY;
                puzzleRect.anchoredPosition = pos;
            }
            else
            {
                Debug.LogError("[FramePuzzleInteractor] 퍼즐 Image에 RectTransform이 없습니다.");
            }
        }
        else
        {
            Debug.LogWarning("[FramePuzzleInteractor] 퍼즐 Image가 인스펙터에 연결되지 않았습니다.");
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.collider.CompareTag(playerTag))
        {
            isPlayerColliding = true;
        }
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        if (collision.collider.CompareTag(playerTag))
        {
            isPlayerColliding = false;
        }
    }

    private void Update()
    {
        // 1) Esc로 올리는 입력은 플레이어 충돌 여부와 상관 없이 처리
        //    단, 이동 중일 때는 한 번만 처리되도록 상태로 제한
        if (Input.GetKeyDown(upKey))
        {
            // 퍼즐이 아래에 있을 때만 위로 올리기
            if (state == PuzzleState.Bottom)
            {
                StartMoveUp();
                return;
            }
        }

        // 2) F로 내리는 입력은 액자와 부딪힌 상태에서만 처리
        if (!isPlayerColliding)
            return;

        // 이동 중일 때는 아무 입력도 받지 않음 (연타 방지)
        if (state == PuzzleState.MovingDown || state == PuzzleState.MovingUp)
            return;

        // F: 위에 있을 때만 내려오기 시작
        if (Input.GetKeyDown(downKey) && state == PuzzleState.Top)
        {
            StartMoveDown();
            return;
        }
    }

    private void StartMoveDown()
    {
        if (puzzleRect == null)
        {
            if (puzzleImage != null)
                puzzleRect = puzzleImage.GetComponent<RectTransform>();

            if (puzzleRect == null)
            {
                Debug.LogError("[FramePuzzleInteractor] 퍼즐 RectTransform을 찾을 수 없습니다.");
                return;
            }
        }

        if (!puzzleImage.gameObject.activeSelf)
        {
            puzzleImage.gameObject.SetActive(true);
        }

        // 위쪽 위치에서 시작
        Vector2 pos = puzzleRect.anchoredPosition;
        pos.y = topY;
        puzzleRect.anchoredPosition = pos;

        StopAllCoroutines();
        StartCoroutine(Co_MovePuzzle(topY, bottomY, PuzzleState.MovingDown, PuzzleState.Bottom));
    }

    private void StartMoveUp()
    {
        if (puzzleRect == null)
        {
            if (puzzleImage != null)
                puzzleRect = puzzleImage.GetComponent<RectTransform>();

            if (puzzleRect == null)
            {
                Debug.LogError("[FramePuzzleInteractor] 퍼즐 RectTransform을 찾을 수 없습니다.");
                return;
            }
        }

        // 아래쪽 위치에서 시작
        Vector2 pos = puzzleRect.anchoredPosition;
        pos.y = bottomY;
        puzzleRect.anchoredPosition = pos;

        StopAllCoroutines();
        StartCoroutine(Co_MovePuzzle(bottomY, topY, PuzzleState.MovingUp, PuzzleState.Top));
    }

    private IEnumerator Co_MovePuzzle(float fromY, float toY, PuzzleState movingState, PuzzleState endState)
    {
        state = movingState;

        float elapsed = 0f;
        Vector2 startPos = puzzleRect.anchoredPosition;
        startPos.y = fromY;
        Vector2 endPos = new Vector2(startPos.x, toY);

        while (elapsed < moveDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / moveDuration);
            float easedT = EaseOutCubic(t);

            puzzleRect.anchoredPosition = Vector2.Lerp(startPos, endPos, easedT);

            yield return null;
        }

        puzzleRect.anchoredPosition = endPos;
        state = endState;
    }

    // 부드러운 감속 이징 함수
    private float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        t = t - 1f;
        return t * t * t + 1f;
    }
}
