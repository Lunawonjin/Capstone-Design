// PuzzleManager.cs (Unity 6 LTS)
// 퍼즐 데이터 관리 + UI 컨테이너(Image) 안에 퍼즐 조각 UI(Image)를 생성/배치.
// 같은 파일 안에 드래그/선택/회전/스냅 고정 기능(PuzzlePieceDrag)까지 전부 포함.
//
// 포함 기능:
// 1) 인스펙터에서 퍼즐 조각 개수(pieceCount, 기본 8)와 조각 스프라이트(pieceSprites) 설정
// 2) 컨테이너 UI(Image) 안에 조각 UI(Image)들을 랜덤 배치(겹치지 않게)
// 3) 마우스로 조각을 드래그 가능(1920x1080 영역 밖으로 못 나감)
// 4) 마지막으로 클릭/드래그한 조각 1개만 선택 상태 유지(백플레이트로 표시)
//    - 퍼즐 조각이 아닌 곳을 클릭하면(버튼/셀렉터블 제외) 선택 해제되어 백플레이트 꺼짐
//    - 고정된(locked) 조각은 클릭해도 선택되지 않으며 백플레이트가 나오지 않음
// 5) 생성 시 조각 Z 회전 0/90/180/270 랜덤
// 6) 회전 버튼 지원
//    - Clockwise_BT 클릭: 선택된 조각 -90도
//    - Counterclockwise_BT 클릭: 선택된 조각 +90도
// 7) 스냅/고정 기능(난이도 조절 포함)
//    - 인스펙터로 BoxCollider2D 슬롯들을 여러 개 받음(targetColliders)
//    - "조각 인덱스 == 슬롯 인덱스"일 때만 검사
//    - 조각 Z 회전이 0(허용 오차 내)이고
//    - 해당 슬롯(BoxCollider2D)과 겹치며
//    - 조각 중심이 슬롯 중심에 충분히 가까울 때만( snapMaxCenterDistanceLocal )
//    - (옵션) 조각 중심이 슬롯 안에 들어와 있어야 할 때( requirePieceCenterInsideTarget )
//    - 마우스가 눌려있지 않을 때 고정
// 8) Reset_BT 지원
//    - Reset_BT 클릭 시 모든 퍼즐 조각이 "처음 랜덤 생성된 위치/회전"으로 되돌아감
//    - 잠금/진행도/선택 상태도 전부 초기화
// 9) 모든 조각이 고정되면 Debug.Log로 완료 메시지 출력 + onPuzzleCompleted 호출
//
// 주의:
// - Canvas에 GraphicRaycaster, 씬에 EventSystem이 있어야 입력이 동작합니다.
// - 조각은 UI(Image), 슬롯은 BoxCollider2D(월드)로 가정합니다.
//   OnTrigger를 쓰지 않고 Bounds 교차로 겹침을 판정합니다.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public class PuzzleManager : MonoBehaviour
{
    public enum SpawnRegionMode
    {
        FixedSize,
        UseContainerRect
    }

    [Header("Puzzle Settings")]
    [SerializeField, Min(1)]
    private int pieceCount = 8;

    [Tooltip("퍼즐 조각 개수만큼 스프라이트를 넣어주세요.")]
    [SerializeField]
    private List<Sprite> pieceSprites = new List<Sprite>(8);

    [Header("UI Spawn Settings")]
    [Tooltip("퍼즐 조각들이 생성될 UI 컨테이너 Image.")]
    [SerializeField]
    private Image containerImage;

    [Tooltip("스폰 영역 모드. UseContainerRect면 컨테이너 전체에서 랜덤 배치됩니다.")]
    [SerializeField]
    private SpawnRegionMode spawnRegionMode = SpawnRegionMode.UseContainerRect;

    [Tooltip("FixedSize 모드일 때 사용하는 스폰 영역 크기.")]
    [SerializeField]
    private Vector2 fixedSpawnRegionSize = new Vector2(320f, 419f);

    [Tooltip("가운데 비워둘 퍼즐 보드 RectTransform. 비우지 않을 거면 비워두세요.")]
    [SerializeField]
    private RectTransform puzzleBoardRect;

    [Tooltip("퍼즐 보드 제외 영역에 주는 여유값(픽셀).")]
    [SerializeField, Min(0f)]
    private float boardPadding = 10f;

    [Tooltip("조각 UI의 기본 스케일.")]
    [SerializeField, Min(0.01f)]
    private float pieceScale = 1f;

    [Tooltip("스프라이트 원래 크기를 UI 크기로 반영할지 여부.")]
    [SerializeField]
    private bool useNativeSize = true;

    [Tooltip("겹침 계산 여유값(픽셀).")]
    [SerializeField, Min(0f)]
    private float overlapPadding = 2f;

    [Tooltip("조각 하나 배치할 때 최대 시도 횟수.")]
    [SerializeField, Min(1)]
    private int maxPlacementAttemptsPerPiece = 500;

    [Tooltip("전체 레이아웃 재시도 횟수.")]
    [SerializeField, Min(1)]
    private int maxLayoutRetries = 60;

    [Header("Auto Scale Down (optional)")]
    [Tooltip("배치가 계속 실패하면 스케일을 자동으로 줄일지.")]
    [SerializeField]
    private bool autoScaleDownIfFail = false;

    [Tooltip("자동 스케일 다운의 최소 스케일.")]
    [SerializeField, Min(0.01f)]
    private float minAutoScale = 0.6f;

    [Tooltip("재시도 한 바퀴 실패할 때마다 곱해지는 스케일 비율.")]
    [SerializeField, Range(0.5f, 0.99f)]
    private float autoScaleStep = 0.92f;

    [Header("Drag Clamp")]
    [Tooltip("드래그 가능 영역 크기(컨테이너 로컬 좌표 기준). 1920x1080 밖으로 못 나가게 제한합니다.")]
    [SerializeField]
    private Vector2 dragClampSize = new Vector2(1920f, 1080f);

    [Header("Selection Backplate")]
    [Tooltip("선택된 조각 뒤에 깔리는 하이라이트 색.")]
    [SerializeField]
    private Color selectedBackplateColor = new Color(1f, 1f, 1f, 0.25f);

    [Tooltip("선택 하이라이트가 조각보다 바깥으로 얼마나 더 나올지(픽셀).")]
    [SerializeField, Min(0f)]
    private float selectedBackplatePadding = 12f;

    [Header("Rotate Buttons")]
    [Tooltip("시계방향 회전 버튼(클릭 시 -90도).")]
    [SerializeField]
    private Button clockwiseButton;

    [Tooltip("반시계방향 회전 버튼(클릭 시 +90도).")]
    [SerializeField]
    private Button counterclockwiseButton;

    [Header("Reset Button")]
    [Tooltip("Reset_BT. 클릭 시 모든 퍼즐 조각을 처음 위치/회전으로 되돌립니다.")]
    [SerializeField]
    private Button resetButton;

    [Header("Snap Targets")]
    [Tooltip("퍼즐 슬롯(BoxCollider2D)들. 인덱스가 조각 인덱스와 1:1로 매칭됩니다.")]
    [SerializeField]
    private List<BoxCollider2D> targetColliders = new List<BoxCollider2D>();

    [Tooltip("Z 회전이 0이라고 보는 허용 오차(도).")]
    [SerializeField, Min(0f)]
    private float snapRotationTolerance = 3f;

    [Tooltip("조각 중심이 슬롯 중심에서 이 거리(로컬 px) 안에 들어와야 스냅됩니다. 값이 작을수록 어렵습니다.")]
    [SerializeField, Min(0f)]
    private float snapMaxCenterDistanceLocal = 25f;

    [Tooltip("true면 조각 중심이 슬롯 Bounds 안에 들어와 있어야만 스냅됩니다.")]
    [SerializeField]
    private bool requirePieceCenterInsideTarget = true;

    [Tooltip("스냅 시 슬롯 중심에서 더할 오프셋(월드). 필요 없으면 (0,0).")]
    [SerializeField]
    private Vector2 snapWorldOffset = Vector2.zero;

    [Header("Events")]
    [Tooltip("퍼즐이 전부 맞춰졌을 때 호출됩니다.")]
    [SerializeField]
    private UnityEvent onPuzzleCompleted;

    private bool[] placedFlags;
    private int placedCount;

    private readonly List<Image> spawnedPieceImages = new List<Image>();
    private readonly List<PuzzlePieceDrag> spawnedPieceDrags = new List<PuzzlePieceDrag>();
    private readonly List<Rect> placedRectsLocal = new List<Rect>();

    // 초기 랜덤 생성 상태 저장(Reset 용)
    private Vector2[] initialPositions;
    private float[] initialAngles;

    private PuzzlePieceDrag currentSelected;
    private bool completionLogged;

    public int PieceCount => pieceCount;
    public IReadOnlyList<Sprite> PieceSprites => pieceSprites;
    public PuzzlePieceDrag CurrentSelected => currentSelected;

    public Color GetSelectedBackplateColor() => selectedBackplateColor;
    public float GetSelectedBackplatePadding() => selectedBackplatePadding;

    private RectTransform ContainerRT => containerImage != null ? containerImage.rectTransform : null;

    private void Awake()
    {
        placedFlags = new bool[pieceCount];
        placedCount = 0;
        completionLogged = false;

        ResizeSpriteListToMatchCount();

        initialPositions = new Vector2[pieceCount];
        initialAngles = new float[pieceCount];
    }

    private void Start()
    {
        BindButtons();
        GeneratePiecesUI();
        RefreshRotateButtonsInteractable();
    }

    private void OnEnable()
    {
        BindButtons();
        RefreshRotateButtonsInteractable();
    }

    private void OnDisable()
    {
        UnbindButtons();
    }

    private void OnDestroy()
    {
        UnbindButtons();
    }

    private void Update()
    {
        RefreshRotateButtonsInteractable();
        HandleClickOutsideToDeselect();
    }

    // 퍼즐이 아닌 곳 클릭 시(버튼 제외) 선택 해제
    private void HandleClickOutsideToDeselect()
    {
        if (!Input.GetMouseButtonDown(0)) return;
        if (EventSystem.current == null) return;

        PointerEventData ped = new PointerEventData(EventSystem.current);
        ped.position = Input.mousePosition;

        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(ped, results);

        bool overSelectableButton = false;
        bool overPiece = false;

        for (int i = 0; i < results.Count; i++)
        {
            GameObject go = results[i].gameObject;
            if (go == null) continue;

            if (go.GetComponentInParent<PuzzlePieceDrag>() != null)
            {
                overPiece = true;
                break;
            }

            if (go.GetComponentInParent<Button>() != null || go.GetComponentInParent<Selectable>() != null)
            {
                overSelectableButton = true;
                break;
            }
        }

        if (!overPiece && !overSelectableButton)
        {
            DeselectCurrent();
        }
    }

    private void BindButtons()
    {
        if (clockwiseButton != null)
        {
            clockwiseButton.onClick.RemoveListener(OnClockwiseClicked);
            clockwiseButton.onClick.AddListener(OnClockwiseClicked);
        }

        if (counterclockwiseButton != null)
        {
            counterclockwiseButton.onClick.RemoveListener(OnCounterClockwiseClicked);
            counterclockwiseButton.onClick.AddListener(OnCounterClockwiseClicked);
        }

        if (resetButton != null)
        {
            resetButton.onClick.RemoveListener(OnResetClicked);
            resetButton.onClick.AddListener(OnResetClicked);
        }
    }

    private void UnbindButtons()
    {
        if (clockwiseButton != null)
            clockwiseButton.onClick.RemoveListener(OnClockwiseClicked);

        if (counterclockwiseButton != null)
            counterclockwiseButton.onClick.RemoveListener(OnCounterClockwiseClicked);

        if (resetButton != null)
            resetButton.onClick.RemoveListener(OnResetClicked);
    }

    private void RefreshRotateButtonsInteractable()
    {
        bool hasSelected = currentSelected != null && !currentSelected.IsLocked;

        if (clockwiseButton != null)
            clockwiseButton.interactable = hasSelected;

        if (counterclockwiseButton != null)
            counterclockwiseButton.interactable = hasSelected;
    }

    private void OnClockwiseClicked()
    {
        RotateSelectedPiece(-90);
    }

    private void OnCounterClockwiseClicked()
    {
        RotateSelectedPiece(90);
    }

    private void OnResetClicked()
    {
        ResetPiecesToInitial();
    }

    // Reset_BT 동작: 처음 랜덤 생성 상태로 되돌림
    public void ResetPiecesToInitial()
    {
        if (spawnedPieceDrags.Count == 0) return;

        DeselectCurrent();

        placedFlags = new bool[pieceCount];
        placedCount = 0;
        completionLogged = false;

        for (int i = 0; i < spawnedPieceDrags.Count; i++)
        {
            PuzzlePieceDrag piece = spawnedPieceDrags[i];
            if (piece == null) continue;

            RectTransform rt = piece.PieceRectTransform;
            if (rt == null) continue;

            piece.SetLocked(false);
            piece.ForceCancelDragAndSelection();

            float angle = (i < initialAngles.Length) ? initialAngles[i] : rt.localEulerAngles.z;
            Vector2 pos = (i < initialPositions.Length) ? initialPositions[i] : rt.anchoredPosition;

            rt.localEulerAngles = new Vector3(0f, 0f, angle);
            rt.anchoredPosition = pos;

            ClampPieceToDragArea(rt);
        }

        RefreshRotateButtonsInteractable();

        Debug.Log("PuzzleManager: Reset 완료. 모든 조각을 초기 위치로 되돌렸습니다.");
    }

    public void RotateSelectedPiece(int deltaAngle)
    {
        if (currentSelected == null) return;
        if (currentSelected.IsLocked) return;

        RectTransform rt = currentSelected.PieceRectTransform;
        if (rt == null) return;

        float z = rt.localEulerAngles.z;
        int snapped = Mathf.RoundToInt(z / 90f) * 90;
        snapped = ((snapped % 360) + 360) % 360;

        int newAngle = snapped + deltaAngle;
        newAngle = ((newAngle % 360) + 360) % 360;

        rt.localEulerAngles = new Vector3(0f, 0f, newAngle);

        ClampPieceToDragArea(rt);

        TrySnapAndLock(currentSelected);
    }

    public void ClampPieceToDragArea(RectTransform pieceRT)
    {
        if (pieceRT == null) return;

        Vector2 sizeForClamp = GetEffectiveScaledSizeConsideringRotation(pieceRT);
        Vector2 clamped = ClampPositionToDragArea(pieceRT.anchoredPosition, sizeForClamp);
        pieceRT.anchoredPosition = clamped;
    }

    private void OnValidate()
    {
        if (pieceCount < 1) pieceCount = 1;
        ResizeSpriteListToMatchCount();
    }

    private void ResizeSpriteListToMatchCount()
    {
        if (pieceSprites == null)
            pieceSprites = new List<Sprite>(pieceCount);

        while (pieceSprites.Count < pieceCount)
            pieceSprites.Add(null);

        while (pieceSprites.Count > pieceCount)
            pieceSprites.RemoveAt(pieceSprites.Count - 1);
    }

    public Sprite GetPieceSprite(int index)
    {
        if (index < 0 || index >= pieceCount) return null;
        if (pieceSprites == null || index >= pieceSprites.Count) return null;
        return pieceSprites[index];
    }

    // Selection API
    public void SelectPiece(PuzzlePieceDrag piece)
    {
        if (piece == null) return;
        if (piece.IsLocked) return;
        if (currentSelected == piece) return;

        if (currentSelected != null)
            currentSelected.SetSelected(false);

        currentSelected = piece;
        currentSelected.SetSelected(true);

        RefreshRotateButtonsInteractable();
    }

    public void DeselectCurrent()
    {
        if (currentSelected != null)
            currentSelected.SetSelected(false);

        currentSelected = null;

        RefreshRotateButtonsInteractable();
    }

    // UI 퍼즐 조각 생성/배치
    public void GeneratePiecesUI()
    {
        if (containerImage == null)
        {
            Debug.LogError("PuzzleManager: containerImage가 비어 있습니다.", this);
            return;
        }

        RectTransform containerRT = containerImage.rectTransform;

        ClearSpawnedPiecesUI();
        DeselectCurrent();

        initialPositions = new Vector2[pieceCount];
        initialAngles = new float[pieceCount];

        for (int i = 0; i < pieceCount; i++)
        {
            Sprite sprite = GetPieceSprite(i);
            Image pieceImg = CreatePieceUIImage(containerRT, i, sprite);

            if (useNativeSize && sprite != null)
                pieceImg.SetNativeSize();

            RectTransform pieceRT = pieceImg.rectTransform;

            int[] angles = { 0, 90, 180, 270 };
            int angle = angles[UnityEngine.Random.Range(0, angles.Length)];
            pieceRT.localEulerAngles = new Vector3(0f, 0f, angle);

            PuzzlePieceDrag drag = pieceImg.gameObject.AddComponent<PuzzlePieceDrag>();
            drag.Init(containerRT, dragClampSize, this, i);

            spawnedPieceImages.Add(pieceImg);
            spawnedPieceDrags.Add(drag);
        }

        ForceUILayoutUpdate();

        Vector2 spawnRegionSize = GetSpawnRegionSize(containerRT);
        Vector2 halfRegion = spawnRegionSize * 0.5f;

        float currentScale = pieceScale;
        bool success = false;

        while (true)
        {
            success = TryLayoutAllPiecesWithoutOverlap(currentScale, halfRegion, containerRT);

            if (success) break;

            if (!autoScaleDownIfFail || currentScale <= minAutoScale) break;

            currentScale *= autoScaleStep;
            currentScale = Mathf.Max(currentScale, minAutoScale);
        }

        if (!success)
        {
            Debug.LogError(
                "PuzzleManager: 스폰 영역에서 겹침 없이 배치할 수 없습니다. " +
                "조각이 너무 크거나, 제외 보드 영역이 너무 큰 상황입니다.",
                this
            );
        }

        for (int i = 0; i < spawnedPieceDrags.Count; i++)
        {
            PuzzlePieceDrag drag = spawnedPieceDrags[i];
            if (drag == null) continue;

            RectTransform rt = drag.PieceRectTransform;
            if (rt == null) continue;

            if (i < initialPositions.Length)
                initialPositions[i] = rt.anchoredPosition;

            if (i < initialAngles.Length)
                initialAngles[i] = rt.localEulerAngles.z;
        }
    }

    private Vector2 GetSpawnRegionSize(RectTransform containerRT)
    {
        if (spawnRegionMode == SpawnRegionMode.UseContainerRect)
            return containerRT.rect.size;

        return fixedSpawnRegionSize;
    }

    private void ForceUILayoutUpdate()
    {
        Canvas.ForceUpdateCanvases();
        for (int i = 0; i < spawnedPieceImages.Count; i++)
        {
            if (spawnedPieceImages[i] == null) continue;
            RectTransform rt = spawnedPieceImages[i].rectTransform;
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        }
        Canvas.ForceUpdateCanvases();
    }

    private bool TryLayoutAllPiecesWithoutOverlap(float scale, Vector2 halfRegion, RectTransform containerRT)
    {
        for (int retry = 0; retry < maxLayoutRetries; retry++)
        {
            placedRectsLocal.Clear();

            for (int i = 0; i < spawnedPieceImages.Count; i++)
            {
                if (spawnedPieceImages[i] == null) continue;
                spawnedPieceImages[i].rectTransform.localScale = Vector3.one * scale;
            }

            bool allPlaced = true;

            for (int i = 0; i < spawnedPieceImages.Count; i++)
            {
                Image pieceImg = spawnedPieceImages[i];
                if (pieceImg == null) continue;

                RectTransform pieceRT = pieceImg.rectTransform;

                bool placed = TryPlaceOne(pieceRT, halfRegion, containerRT, out Vector2 pos, out Rect rect);

                if (!placed)
                {
                    allPlaced = false;
                    break;
                }

                Vector2 sizeForClamp = GetEffectiveScaledSizeConsideringRotation(pieceRT);
                pos = ClampPositionToDragArea(pos, sizeForClamp);

                pieceRT.anchoredPosition = pos;
                placedRectsLocal.Add(rect);
            }

            if (allPlaced) return true;
        }

        return false;
    }

    private bool TryPlaceOne(RectTransform pieceRT, Vector2 halfRegion, RectTransform containerRT,
                             out Vector2 finalPos, out Rect finalRect)
    {
        Vector2 size = GetEffectiveScaledSizeConsideringRotation(pieceRT);
        size.x += overlapPadding * 2f;
        size.y += overlapPadding * 2f;

        Vector2 pos = Vector2.zero;
        Rect rect = new Rect();

        for (int attempt = 0; attempt < maxPlacementAttemptsPerPiece; attempt++)
        {
            pos.x = UnityEngine.Random.Range(-halfRegion.x, halfRegion.x);
            pos.y = UnityEngine.Random.Range(-halfRegion.y, halfRegion.y);

            rect = MakeLocalRect(pos, size);

            if (IsInsideBoardExclusion(rect, containerRT))
                continue;

            if (!IsOverlappingAny(rect))
            {
                finalPos = pos;
                finalRect = rect;
                return true;
            }
        }

        finalPos = Vector2.zero;
        finalRect = new Rect();
        return false;
    }

    private bool IsInsideBoardExclusion(Rect candidateLocal, RectTransform containerRT)
    {
        if (puzzleBoardRect == null) return false;

        Rect boardLocal = GetBoardRectInContainerLocal(containerRT);

        boardLocal.xMin -= boardPadding;
        boardLocal.xMax += boardPadding;
        boardLocal.yMin -= boardPadding;
        boardLocal.yMax += boardPadding;

        return candidateLocal.Overlaps(boardLocal);
    }

    private Rect GetBoardRectInContainerLocal(RectTransform containerRT)
    {
        Vector3[] boardWorldCorners = new Vector3[4];
        puzzleBoardRect.GetWorldCorners(boardWorldCorners);

        Vector2 minLocal, maxLocal;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            containerRT,
            RectTransformUtility.WorldToScreenPoint(null, boardWorldCorners[0]),
            null,
            out minLocal
        );
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            containerRT,
            RectTransformUtility.WorldToScreenPoint(null, boardWorldCorners[2]),
            null,
            out maxLocal
        );

        Vector2 size = maxLocal - minLocal;
        return new Rect(minLocal, size);
    }

    private Image CreatePieceUIImage(RectTransform parent, int index, Sprite sprite)
    {
        GameObject go = new GameObject($"Piece_{index}", typeof(RectTransform), typeof(Image));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);

        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);

        Image img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.preserveAspect = true;
        img.raycastTarget = true;

        return img;
    }

    // 회전을 고려한 실제 점유 크기 계산(겹침/클램프 용)
    public static Vector2 GetEffectiveScaledSizeConsideringRotation(RectTransform rt)
    {
        float w = rt.rect.width * rt.localScale.x;
        float h = rt.rect.height * rt.localScale.y;

        if (w <= 0f) w = 64f * rt.localScale.x;
        if (h <= 0f) h = 64f * rt.localScale.y;

        float z = rt.localEulerAngles.z;
        int snapped = Mathf.RoundToInt(z / 90f) * 90;
        snapped = ((snapped % 360) + 360) % 360;

        if (snapped == 90 || snapped == 270)
            return new Vector2(h, w);

        return new Vector2(w, h);
    }

    private Rect MakeLocalRect(Vector2 centerPos, Vector2 size)
    {
        Vector2 min = centerPos - size * 0.5f;
        return new Rect(min, size);
    }

    private bool IsOverlappingAny(Rect candidate)
    {
        for (int i = 0; i < placedRectsLocal.Count; i++)
        {
            if (candidate.Overlaps(placedRectsLocal[i]))
                return true;
        }
        return false;
    }

    private Vector2 ClampPositionToDragArea(Vector2 pos, Vector2 pieceSize)
    {
        Vector2 halfClamp = dragClampSize * 0.5f;

        float minX = -halfClamp.x + pieceSize.x * 0.5f;
        float maxX = halfClamp.x - pieceSize.x * 0.5f;
        float minY = -halfClamp.y + pieceSize.y * 0.5f;
        float maxY = halfClamp.y - pieceSize.y * 0.5f;

        if (minX > maxX) { minX = maxX = 0f; }
        if (minY > maxY) { minY = maxY = 0f; }

        pos.x = Mathf.Clamp(pos.x, minX, maxX);
        pos.y = Mathf.Clamp(pos.y, minY, maxY);

        return pos;
    }

    public void ClearSpawnedPiecesUI()
    {
        for (int i = spawnedPieceImages.Count - 1; i >= 0; i--)
        {
            if (spawnedPieceImages[i] != null)
                Destroy(spawnedPieceImages[i].gameObject);
        }
        spawnedPieceImages.Clear();
        spawnedPieceDrags.Clear();
        placedRectsLocal.Clear();

        placedFlags = new bool[pieceCount];
        placedCount = 0;
        completionLogged = false;
    }

    // 스냅/고정 로직(난이도 조건 추가)
    public bool TrySnapAndLock(PuzzlePieceDrag piece)
    {
        if (piece == null) return false;
        if (piece.IsLocked) return false;
        if (piece.IsDragging) return false;

        int index = piece.PieceIndex;
        if (index < 0) return false;
        if (targetColliders == null || index >= targetColliders.Count) return false;

        BoxCollider2D target = targetColliders[index];
        if (target == null) return false;

        RectTransform pieceRT = piece.PieceRectTransform;
        if (pieceRT == null) return false;

        RectTransform containerRT = ContainerRT;
        if (containerRT == null) return false;

        // 1) Z 회전 0 조건
        if (!IsRotationZero(pieceRT.localEulerAngles.z, snapRotationTolerance))
            return false;

        // 2) 슬롯과 겹침 조건
        if (!IsPieceOverlappingTarget(pieceRT, target, out Bounds pieceBounds))
            return false;

        // 3) (옵션) 조각 중심이 슬롯 안에 있어야 함
        Vector3 targetCenterWorld = target.bounds.center + (Vector3)snapWorldOffset;
        Vector3 pieceCenterWorld = pieceBounds.center;

        if (requirePieceCenterInsideTarget && !target.bounds.Contains(pieceCenterWorld))
            return false;

        // 4) 조각 중심이 슬롯 중심에 충분히 가까워야 함(로컬 거리)
        Vector2 targetCenterLocal = containerRT.InverseTransformPoint(targetCenterWorld);
        Vector2 pieceCenterLocal = containerRT.InverseTransformPoint(pieceCenterWorld);

        float centerDistLocal = Vector2.Distance(pieceCenterLocal, targetCenterLocal);
        if (centerDistLocal > snapMaxCenterDistanceLocal)
            return false;

        // 5) 스냅 이동
        pieceRT.anchoredPosition = targetCenterLocal;
        pieceRT.localEulerAngles = Vector3.zero;

        ClampPieceToDragArea(pieceRT);

        piece.SetLocked(true);

        if (currentSelected == piece)
            DeselectCurrent();

        RegisterPiecePlaced(index);

        return true;
    }

    private bool IsRotationZero(float zAngle, float toleranceDeg)
    {
        float a = NormalizeAngle360(zAngle);
        float distToZero = Mathf.Min(Mathf.Abs(a - 0f), Mathf.Abs(a - 360f));
        return distToZero <= toleranceDeg;
    }

    private float NormalizeAngle360(float a)
    {
        a %= 360f;
        if (a < 0f) a += 360f;
        return a;
    }

    private bool IsPieceOverlappingTarget(RectTransform pieceRT, BoxCollider2D target, out Bounds pieceBounds)
    {
        Vector3[] corners = new Vector3[4];
        pieceRT.GetWorldCorners(corners);

        pieceBounds = new Bounds(corners[0], Vector3.zero);
        pieceBounds.Encapsulate(corners[1]);
        pieceBounds.Encapsulate(corners[2]);
        pieceBounds.Encapsulate(corners[3]);

        return pieceBounds.Intersects(target.bounds);
    }

    // 퍼즐 진행도
    public void RegisterPiecePlaced(int index)
    {
        if (index < 0 || index >= pieceCount) return;
        if (placedFlags[index]) return;

        placedFlags[index] = true;
        placedCount++;

        if (placedCount >= pieceCount)
        {
            if (!completionLogged)
            {
                completionLogged = true;
                Debug.Log("PuzzleManager: 퍼즐이 모두 맞춰졌습니다.");
            }
            onPuzzleCompleted?.Invoke();
        }
    }

    public void RegisterPieceRemoved(int index)
    {
        if (index < 0 || index >= pieceCount) return;
        if (!placedFlags[index]) return;

        placedFlags[index] = false;
        placedCount = Mathf.Max(0, placedCount - 1);
    }
}

// ---------------------------------------------------------
// PuzzlePieceDrag (same file)
// ---------------------------------------------------------
// UI 퍼즐 조각 드래그 + 선택 처리.
// - 클릭하거나 드래그 시작하면 PuzzleManager에 선택 요청
// - locked면 선택도, 드래그도, 회전도 불가
// - 드래그 종료 시 스냅/고정 시도
// - Reset 시 강제로 dragging/selected 해제할 수 있는 API 제공

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(Image))]
public class PuzzlePieceDrag : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerDownHandler
{
    private RectTransform pieceRT;
    private RectTransform dragAreaRT;
    private Vector2 clampSize;
    private Vector2 dragOffsetLocal;
    private bool initialized;

    private PuzzleManager manager;

    private Image backplateImage;
    private RectTransform backplateRT;

    private bool selected;
    private bool locked;
    private bool dragging;

    private int pieceIndex;

    public bool IsSelected => selected;
    public bool IsLocked => locked;
    public bool IsDragging => dragging;

    public int PieceIndex => pieceIndex;
    public RectTransform PieceRectTransform => pieceRT;

    public void Init(RectTransform areaRT, Vector2 clampSizeLocal, PuzzleManager puzzleManager, int index)
    {
        pieceRT = GetComponent<RectTransform>();
        dragAreaRT = areaRT;
        clampSize = clampSizeLocal;
        manager = puzzleManager;
        pieceIndex = index;

        CreateBackplateIfNeeded();
        SyncBackplateTransform();
        MoveBackplateBehindPiece();

        initialized = (pieceRT != null && dragAreaRT != null);
        SetSelected(false);
        SetLocked(false);
        dragging = false;
    }

    // Reset에서 쓰는 강제 해제
    public void ForceCancelDragAndSelection()
    {
        dragging = false;
        SetSelected(false);
    }

    private void CreateBackplateIfNeeded()
    {
        if (backplateImage != null) return;
        if (dragAreaRT == null || pieceRT == null) return;

        GameObject bg = new GameObject("SelectionBackplate", typeof(RectTransform), typeof(Image));
        backplateRT = bg.GetComponent<RectTransform>();
        backplateRT.SetParent(dragAreaRT, false);

        backplateRT.anchorMin = pieceRT.anchorMin;
        backplateRT.anchorMax = pieceRT.anchorMax;
        backplateRT.pivot = pieceRT.pivot;

        backplateImage = bg.GetComponent<Image>();
        backplateImage.raycastTarget = false;
        backplateImage.color = manager != null ? manager.GetSelectedBackplateColor() : new Color(1f, 1f, 1f, 0.25f);
        backplateImage.enabled = false;
    }

    private void LateUpdate()
    {
        if (!initialized) return;
        if (backplateRT == null) return;

        SyncBackplateTransform();

        if (selected)
            MoveBackplateBehindPiece();
    }

    private void SyncBackplateTransform()
    {
        if (backplateRT == null || pieceRT == null) return;

        float pad = manager != null ? manager.GetSelectedBackplatePadding() : 12f;

        backplateRT.anchoredPosition = pieceRT.anchoredPosition;
        backplateRT.localScale = pieceRT.localScale;
        backplateRT.rotation = pieceRT.rotation;

        Vector2 baseSize = pieceRT.rect.size;
        backplateRT.sizeDelta = baseSize + new Vector2(pad * 2f, pad * 2f);
    }

    private void MoveBackplateBehindPiece()
    {
        if (backplateRT == null || pieceRT == null) return;
        if (backplateRT.parent != pieceRT.parent) return;

        int pieceSibling = pieceRT.GetSiblingIndex();
        int backSibling = backplateRT.GetSiblingIndex();

        int desired = pieceSibling;
        if (backSibling < pieceSibling)
            desired = pieceSibling - 1;

        desired = Mathf.Clamp(desired, 0, backplateRT.parent.childCount - 1);
        backplateRT.SetSiblingIndex(desired);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!initialized) return;

        if (locked)
        {
            manager?.DeselectCurrent();
            return;
        }

        manager?.SelectPiece(this);

        pieceRT.SetAsLastSibling();
        MoveBackplateBehindPiece();
        pieceRT.SetAsLastSibling();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!initialized) return;
        if (locked) return;

        dragging = true;

        manager?.SelectPiece(this);

        pieceRT.SetAsLastSibling();
        MoveBackplateBehindPiece();
        pieceRT.SetAsLastSibling();

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                dragAreaRT, eventData.position, eventData.pressEventCamera, out Vector2 localPoint))
        {
            dragOffsetLocal = localPoint - pieceRT.anchoredPosition;
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!initialized) return;
        if (locked) return;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                dragAreaRT, eventData.position, eventData.pressEventCamera, out Vector2 localPoint))
            return;

        Vector2 targetPos = localPoint - dragOffsetLocal;

        Vector2 pieceSize = PuzzleManager.GetEffectiveScaledSizeConsideringRotation(pieceRT);
        Vector2 halfClamp = clampSize * 0.5f;

        float minX = -halfClamp.x + pieceSize.x * 0.5f;
        float maxX = halfClamp.x - pieceSize.x * 0.5f;
        float minY = -halfClamp.y + pieceSize.y * 0.5f;
        float maxY = halfClamp.y - pieceSize.y * 0.5f;

        if (minX > maxX) { minX = maxX = 0f; }
        if (minY > maxY) { minY = maxY = 0f; }

        targetPos.x = Mathf.Clamp(targetPos.x, minX, maxX);
        targetPos.y = Mathf.Clamp(targetPos.y, minY, maxY);

        pieceRT.anchoredPosition = targetPos;

        SyncBackplateTransform();
        if (selected)
            MoveBackplateBehindPiece();
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!initialized) return;
        if (locked) return;

        dragging = false;

        manager?.TrySnapAndLock(this);
    }

    public void SetSelected(bool on)
    {
        if (locked) on = false;

        selected = on;

        if (backplateImage == null)
            CreateBackplateIfNeeded();

        if (backplateImage != null)
            backplateImage.enabled = on;

        if (on)
            MoveBackplateBehindPiece();
    }

    public void SetLocked(bool on)
    {
        locked = on;

        if (on)
        {
            dragging = false;
            SetSelected(false);
        }
        else
        {
            SetSelected(false);
        }
    }

    private void OnDestroy()
    {
        if (backplateRT != null)
            Destroy(backplateRT.gameObject);
    }
}
