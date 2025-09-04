using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 색–태그–아이디 조합(spec)을 보관하고, 호출 시 조각들에 랜덤 적용.
/// - autoAssignOnStart=false로 두면 시작 때는 아무 변화 없음(잠금 상태 유지).
/// - AssignRandomSpecs()를 외부에서 호출했을 때만 실제로 색/태그/ID가 적용됨.
/// </summary>
public class PieceRandomizer : MonoBehaviour
{
    [Serializable]
    public class PieceSpec
    {
        public string tagName;     // Tag Manager에 미리 등록 필요
        public string uniqueId;    // 매칭용 고유 ID
        public Color color;        // 표시 색
    }

    [Header("옵션")]
    [Tooltip("Start()에서 자동으로 랜덤 할당을 수행할지 여부")]
    public bool autoAssignOnStart = false;

    [Header("랜덤 풀(예: 6개)")]
    public List<PieceSpec> specPool = new List<PieceSpec>();

    [Header("드래그 조각들(위 줄)")]
    public List<MouseDragSpringCheck> sourcePieces = new List<MouseDragSpringCheck>();

    [Header("목표 조각들(아래 줄)")]
    public List<MouseDragSpringCheck> targetPieces = new List<MouseDragSpringCheck>();

    [Header("목표 조각 시각 옵션")]
    public bool makeTargetsSemiTransparent = true;
    [Range(0f, 1f)] public float targetInitialAlpha = 0.5f;

    private void Start()
    {
        if (autoAssignOnStart)
        {
            AssignRandomSpecs();
        }
        else
        {
            Debug.Log("[랜덤할당] autoAssignOnStart=false → 시작 시 색/태그/ID 적용 안 함(대기).");
        }
    }

    /// <summary>
    /// 같은 풀을 두 번 섞어서 source/target 각각에 적용.
    /// </summary>
    public void AssignRandomSpecs()
    {
        if (specPool == null || specPool.Count == 0)
        {
            Debug.LogWarning("[랜덤할당] specPool이 비어 있습니다.");
            return;
        }

        var poolA = new List<PieceSpec>(specPool);
        var poolB = new List<PieceSpec>(specPool);
        Shuffle(poolA);
        Shuffle(poolB);

        for (int i = 0; i < sourcePieces.Count && i < poolA.Count; i++)
            ApplySpecToPiece(sourcePieces[i], poolA[i], isTarget: false);

        for (int i = 0; i < targetPieces.Count && i < poolB.Count; i++)
            ApplySpecToPiece(targetPieces[i], poolB[i], isTarget: true);

        Debug.Log("[랜덤할당] 드래그/목표 조각에 랜덤 적용 완료");
    }

    private void ApplySpecToPiece(MouseDragSpringCheck piece, PieceSpec spec, bool isTarget)
    {
        if (piece == null || spec == null) return;

        // 태그 적용(태그 미등록 시 에러가 뜰 수 있음)
        if (!string.IsNullOrEmpty(spec.tagName)) piece.gameObject.tag = spec.tagName;

        // uniqueId 적용
        piece.uniqueId = spec.uniqueId;

        // 색 적용(+ 목표면 초기 반투명)
        var sr = piece.GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            Color c = spec.color;
            if (isTarget && makeTargetsSemiTransparent) c.a = targetInitialAlpha;
            sr.color = c;
        }
    }

    private void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
