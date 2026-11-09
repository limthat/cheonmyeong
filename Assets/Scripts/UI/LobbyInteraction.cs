using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CheonMyeong.UI
{
    /// <summary>
    /// 261101 추가 - 로비 캐릭터와 건물 사이의 상호작용.
    /// - 캐릭터 발밑에서 interactRadius 안의 가장 가까운 건물 입구 하나만 "가까움"으로 표시(호버 그림 + [E] 안내).
    /// - 그 상태에서 상호작용 키(기본 E)를 누르면 그 건물을 연다. 설정 창 같은 오버레이가 떠 있으면 무시.
    /// - 앞뒤 정렬: UI는 형제 순서대로 그려지므로, 건물 입구(바닥)보다 위(멀리)에 선 캐릭터는 그 건물 뒤에 그려지도록
    ///   캐릭터의 형제 순서를 바꾼다. buildings는 빌더가 멀리(위) → 가까이(아래) 순으로 넣고, 씬에서도 그 순서로 놓는다.
    /// </summary>
    public class LobbyInteraction : MonoBehaviour
    {
        [SerializeField] private LobbyAvatar avatar;

        [Tooltip("멀리(화면 위쪽) → 가까이(아래쪽) 순서. 형제 순서도 이 순서여야 앞뒤 정렬이 맞는다.")]
        [SerializeField] private LobbyBuilding[] buildings;

        [Tooltip("이 거리(캔버스 단위) 안에 들어오면 건물이 하이라이트되고 상호작용 키로 열 수 있다.")]
        [SerializeField] private float interactRadius = 170f;

        [Tooltip("상호작용 키.")]
        [SerializeField] private Key interactKey = Key.E;

        private LobbyBuilding nearest;
        private int lastAvatarSiblingIndex = -1;

        // 261109 - 앞뒤 정렬 대상: 캐릭터와 같은 부모 아래의 모든 건물(아트가 추가한 출진문처럼 상호작용 목록에 없는 것 포함),
        // 멀리(기준선이 위) → 가까이 순.
        private readonly List<LobbyBuilding> occluders = new List<LobbyBuilding>();
        private bool occludersReady;

        private void OnEnable()
        {
            occludersReady = false;
        }

        private void CollectOccluders()
        {
            occludersReady = true;
            occluders.Clear();
            if (avatar == null || avatar.transform.parent == null)
            {
                return;
            }
            Transform parent = avatar.transform.parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                if (parent.GetChild(i).TryGetComponent(out LobbyBuilding building))
                {
                    occluders.Add(building);
                }
            }
            occluders.Sort((a, b) => b.DepthLineY.CompareTo(a.DepthLineY));
        }

        private void OnDisable()
        {
            SetNearest(null);
            lastAvatarSiblingIndex = -1;
        }

        private void Update()
        {
            if (avatar == null || buildings == null)
            {
                return;
            }

            if (!occludersReady)
            {
                CollectOccluders();
            }
            Vector2 foot = avatar.FootPosition;
            SetNearest(FindNearest(foot));
            UpdateDepth(foot);

            bool overlayOpen = UIFlowManager.Instance != null && UIFlowManager.Instance.HasOverlay;
            Keyboard keyboard = Keyboard.current;
            if (!overlayOpen && nearest != null && keyboard != null && keyboard[interactKey].wasPressedThisFrame)
            {
                nearest.Activate();
            }
        }

        private LobbyBuilding FindNearest(Vector2 foot)
        {
            LobbyBuilding best = null;
            float bestSqr = interactRadius * interactRadius;
            for (int i = 0; i < buildings.Length; i++)
            {
                LobbyBuilding building = buildings[i];
                if (building == null)
                {
                    continue;
                }
                float sqr = (building.EntrancePosition - foot).sqrMagnitude;
                if (sqr <= bestSqr)
                {
                    bestSqr = sqr;
                    best = building;
                }
            }
            return best;
        }

        private void SetNearest(LobbyBuilding value)
        {
            if (nearest == value)
            {
                return;
            }
            if (nearest != null)
            {
                nearest.SetNearby(false);
            }
            nearest = value;
            if (nearest != null)
            {
                nearest.SetNearby(true);
            }
        }

        private void UpdateDepth(Vector2 foot)
        {
            int index = -1;
            for (int i = 0; i < occluders.Count; i++)
            {
                LobbyBuilding building = occluders[i];
                if (building != null && building.DepthLineY < foot.y)
                {
                    index = building.transform.GetSiblingIndex();
                    break;
                }
            }

            Transform avatarTransform = avatar.transform;
            if (index < 0)
            {
                index = avatarTransform.parent.childCount - 1;
            }
            else if (avatarTransform.GetSiblingIndex() < index)
            {
                index -= 1;
            }

            if (index != lastAvatarSiblingIndex || avatarTransform.GetSiblingIndex() != index)
            {
                avatarTransform.SetSiblingIndex(index);
                lastAvatarSiblingIndex = index;
            }
        }
    }
}
