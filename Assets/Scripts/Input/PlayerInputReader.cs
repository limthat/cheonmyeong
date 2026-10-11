using UnityEngine;
using UnityEngine.InputSystem;

namespace CheonMyeong.Input
{
    /// <summary>
    /// 입력만 읽어서 값으로 노출하는 클래스. 이동/전투 로직은 전혀 모른다.
    /// 새 Input System의 저수준 Keyboard/Mouse API를 직접 폴링하는 방식으로,
    /// 이벤트 구독이 없어 OnEnable/OnDisable 해제를 신경 쓸 필요가 없다.
    /// (.inputactions 액션 에셋 기반 리바인딩은 추후 필요해지면 이 클래스
    /// 내부만 바꾸면 되고, 이 클래스를 쓰는 쪽 코드는 그대로 둘 수 있다.)
    /// </summary>
    public class PlayerInputReader : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;

        /// <summary>WASD/방향키 입력 (-1~1 범위, 정규화하지 않음 - 대각선 보정은 이동 스크립트에서)</summary>
        public Vector2 MoveInput { get; private set; }

        /// <summary>마우스 커서의 월드 좌표 (z = 0 평면 기준, 탑뷰 2D용)</summary>
        public Vector3 AimWorldPosition { get; private set; }

        private void Awake()
        {
            // Camera.main은 내부적으로 태그 검색을 하므로 매 프레임 호출하지 않고 캐싱한다.
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }
        }

        private void Update()
        {
            ReadMove();
            ReadAim();
        }

        private void ReadMove()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                MoveInput = Vector2.zero;
                return;
            }

            float x = 0f;
            float y = 0f;

            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) x -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) x += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) y -= 1f;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) y += 1f;

            MoveInput = new Vector2(x, y);
        }

        private void ReadAim()
        {
            var mouse = Mouse.current;
            if (mouse == null || targetCamera == null)
            {
                return;
            }

            Vector2 screenPos = mouse.position.ReadValue();
            // 탑뷰 2D 기준: 카메라와 플레이어 평면 사이 거리를 알아야 정확하지만,
            // 프로토타입 단계에서는 카메라의 고정 거리값을 그대로 사용한다.
            Vector3 screenPoint = new Vector3(screenPos.x, screenPos.y, -targetCamera.transform.position.z);
            Vector3 world = targetCamera.ScreenToWorldPoint(screenPoint);
            world.z = 0f;
            AimWorldPosition = world;
        }
    }
}
