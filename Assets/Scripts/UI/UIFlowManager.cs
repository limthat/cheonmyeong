using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CheonMyeong.UI
{
    /// <summary>
    /// 전체 UI 화면 전환을 관리하는 런타임 매니저 - wireframe-reference_261013.md의
    /// "천명 · UI 플로우" 다이어그램을 그대로 코드로 옮긴 것. 화면은 두 종류로 나눈다:
    ///
    /// - "베이스" 화면(ShowScreen): 메인 플로우를 따라가는 전체 화면 전환. 전환하면
    ///   오버레이 스택은 전부 비워지고 그 화면 하나만 보인다 - 로비/맵선택/대기실/로딩/
    ///   컷신/인게임/결과, 그리고 로비에서 여닫는 스킬트리·스토리보관함도 여기 해당한다
    ///   (와이어프레임상 이 셋은 로비를 완전히 대체하는 전체 화면이라서).
    /// - "오버레이"(OpenOverlay/CloseOverlay): 현재 베이스 화면 위에 겹쳐 보이는 화면
    ///   (ESC 메뉴, 레벨업 증강 선택, 그리고 ESC 위에서 연 설정) - 스택 구조라서 설정을
    ///   ESC 위에서 열어도 ESC 키 한 번에 한 겹씩 정확히 닫힌다.
    ///
    /// 화면 GameObject는 전부 screensRoot 아래 자식으로 두고, 이 매니저가 Awake에서
    /// 자식 이름으로 자동 등록한다 - 화면이 늘어나도 이 스크립트를 고칠 필요가 없게
    /// 하기 위함(UICanvasBuilder가 이름만 맞춰 자식으로 넣으면 끝).
    ///
    /// ESC 키 처리를 이 매니저가 중앙에서 전담하는 이유: 오버레이가 몇 겹이든("설정"을
    /// ESC 위에서 연 경우 등) "ESC는 항상 가장 위에 뜬 것부터 하나씩 닫는다"는 동작이
    /// 화면마다 따로 구현하면 꼬이기 쉬운데, 한 곳에서만 처리하면 항상 일관된다.
    /// </summary>
    public class UIFlowManager : MonoBehaviour
    {
        public const string Lobby = "Lobby";
        public const string CharSkillTree = "CharSkillTree";
        public const string StoryStorage = "StoryStorage";
        public const string ChoiceMap = "ChoiceMap";
        public const string WaitRoom = "WaitRoom";
        public const string Loading = "Loading";
        public const string MapStory = "MapStory";
        public const string InGame = "InGame";
        public const string Upgrade = "Upgrade";
        public const string Reward = "Reward";
        public const string Settings = "Settings";
        public const string EscMenu = "EscMenu";

        public static UIFlowManager Instance { get; private set; }

        [Tooltip("이 트랜스폼의 직속 자식들을 화면으로 자동 등록한다 - 자식 GameObject 이름이 " +
                 "곧 화면 id(Lobby/ChoiceMap/... 위 상수들)여야 한다.")]
        [SerializeField] private Transform screensRoot;

        private readonly Dictionary<string, GameObject> screensById = new Dictionary<string, GameObject>();
        private readonly List<string> overlayStack = new List<string>();

        // 261014 추가 - 최적화/정리: ApplyVisibility가 화면 수만큼 매번 overlayStack.Contains()를
        // 호출하던 것을 O(1) 조회로 바꾸기 위한 보조 자료구조. overlayStack(순서가 중요한 List)는
        // CloseOverlay/IsTopOverlay가 "맨 위" 판정에 여전히 필요해서 그대로 둔다 - 이건 "지금 스택에
        // 몇 번 들어있는지"만 보는 조회 전용 캐시다. 같은 화면이 이론상 중첩으로 두 번 열릴 수도
        // 있어 단순 HashSet이 아니라 등장 횟수를 세는 Dictionary로 둔다(List.Contains와 완전히
        // 같은 의미를 유지하기 위함 - 동작 변화 없음, 조회 비용만 줄임).
        private readonly Dictionary<string, int> overlayMembershipCounts = new Dictionary<string, int>();

        private string currentBaseScreenId;

        private void Awake()
        {
            Instance = this;

            screensById.Clear();
            if (screensRoot != null)
            {
                foreach (Transform child in screensRoot)
                {
                    screensById[child.name] = child.gameObject;
                }
            }
        }

        private void Update()
        {
            // New Input System 기준(Keyboard.current) - 이 프로젝트 전역이 이미 이 방식을
            // 쓰고 있다(PlayerInputReader/TitleDragCutController 참고).
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                HandleEscapePressed();
            }
        }

        private void HandleEscapePressed()
        {
            if (overlayStack.Count > 0)
            {
                CloseOverlay();
            }
            else if (currentBaseScreenId == InGame)
            {
                OpenOverlay(EscMenu);
            }
        }

        /// <summary>베이스 화면 전환 - 오버레이 스택을 전부 비우고 이 화면 하나만 보여준다.</summary>
        public void ShowScreen(string screenId)
        {
            overlayStack.Clear();
            overlayMembershipCounts.Clear();
            currentBaseScreenId = screenId;
            ApplyVisibility();
        }

        /// <summary>현재 화면 위에 오버레이를 하나 더 쌓는다(ESC 위의 설정처럼 중첩 가능).</summary>
        public void OpenOverlay(string screenId)
        {
            overlayStack.Add(screenId);
            overlayMembershipCounts.TryGetValue(screenId, out int count);
            overlayMembershipCounts[screenId] = count + 1;
            ApplyVisibility();
        }

        /// <summary>어떤 오버레이가 CloseOverlay()로 닫혔을 때 그 screenId와 함께 발생한다
        /// (261013 추가) - 예: 타이틀에서 Settings 부적을 잘라 연 설정을 닫았을 때, 타이틀
        /// 쪽(TitleToLobbyBridge)이 이 신호를 받아서 그 부적을 다시 벨 수 있는 상태로
        /// 되돌리는 용도.</summary>
        public event Action<string> OnOverlayClosed;

        /// <summary>가장 위에 쌓인 오버레이 하나를 닫는다. 쌓인 게 없으면 아무 일도 안 한다.</summary>
        public void CloseOverlay()
        {
            if (overlayStack.Count > 0)
            {
                string closedScreenId = overlayStack[overlayStack.Count - 1];
                overlayStack.RemoveAt(overlayStack.Count - 1);

                if (overlayMembershipCounts.TryGetValue(closedScreenId, out int count))
                {
                    if (count <= 1)
                    {
                        overlayMembershipCounts.Remove(closedScreenId);
                    }
                    else
                    {
                        overlayMembershipCounts[closedScreenId] = count - 1;
                    }
                }

                OnOverlayClosed?.Invoke(closedScreenId);
            }
            ApplyVisibility();
        }

        /// <summary>screenId가 지금 오버레이 스택 맨 위에 떠 있는지 - 같은 화면을 "로비에서
        /// 전체화면으로 열었는지" vs "다른 화면 위에 오버레이로 열었는지" 구분할 때 쓴다
        /// (예: SettingsScreen의 "뒤로" 버튼이 ShowScreen(Lobby) / CloseOverlay() 중 뭘 할지 결정).</summary>
        public bool IsTopOverlay(string screenId)
        {
            return overlayStack.Count > 0 && overlayStack[overlayStack.Count - 1] == screenId;
        }

        private void ApplyVisibility()
        {
            foreach (KeyValuePair<string, GameObject> entry in screensById)
            {
                bool shouldShow = entry.Key == currentBaseScreenId || overlayMembershipCounts.ContainsKey(entry.Key);
                if (entry.Value != null)
                {
                    entry.Value.SetActive(shouldShow);
                }
            }
        }
    }
}
