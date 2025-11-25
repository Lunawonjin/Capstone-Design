// PuzzleManager.cs (Unity 6 LTS)
// 퍼즐 데이터 관리 + UI 컨테이너(Image) 안에 퍼즐 조각 UI(Image)를 생성/배치.
// 같은 파일 안에 드래그/선택/회전/스냅 고정 기능(PuzzlePieceDrag)까지 전부 포함.
//
// 핵심 규칙:
//  - pieceSprites[i]   : i번째 퍼즐 조각 스프라이트
//  - targetColliders[i]: i번째 퍼즐 슬롯(BoxCollider2D)
//  => "인덱스가 같은 것끼리"만 자기 자리로 취급. 코드에서 순서를 자동으로 바꾸지 않는다.
//
// 기능 요약:
// 1) 인스펙터에서 퍼즐 조각 개수(pieceCount)와 스프라이트 리스트(pieceSprites) 설정
// 2) 컨테이너 UI(Image) 안에 조각 UI(Image)를 겹치지 않게 랜덤 배치
// 3) 마우스로 조각 드래그(1920x1080 영역 밖 이동 불가)
// 4) 마지막으로 클릭/드래그한 조각만 선택(백플레이트 표시)
//    - 퍼즐이 아닌 빈 곳 클릭(버튼/Selectable 제외) 시 선택 해제
//    - locked 조각은 선택/백플레이트 안 켜짐
// 5) 생성 시 각 조각의 Z 회전 0/90/180/270 랜덤
// 6) 회전 버튼
//    - Clockwise_BT : 선택 조각 -90도
//    - Counter_BT   : 선택 조각 +90도
// 7) 스냅/고정
//    - index 같은 BoxCollider2D와 겹치고, 회전이 0 근처이고,
//      중심 거리가 snapMaxCenterDistanceLocal 이하일 때만 스냅 + 잠금
//      스냅 기준 위치는 BoxCollider2D의 transform.position + snapWorldOffset
// 8) Reset_BT
//    - 시작 시 상태(랜덤 배치 + 프리솔브 상태)를 그대로 복원
//    - 퍼즐 완성 상태에서는 Reset 비활성화
// 9) 시작할 때 10~13개의 조각을 자기 슬롯(targetColliders[i])에 미리 붙여서 잠금(회전 0)
// 10) logTargetIndexMapping 켜면 인덱스/스프라이트/콜라이더 매핑 로그 출력
// 11) 맞춰서 고정된 퍼즐 조각은 항상 레이어를 가장 뒤로 보냄
//     - 나중에 맞추는 조각들도 자동으로 뒤로 내려가서,
//       아직 안 맞춘 조각들이 앞에서 잘 보이도록 함.

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
    private int pieceCount = 25;

    [Tooltip("퍼즐 조각 개수만큼 스프라이트를 넣어주세요. index=슬롯 index와 매칭됩니다.")]
    [SerializeField]
    private List<Sprite> pieceSprites = new List<Sprite>(25);

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
    [SerializeField] private Button clockwiseButton;
    [SerializeField] private Button counterclockwiseButton;

    [Header("Reset Button")]
    [SerializeField] private Button resetButton;

    [Header("Snap Targets (index 매칭)")]
    [Tooltip("퍼즐 슬롯(BoxCollider2D)들. index가 조각 index와 그대로 매칭됩니다.")]
    [SerializeField]
    private List<BoxCollider2D> targetColliders = new List<BoxCollider2D>();

    [Tooltip("Z 회전이 0이라고 보는 허용 오차(도).")]
    [SerializeField, Min(0f)]
    private float snapRotationTolerance = 3f;

    [Tooltip("조각 중심이 슬롯 기준 위치에서 이 거리(로컬 px) 안에 들어와야 스냅됩니다. 값이 작을수록 어렵습니다.")]
    [SerializeField, Min(0f)]
    private float snapMaxCenterDistanceLocal = 25f;

    [Tooltip("true면 조각 중심이 슬롯 Bounds 안에 들어와 있어야만 스냅됩니다.")]
    [SerializeField]
    private bool requirePieceCenterInsideTarget = true;

    [Tooltip("스냅 시 사용할 기준 위치 = target.transform.position + snapWorldOffset.")]
    [SerializeField]
    private Vector2 snapWorldOffset = Vector2.zero;

    [Header("Debug Logs")]
    [Tooltip("index별 sprite/slot/targetPos 매핑 로그 출력.")]
    [SerializeField]
    private bool logTargetIndexMapping = true;

    [Tooltip("초기 조각 상태(위치/각도/잠금 여부)를 로그로 출력.")]
    [SerializeField]
    private bool logPieceInitialState = false;

    [Header("Events")]
    [Tooltip("퍼즐이 전부 맞춰졌을 때 호출됩니다.")]
    [SerializeField]
    private UnityEvent onPuzzleCompleted;

    private bool[] placedFlags;
    private int placedCount;

    private readonly List<Image> spawnedPieceImages = new List<Image>();
    private readonly List<PuzzlePieceDrag> spawnedPieceDrags = new List<PuzzlePieceDrag>();
    private readonly List<Rect> placedRectsLocal = new List<Rect>();

    private Vector2[] initialPositions;
    private float[] initialAngles;
    private bool[] initialLockedFlags;

    private PuzzlePieceDrag currentSelected;
    private bool completionLogged;
    private bool puzzleCompleted;

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
        puzzleCompleted = false;

        ResizeSpriteListToMatchCount();

        initialPositions = new Vector2[pieceCount];
        initialAngles = new float[pieceCount];
        initialLockedFlags = new bool[pieceCount];

        SetResetButtonInteractable(true);
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

    private void SetResetButtonInteractable(bool on)
    {
        if (resetButton != null)
            resetButton.interactable = on;
    }

    private void MarkPuzzleCompleted()
    {
        puzzleCompleted = true;
        SetResetButtonInteractable(false);
    }

    private void ClearPuzzleCompleted()
    {
        puzzleCompleted = false;
        SetResetButtonInteractable(true);
    }

    // 맞춰진 퍼즐 조각을 레이어 가장 뒤로 보내는 함수
    private void SendLockedPieceToBack(PuzzlePieceDrag piece)
    {
        if (piece == null) return;
        RectTransform rt = piece.PieceRectTransform;
        if (rt == null) return;
        Transform parent = rt.parent;
        if (parent == null) return;

        // 컨테이너 안에서 가장 첫 번째(가장 뒤)로 보냄
        rt.SetSiblingIndex(0);
    }

    // 빈 곳 클릭 시 선택 해제
    private void HandleClickOutsideToDeselect()
    {
        if (!Input.GetMouseButtonDown(0)) return;
        if (EventSystem.current == null) return;

        PointerEventData ped = new PointerEventData(EventSystem.current)
        {
            position = Input.mousePosition
        };

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
        if (puzzleCompleted) return;
        ResetPiecesToInitial();
    }

    public void ResetPiecesToInitial()
    {
        if (spawnedPieceDrags.Count == 0) return;

        DeselectCurrent();

        completionLogged = false;
        ClearPuzzleCompleted();

        placedFlags = new bool[pieceCount];
        placedCount = 0;

        for (int i = 0; i < spawnedPieceDrags.Count; i++)
        {
            PuzzlePieceDrag piece = spawnedPieceDrags[i];
            if (piece == null) continue;

            RectTransform rt = piece.PieceRectTransform;
            if (rt == null) continue;

            bool initiallyLocked = (initialLockedFlags != null &&
                                    i < initialLockedFlags.Length &&
                                    initialLockedFlags[i]);

            piece.SetLocked(initiallyLocked);
            piece.ForceCancelDragAndSelection();

            float angle = (i < initialAngles.Length) ? initialAngles[i] : rt.localEulerAngles.z;
            Vector2 pos = (i < initialPositions.Length) ? initialPositions[i] : rt.anchoredPosition;

            rt.localEulerAngles = new Vector3(0f, 0f, angle);
            rt.anchoredPosition = pos;

            ClampPieceToDragArea(rt);

            if (initiallyLocked)
            {
                placedFlags[i] = true;
                placedCount++;
                // 초기부터 맞춰져 있던 조각은 리셋 후에도 뒤로 보냄
                SendLockedPieceToBack(piece);
            }
        }

        RefreshRotateButtonsInteractable();

        Debug.Log("PuzzleManager: Reset 완료. 모든 조각을 초기 상태로 되돌렸습니다.");
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

        placedFlags = new bool[pieceCount];
        placedCount = 0;
        completionLogged = false;
        ClearPuzzleCompleted();

        initialPositions = new Vector2[pieceCount];
        initialAngles = new float[pieceCount];
        initialLockedFlags = new bool[pieceCount];

        LogIndexMappingIfNeeded();

        // 1) 조각 생성
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

        // 2) 랜덤 배치(겹치지 않게)
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

        // 3) 일부 조각을 제자리로 스냅 + 잠금(회전 0)
        PreSolveSomePieces();

        // 4) 최종 상태(일부는 이미 잠금) 기준으로 초기값 기록
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

            if (logPieceInitialState)
            {
                string spriteName = (i < pieceSprites.Count && pieceSprites[i] != null)
                    ? pieceSprites[i].name
                    : "(null sprite)";
                Debug.Log($"PuzzleManager: 초기 조각 index={i}, sprite={spriteName}, " +
                          $"locked={drag.IsLocked}, posLocal={rt.anchoredPosition}, angleZ={rt.localEulerAngles.z}");
            }
        }
    }

    // index별 sprite/slot/targetPos 매핑 로그
    private void LogIndexMappingIfNeeded()
    {
        if (!logTargetIndexMapping) return;

        int max = Mathf.Max(pieceCount, targetColliders != null ? targetColliders.Count : 0);
        Debug.Log($"PuzzleManager: 인덱스 매핑 로그 (pieceCount={pieceCount}, targetCount={targetColliders.Count})");

        for (int i = 0; i < max; i++)
        {
            string spriteName = (i < pieceSprites.Count && pieceSprites[i] != null)
                ? pieceSprites[i].name
                : "(null sprite)";

            string colliderName = "(no collider)";
            string colliderPos = "";
            if (targetColliders != null && i < targetColliders.Count && targetColliders[i] != null)
            {
                colliderName = targetColliders[i].name;
                Vector3 c = targetColliders[i].transform.position;
                colliderPos = $" targetPos=({c.x:F2}, {c.y:F2})";
            }

            Debug.Log($"  index={i}, sprite={spriteName}, collider={colliderName}{colliderPos}");
        }
    }

    // 시작 시 랜덤으로 10~13개의 조각을 자기 슬롯에 스냅 + 잠금(회전 0)
    private void PreSolveSomePieces()
    {
        if (targetColliders == null || targetColliders.Count == 0)
            return;

        int upperBound = Mathf.Min(pieceCount, targetColliders.Count, spawnedPieceDrags.Count);
        if (upperBound <= 0)
            return;

        int minPreSolve = 10;
        int maxPreSolve = 13;

        int maxPossible = Mathf.Min(upperBound, maxPreSolve);
        if (maxPossible <= 0)
            return;

        int desiredCount;
        if (maxPossible <= minPreSolve)
            desiredCount = maxPossible;
        else
            desiredCount = UnityEngine.Random.Range(minPreSolve, maxPossible + 1);

        List<int> pool = new List<int>(upperBound);
        for (int i = 0; i < upperBound; i++)
            pool.Add(i);

        Debug.Log($"PuzzleManager: 시작 시 미리 맞춰 둘 조각 개수={desiredCount} (전체={upperBound})");

        for (int n = 0; n < desiredCount && pool.Count > 0; n++)
        {
            int r = UnityEngine.Random.Range(0, pool.Count);
            int index = pool[r];
            pool.RemoveAt(r);

            ForcePlaceAndLockAtTarget(index);

            if (initialLockedFlags != null && index < initialLockedFlags.Length)
                initialLockedFlags[index] = true;
        }
    }

    // index 조각을 index 슬롯 위치에 바로 스냅 + 잠금(회전 0)
    private void ForcePlaceAndLockAtTarget(int index)
    {
        if (index < 0 || index >= spawnedPieceDrags.Count) return;
        if (targetColliders == null || index >= targetColliders.Count) return;

        PuzzlePieceDrag piece = spawnedPieceDrags[index];
        BoxCollider2D target = targetColliders[index];
        if (piece == null || target == null) return;

        RectTransform containerRT = ContainerRT;
        RectTransform pieceRT = piece.PieceRectTransform;
        if (containerRT == null || pieceRT == null) return;

        Vector3 targetWorldPos = target.transform.position + (Vector3)snapWorldOffset;
        Vector2 targetLocalPos = containerRT.InverseTransformPoint(targetWorldPos);

        pieceRT.localRotation = Quaternion.identity;
        pieceRT.localEulerAngles = Vector3.zero;
        pieceRT.anchoredPosition = targetLocalPos;

        ClampPieceToDragArea(pieceRT);

        piece.SetLocked(true);
        // 프리솔브된 조각도 레이어를 가장 뒤로 보냄
        SendLockedPieceToBack(piece);

        RegisterPiecePlaced(index);

        if (initialPositions != null && index < initialPositions.Length)
            initialPositions[index] = pieceRT.anchoredPosition;
        if (initialAngles != null && index < initialAngles.Length)
            initialAngles[index] = 0f;

        string spriteName = (index < pieceSprites.Count && pieceSprites[index] != null)
            ? pieceSprites[index].name
            : "(null sprite)";

        Debug.Log($"PuzzleManager: 프리솔브 index={index}, sprite={spriteName}, target={target.name}");
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
        ClearPuzzleCompleted();
    }

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

        if (!IsRotationZero(pieceRT.localEulerAngles.z, snapRotationTolerance))
            return false;

        if (!IsPieceOverlappingTarget(pieceRT, target, out Bounds pieceBounds))
            return false;

        Vector3 targetWorldPos = target.transform.position + (Vector3)snapWorldOffset;
        Vector3 pieceCenterWorld = pieceBounds.center;

        if (requirePieceCenterInsideTarget && !target.bounds.Contains(pieceCenterWorld))
            return false;

        Vector2 targetCenterLocal = containerRT.InverseTransformPoint(targetWorldPos);
        Vector2 pieceCenterLocal = containerRT.InverseTransformPoint(pieceCenterWorld);

        float centerDistLocal = Vector2.Distance(pieceCenterLocal, targetCenterLocal);
        if (centerDistLocal > snapMaxCenterDistanceLocal)
            return false;

        pieceRT.anchoredPosition = targetCenterLocal;
        pieceRT.localEulerAngles = Vector3.zero;

        ClampPieceToDragArea(pieceRT);

        piece.SetLocked(true);
        // 유저가 맞춰서 고정시킨 조각도 레이어를 가장 뒤로 보냄
        SendLockedPieceToBack(piece);

        if (currentSelected == piece)
            DeselectCurrent();

        RegisterPiecePlaced(index);

        string spriteName = (index < pieceSprites.Count && pieceSprites[index] != null)
            ? pieceSprites[index].name
            : "(null sprite)";

        Debug.Log($"PuzzleManager: 스냅 성공 index={index}, sprite={spriteName}, target={target.name}");

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

    public void RegisterPiecePlaced(int index)
    {
        if (index < 0 || index >= pieceCount) return;
        if (placedFlags[index]) return;

        placedFlags[index] = true;
        placedCount++;

        Debug.Log($"PuzzleManager: 조각 고정 index={index}, 현재 고정 개수={placedCount}/{pieceCount}");

        if (placedCount >= pieceCount)
        {
            if (!completionLogged)
            {
                completionLogged = true;
                Debug.Log("PuzzleManager: 퍼즐이 모두 맞춰졌습니다.");
            }

            MarkPuzzleCompleted();
            onPuzzleCompleted?.Invoke();
        }
    }

    public void RegisterPieceRemoved(int index)
    {
        if (index < 0 || index >= pieceCount) return;
        if (!placedFlags[index]) return;

        placedFlags[index] = false;
        placedCount = Mathf.Max(0, placedCount - 1);

        Debug.Log($"PuzzleManager: 조각 해제 index={index}, 현재 고정 개수={placedCount}/{pieceCount}");

        if (placedCount < pieceCount && puzzleCompleted)
        {
            ClearPuzzleCompleted();
        }
    }
}

// ---------------------------------------------------------
// PuzzlePieceDrag (same file)
// ---------------------------------------------------------

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
