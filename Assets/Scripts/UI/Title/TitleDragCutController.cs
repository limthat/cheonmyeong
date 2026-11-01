using UnityEngine;
using UnityEngine.InputSystem;

namespace CheonMyeong.UI.Title
{
    /// <summary>
    /// 타이틀 화면에 하나만 둔다. 포인터(마우스/터치)로 부적(TitleTalisman)을 누르고 같은 부적 위에서 떼면
    /// 그 부적을 발동한다(시작/설정/종료).
    ///
    /// 261101 수정 - 원래는 대각선으로 그어서 "베는" 드래그였는데, 아트에서 잘린 조각 애니메이션을 만들기 어렵다고 해서
    /// (예섭) 일반 클릭으로 바꿨다. 클래스/파일 이름은 씬에 붙은 스크립트 참조가 끊기지 않도록 그대로 둔다.
    /// 누른 부적과 뗀 부적이 같아야 발동한다(누른 채로 밖으로 끌고 나가면 취소 - 일반 버튼과 같은 규칙).
    ///
    /// Input System 패키지(Mouse.current / Touchscreen.current) 직접 폴링 방식.
    /// </summary>
    public class TitleDragCutController : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField] private LayerMask talismanLayer = ~0;

        private TitleTalisman pressedTarget;
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
            // 설정 창 등 오버레이가 떠 있으면 그 아래 부적이 같이 눌리지 않게 막는다(클릭 방식이라 실수로 종료가 눌릴 수 있음).
            if (IsBlockedByUi())
            {
                pressedTarget = null;
                if (hoveredTarget != null)
                {
                    hoveredTarget.SetHovered(false);
                    hoveredTarget = null;
                }
                return;
            }

            UpdateHover();

            if (TryGetPointerDownThisFrame(out Vector2 downScreenPos))
            {
                TitleTalisman target = FindTalisman(ScreenToWorld(downScreenPos));
                pressedTarget = target != null && !target.IsCut ? target : null;
                return;
            }

            if (pressedTarget != null && TryGetPointerUpThisFrame(out Vector2 upScreenPos))
            {
                TitleTalisman released = FindTalisman(ScreenToWorld(upScreenPos));
                TitleTalisman pressed = pressedTarget;
                pressedTarget = null;
                if (released == pressed && !pressed.IsCut)
                {
                    pressed.Activate();
                }
            }
        }

        private static bool IsBlockedByUi()
        {
            return UIFlowManager.Instance != null && UIFlowManager.Instance.HasOverlay;
        }

        private TitleTalisman FindTalisman(Vector2 worldPos)
        {
            Collider2D hit = Physics2D.OverlapPoint(worldPos, talismanLayer);
            return hit != null ? hit.GetComponent<TitleTalisman>() : null;
        }

        private void UpdateHover()
        {
            TitleTalisman target = FindTalisman(ScreenToWorld(GetPointerScreenPosition()));
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
