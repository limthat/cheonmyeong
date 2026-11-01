using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CheonMyeong.UI
{
    /// <summary>
    /// 261101 추가 - 로비 건물 하나(기획서 v0.9.3 "건물이 곧 메뉴. 큰 전각 = 게임 시작, 상점 = 스킬 · 캐릭터, 게시판 = 스토리 저장소").
    /// 이 오브젝트의 위치 기준점(pivot)은 건물 바닥 가운데 = 입구 지점이다.
    ///
    /// 하이라이트(호버) 조건 두 가지 - 둘 중 하나라도 맞으면 호버 그림(Lobby-Sheet의 외곽선 버전)과 "[E] 이름" 안내를 보인다.
    /// - 마우스가 건물 위에 있음(IPointerEnter/Exit).
    /// - 캐릭터가 가까이 있음(LobbyInteraction이 SetNearby로 알려줌).
    /// 열기: 마우스 클릭, 또는 가까이 있을 때 상호작용 키(LobbyInteraction). 둘 다 Activated 이벤트 하나로 모인다.
    /// </summary>
    public class LobbyBuilding : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        [SerializeField] private Image normalImage;
        [SerializeField] private Image hoverImage;
        [SerializeField] private GameObject prompt;

        [Tooltip("캐릭터가 들어갈 수 없는 바닥 영역의 깊이(건물 바닥에서 위로, 캔버스 단위). 0이면 막지 않음.")]
        [SerializeField] private float footprintDepth = 60f;

        [Tooltip("바닥 영역 너비 = 건물 그림 너비 × 이 비율.")]
        [Range(0f, 1f)] [SerializeField] private float footprintWidthRatio = 0.8f;

        public event Action Activated;

        private RectTransform rectTransform;
        private bool pointerInside;
        private bool nearby;
        private bool shown;

        /// <summary>입구 지점(부모 기준 로컬 좌표).</summary>
        public Vector2 EntrancePosition => rectTransform.localPosition;

        private void Awake()
        {
            rectTransform = (RectTransform)transform;
            Apply(true);
        }

        private void OnDisable()
        {
            pointerInside = false;
            nearby = false;
            Apply(true);
        }

        public void SetNearby(bool value)
        {
            if (nearby == value)
            {
                return;
            }
            nearby = value;
            Apply(false);
        }

        public void Activate()
        {
            Activated?.Invoke();
        }

        /// <summary>발밑 좌표(부모 기준 로컬)가 건물 바닥 영역 안인지 - LobbyAvatar가 통과 못 하게 막는 데 쓴다.</summary>
        public bool BlocksPoint(Vector2 foot)
        {
            if (footprintDepth <= 0f)
            {
                return false;
            }
            Vector2 entrance = rectTransform.localPosition;
            float halfWidth = rectTransform.rect.width * footprintWidthRatio * 0.5f;
            return foot.x > entrance.x - halfWidth && foot.x < entrance.x + halfWidth
                   && foot.y > entrance.y && foot.y < entrance.y + footprintDepth;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            pointerInside = true;
            Apply(false);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            pointerInside = false;
            Apply(false);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                Activate();
            }
        }

        private void Apply(bool force)
        {
            bool highlight = pointerInside || nearby;
            if (!force && highlight == shown)
            {
                return;
            }
            shown = highlight;

            if (hoverImage != null)
            {
                hoverImage.enabled = highlight;
                if (normalImage != null)
                {
                    normalImage.enabled = !highlight;
                }
            }
            if (prompt != null)
            {
                prompt.SetActive(highlight);
            }
        }
    }
}
