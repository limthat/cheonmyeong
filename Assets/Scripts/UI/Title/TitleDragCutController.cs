using UnityEngine;
using UnityEngine.InputSystem;

namespace CheonMyeong.UI.Title
{
    /// <summary>
    /// 타이틀 화면에 하나만 둔다. 포인터(마우스/터치) 드래그의 시작점과 끝점을 추적해서,
    /// 시작점이 어떤 TitleTalisman의 hitArea 위였고 변위가 minDragDistance 이상이면
    /// 그 방향을 대각선으로 분류해 해당 부적을 자른다.
    ///
    /// Input System 패키지(Mouse.current / Touchscreen.current) 직접 폴링 방식을 썼다 - 이
    /// 프로젝트가 PlayerInputReader 등에서 이미 Input System을 쓰고 있다는 전제다(레거시
    /// Input Manager만 쓰는 상태라면 이 스크립트는 그대로 동작하지 않는다 - 확인 필요).
    /// 메뉴 전용 제스처 하나라 별도 Input Actions 에셋 없이 디바이스를 직접 읽는 쪽이
    /// 가볍고 이 화면 전용 용도와 맞다고 판단했다.
    /// </summary>
    public class TitleDragCutController : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField] private LayerMask talismanLayer = ~0;

        [Tooltip("이것보다 짧게 그으면 베기로 인정하지 않는다(실수 클릭/짧은 드래그 방지). 값은 미정")]
        [SerializeField] private float minDragDistance = 0.5f;

        private bool isDragging;
        private Vector2 dragStartWorld;
        private TitleTalisman dragStartTarget;
        private TitleTalisman hoveredTarget;

        private void Awake()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }
        }

        private void Update()
        {
            if (!isDragging)
            {
                UpdateHover();
            }

            if (TryGetPointerDownThisFrame(out Vector2 downScreenPos))
            {
                StartDrag(ScreenToWorld(downScreenPos));
                return;
            }

            if (isDragging && TryGetPointerUpThisFrame(out Vector2 upScreenPos))
            {
                FinishDrag(ScreenToWorld(upScreenPos));
            }
        }

        /// <summary>
        /// 261013 추가 - 커서(마우스/터치)가 지금 어떤 부적 위에 있는지 매 프레임 판정해서
        /// 호버 시작/종료 시에만(대상이 바뀔 때만) TitleTalisman.SetHovered를 호출한다 -
        /// 매 프레임 색을 대입하는 낭비를 피하기 위함.
        /// </summary>
        private void UpdateHover()
        {
            Vector2 worldPos = ScreenToWorld(GetPointerScreenPosition());
            Collider2D hit = Physics2D.OverlapPoint(worldPos, talismanLayer);
            TitleTalisman target = hit != null ? hit.GetComponent<TitleTalisman>() : null;

            if (target == hoveredTarget)
            {
                return;
            }

            if (hoveredTarget != null)
            {
                hoveredTarget.SetHovered(false);
            }
            if (target != null && !target.IsCut)
            {
                target.SetHovered(true);
            }
            hoveredTarget = target;
        }

        private static Vector2 GetPointerScreenPosition()
        {
            if (Mouse.current != null)
            {
                return Mouse.current.position.ReadValue();
            }
            if (Touchscreen.current != null)
            {
                return Touchscreen.current.primaryTouch.position.ReadValue();
            }
            return default;
        }

        private void StartDrag(Vector2 worldPos)
        {
            Collider2D hit = Physics2D.OverlapPoint(worldPos, talismanLayer);
            dragStartTarget = hit != null ? hit.GetComponent<TitleTalisman>() : null;
            dragStartWorld = worldPos;
            isDragging = dragStartTarget != null && !dragStartTarget.IsCut;
        }

        private void FinishDrag(Vector2 worldPos)
        {
            TitleTalisman target = dragStartTarget;
            isDragging = false;
            dragStartTarget = null;

            if (target == null || target.IsCut)
            {
                return;
            }

            Vector2 delta = worldPos - dragStartWorld;
            if (delta.magnitude < minDragDistance)
            {
                return; // 너무 짧은 드래그 - 베기로 인정하지 않음
            }

            target.Cut(ClassifyDirection(delta), delta.normalized);
        }

        /// <summary>
        /// dx, dy 부호가 같으면(좌하 <-> 우상 방향) TopLeft -> BottomRight 대각선과 평행,
        /// 부호가 다르면(좌상 <-> 우하 방향) TopRight -> BottomLeft 대각선과 평행으로 분류한다.
        /// "왼쪽/오른쪽 대각선"이라는 기획 표현과 실제로 맞는 방향인지는 플레이 테스트로
        /// 확인해야 한다(확인 못 함) - 반대로 느껴지면 이 메서드의 두 반환값만 서로 바꾸면 된다.
        /// </summary>
        private static TitleTalisman.CutDirection ClassifyDirection(Vector2 delta)
        {
            bool sameSign = (delta.x >= 0f) == (delta.y >= 0f);
            return sameSign
                ? TitleTalisman.CutDirection.DiagonalTopRightToBottomLeft
                : TitleTalisman.CutDirection.DiagonalTopLeftToBottomRight;
        }

        private Vector2 ScreenToWorld(Vector2 screenPos)
        {
            return targetCamera != null
                ? (Vector2)targetCamera.ScreenToWorldPoint(screenPos)
                : screenPos;
        }

        private static bool TryGetPointerDownThisFrame(out Vector2 screenPos)
        {
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                screenPos = Mouse.current.position.ReadValue();
                return true;
            }

            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            {
                screenPos = Touchscreen.current.primaryTouch.position.ReadValue();
                return true;
            }

            screenPos = default;
            return false;
        }

        private static bool TryGetPointerUpThisFrame(out Vector2 screenPos)
        {
            if (Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame)
            {
                screenPos = Mouse.current.position.ReadValue();
                return true;
            }

            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasReleasedThisFrame)
            {
                screenPos = Touchscreen.current.primaryTouch.position.ReadValue();
                return true;
            }

            screenPos = default;
            return false;
        }
    }
}
