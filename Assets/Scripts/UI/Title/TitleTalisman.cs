using System;
using UnityEngine;

namespace CheonMyeong.UI.Title
{
    /// <summary>
    /// 타이틀 화면 문에 붙은 부적 하나. 평소엔 "온전한" 스프라이트(wholeRenderer) 한 장으로
    /// 보이다가, TitleDragCutController가 이 오브젝트의 hitArea 위에서 시작된 드래그를
    /// 대각선 방향으로 판정하면 그 방향에 맞는 조각 두 개로 교체되고 Rigidbody2D로
    /// 서로 반대 방향으로 튕겨나가며 떨어진다.
    ///
    /// 네트워크 동기화 없음 - 타이틀은 접속 전 로컬 화면이라 NetworkBehaviour가 아니다.
    ///
    /// 대각선 방향 이름(DiagonalTopLeftToBottomRight / DiagonalTopRightToBottomLeft)은
    /// 베는 선의 기울기를 기하학적으로 표현한 것이다. "왼쪽 대각선 베기/오른쪽 대각선 베기"라는
    /// 기획 표현이 이 중 어느 쪽을 가리키는지는 실제 플레이로 확인해야 해서(확인 못 함) 코드에선
    /// 모호하지 않은 이름을 쓴다 - 반대로 느껴지면 TitleDragCutController.ClassifyDirection()
    /// 한 곳만 뒤집으면 된다.
    ///
    /// 조각 스프라이트는 전부 placeholder(단색 사각형)다. 최종 아트는 "온전한 상태" 기준과
    /// 같은 픽셀 크기의 캔버스에, 실제로 보여야 할 조각 모양만 그리고 나머지는 투명으로
    /// 둔 PNG로 교체하면 된다 - 그래야 이 스크립트/에디터 셋업이 잡아둔 위치·피벗을
    /// 다시 안 맞춰도 된다.
    /// </summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public class TitleTalisman : MonoBehaviour
    {
        public enum CutDirection
        {
            DiagonalTopLeftToBottomRight,
            DiagonalTopRightToBottomLeft
        }

        [Header("온전한 상태")]
        [SerializeField] private SpriteRenderer wholeRenderer;
        [SerializeField] private Collider2D hitArea; // 드래그 판정용 - Trigger 권장
        [SerializeField] private TextMesh label; // 부적 위 세로쓰기 글자(시작/설정/종료, 261013 추가) - wholeRenderer와 생사를 같이함

        [Header("TopLeft -> BottomRight 대각선으로 잘렸을 때 조각 두 개")]
        [SerializeField] private GameObject diagonalTLBR_PieceA;
        [SerializeField] private GameObject diagonalTLBR_PieceB;

        [Header("TopRight -> BottomLeft 대각선으로 잘렸을 때 조각 두 개")]
        [SerializeField] private GameObject diagonalTRBL_PieceA;
        [SerializeField] private GameObject diagonalTRBL_PieceB;

        [Header("절단 연출 - 값은 미정, 느낌 보고 조정")]
        [SerializeField] private float pieceSeparationSpeed = 2.5f;
        [SerializeField] private float pieceAngularVelocity = 180f;

        [Header("호버 연출(261013 추가) - 커서를 올리면 색이 짙어짐. 수치는 미정, 느낌 보고 조정")]
        [SerializeField, Range(0.1f, 1f)] private float hoverDarkenFactor = 0.6f;

        /// <summary>버튼 로직(씬 전환 / 설정창 열기 / 종료 등)을 여기 연결한다.</summary>
        public event Action OnCutCompleted;

        public Collider2D HitArea => hitArea;
        public bool IsCut { get; private set; }

        private Color baseColor;
        private Color hoverColor;
        private bool isHovered;

        private void Awake()
        {
            SetPieceActive(diagonalTLBR_PieceA, false);
            SetPieceActive(diagonalTLBR_PieceB, false);
            SetPieceActive(diagonalTRBL_PieceA, false);
            SetPieceActive(diagonalTRBL_PieceB, false);

            if (wholeRenderer != null)
            {
                // 매 프레임 곱하지 않고 Awake에서 한 번만 계산해서 캐싱(호버 전환 시 대입만 함).
                baseColor = wholeRenderer.color;
                hoverColor = new Color(baseColor.r * hoverDarkenFactor, baseColor.g * hoverDarkenFactor,
                                        baseColor.b * hoverDarkenFactor, baseColor.a);
            }
        }

        /// <summary>
        /// TitleDragCutController가 포인터가 이 부적 위에 있는지 매 프레임 판정해서 호출한다.
        /// 상태가 실제로 바뀔 때만 색을 대입하도록 isHovered로 걸러서(TitleDragCutController도
        /// 같은 방식으로 호출 자체를 걸러주지만, 이 쪽에서도 한 번 더 방어) 불필요한 대입을 줄인다.
        /// </summary>
        public void SetHovered(bool hovered)
        {
            if (wholeRenderer == null || IsCut || isHovered == hovered)
            {
                return;
            }
            isHovered = hovered;
            wholeRenderer.color = hovered ? hoverColor : baseColor;
        }

        /// <summary>
        /// TitleDragCutController가 유효한 드래그(최소 길이 이상, 아직 안 잘린 부적)로
        /// 판정했을 때만 호출한다. dragDirection은 정규화된 드래그 방향(조각이 갈라지는
        /// 축을 계산하는 데 씀).
        /// </summary>
        public void Cut(CutDirection direction, Vector2 dragDirection)
        {
            if (IsCut)
            {
                return; // 이미 잘린 부적은 다시 자를 수 없음 - 중복 발동 방지
            }

            IsCut = true;

            if (wholeRenderer != null)
            {
                wholeRenderer.enabled = false;
            }

            if (hitArea != null)
            {
                hitArea.enabled = false; // 잘린 뒤엔 더 이상 드래그 판정 대상이 아님
            }

            if (label != null)
            {
                label.gameObject.SetActive(false); // 글자도 종잇조각과 함께 사라져야 함 - 안 그러면 허공에 글자만 남음
            }

            GameObject pieceA = direction == CutDirection.DiagonalTopLeftToBottomRight
                ? diagonalTLBR_PieceA
                : diagonalTRBL_PieceA;
            GameObject pieceB = direction == CutDirection.DiagonalTopLeftToBottomRight
                ? diagonalTLBR_PieceB
                : diagonalTRBL_PieceB;

            SetPieceActive(pieceA, true);
            SetPieceActive(pieceB, true);
            ApplyPiecePhysics(pieceA, dragDirection, 1f);
            ApplyPiecePhysics(pieceB, dragDirection, -1f);

            OnCutCompleted?.Invoke();
        }

        /// <summary>
        /// 잘린 부적을 다시 "온전한 상태"로 되돌린다(261013 추가) - Start/Exit는 한 번 자르면
        /// 타이틀 자체를 떠나거나(Lobby로) 앱이 꺼지거나(Exit) 해서 되돌릴 일이 없지만,
        /// Settings는 열었다가 닫고 다시 타이틀로 돌아올 수 있어서 또 벨 수 있어야 한다
        /// (TitleToLobbyBridge가 UIFlowManager.OnOverlayClosed를 받아서 호출).
        /// </summary>
        public void Reset()
        {
            if (!IsCut)
            {
                return;
            }

            IsCut = false;
            isHovered = false;

            if (wholeRenderer != null)
            {
                wholeRenderer.enabled = true;
                wholeRenderer.color = baseColor;
            }
            if (hitArea != null)
            {
                hitArea.enabled = true;
            }
            if (label != null)
            {
                label.gameObject.SetActive(true);
            }

            ResetPiece(diagonalTLBR_PieceA);
            ResetPiece(diagonalTLBR_PieceB);
            ResetPiece(diagonalTRBL_PieceA);
            ResetPiece(diagonalTRBL_PieceB);
        }

        private static void ResetPiece(GameObject piece)
        {
            if (piece == null)
            {
                return;
            }

            piece.SetActive(false);
            piece.transform.localPosition = Vector3.zero;
            piece.transform.localRotation = Quaternion.identity;
            if (piece.TryGetComponent(out Rigidbody2D rb))
            {
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
            }
        }

        private static void SetPieceActive(GameObject piece, bool active)
        {
            if (piece != null)
            {
                piece.SetActive(active);
            }
        }

        private void ApplyPiecePhysics(GameObject piece, Vector2 dragDirection, float sign)
        {
            if (piece == null)
            {
                return;
            }

            if (!piece.TryGetComponent(out Rigidbody2D rb))
            {
                Debug.LogWarning($"[CheonMyeong] '{piece.name}'에 Rigidbody2D가 없어서 절단 물리 연출을 건너뜁니다.");
                return;
            }

            // 드래그 방향에 수직인 축으로 두 조각을 서로 반대로 밀어낸다(sign으로 좌/우 반전).
            Vector2 perpendicular = new Vector2(-dragDirection.y, dragDirection.x) * sign;
            rb.linearVelocity = perpendicular * pieceSeparationSpeed;
            rb.angularVelocity = pieceAngularVelocity * sign;
        }
    }
}
