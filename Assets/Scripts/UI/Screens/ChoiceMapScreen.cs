using CheonMyeong.Data;
using CheonMyeong.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CheonMyeong.UI.Screens
{
    /// <summary>
    /// CHOICE_MAP 화면 - 좌측 맵 타일(해금된 것만 선택 가능, 잠긴/추후 공개는 선택 불가) /
    /// 가운데 캐릭터 타일(261014 추가, 예섭 요청 "캐릭터 선택과 맵선택을 같은 화면에서") /
    /// 우측 "친구와 함께" 패널(방 만들기 / 초대 코드로 참가).
    ///
    /// 씬 분리 작업(261013) 반영 - 맵 목록은 더 이상 하드코딩이 아니라 MapCatalogSO 자산에서
    /// 읽는다. 맵을 고르면 CurrentRun.SelectedMap에 기록해두고, 이후 WaitRoomScreen(START)이
    /// 이 맵의 gameplaySceneName으로 실제 Gameplay 씬을 로드한다.
    ///
    /// 261014 추가 - 캐릭터 목록도 같은 패턴(CharacterCatalogSO)으로 이 화면에 같이 띄운다.
    /// 지금은 하영 1명만 해금 상태라 실질적으로 고를 게 없지만(기획 확정 - MVP=하영 1명),
    /// "캐릭터도 선택 안 하면 이전에 선택한 걸로"(예섭 요청)를 만족하려고 화면이 뜰 때
    /// CurrentRun.EnsureCharacterSelected를 호출해 자동으로 채운다.
    ///
    /// 261014 2차 수정 - "방 만들기 자체가 안 되야지 맵 선택 안 하면. 만들고 뒤로 하면
    /// 리소스 낭비임"(예섭 지적). 원래는 WaitRoomScreen의 START 버튼에서만 막았는데, 그
    /// 시점엔 이미 HostAsync()가 Relay 할당 + NetworkManager.StartHost()까지 끝낸 뒤라서
    /// 맵을 안 고른 걸 뒤늦게 깨닫고 뒤로 가기만 해도 이미 만든 세션(Relay 리소스)이
    /// 낭비된다. 그래서 가드를 room 생성 "이전" 시점인 이 화면의 방 만들기 버튼으로
    /// 앞당겼다 - OnCreateRoomClicked 참고. 검증 우선순위는 맵 → 캐릭터 순(예섭 지시) -
    /// 맵이 없으면 캐릭터 상태는 보지도 않고 바로 맵 에러만 띄운다. WaitRoomScreen 쪽
    /// 가드는 지금도 남겨뒀지만 이제 2차 방어선일 뿐이다(정상 경로에서는 거기 도달하기
    /// 전에 이미 여기서 막힘).
    ///
    /// 261014 2차 수정 - "선택된 맵과 캐릭터는 별도로 표기. 미정일 경우 미정으로 표기"
    /// (예섭 요청) 반영 - 기존엔 statusText 하나를 맵/캐릭터가 번갈아 덮어썼는데, 이제
    /// selectedMapText/selectedCharacterText 두 줄을 따로 두고 항상 "선택된 맵: 미정"/
    /// "선택된 캐릭터: 미정" 같은 형태로 현재 상태를 보여준다(RefreshSelectionLabels).
    /// statusText는 "추후 공개입니다"/"맵을 먼저 선택해주세요" 같은 일시적 안내/에러
    /// 문구 전용으로 역할을 좁혔다.
    ///
    /// 실제 세션 생성/참가는 SessionManager.HostAsync()/JoinAsync()를 그대로 호출한다
    /// (기존 OnGUI 임시 버튼이 하던 일을 여기로 옮긴 것 - SessionManager.cs 쪽 OnGUI
    /// 블록은 정식 UI가 생겼으니 제거했다). async void는 버튼 클릭 핸들러에서만
    /// 쓰는 프로젝트 관례를 따르고, 예외는 여기서 잡아 로그만 남긴다.
    /// </summary>
    public class ChoiceMapScreen : MonoBehaviour
    {
        [SerializeField] private SessionManager sessionManager;
        [SerializeField] private MapCatalogSO mapCatalog;
        [SerializeField] private Transform mapTilesContainer;
        [SerializeField] private CharacterCatalogSO characterCatalog;
        [SerializeField] private Transform characterTilesContainer;
        [SerializeField] private Button backButton;
        [SerializeField] private Button createRoomButton;
        [SerializeField] private InputField joinCodeInputField;
        [SerializeField] private Button joinButton;
        [SerializeField] private Text statusText;
        [SerializeField] private Text selectedMapText;
        [SerializeField] private Text selectedCharacterText;

        private void Awake()
        {
            if (backButton != null)
            {
                backButton.onClick.AddListener(() => UIFlowManager.Instance.ShowScreen(UIFlowManager.Lobby));
            }

            if (mapTilesContainer != null && mapCatalog != null)
            {
                // 자식 i번째 버튼 = mapCatalog.maps[i] - UICanvasBuilder가 같은 순서로 만들어둔다.
                for (int i = 0; i < mapTilesContainer.childCount && i < mapCatalog.maps.Length; i++)
                {
                    Button tileButton = mapTilesContainer.GetChild(i).GetComponent<Button>();
                    if (tileButton == null)
                    {
                        continue;
                    }

                    MapCatalogSO.MapEntry entry = mapCatalog.maps[i];
                    tileButton.onClick.AddListener(() => OnMapTileClicked(entry));
                }
            }

            if (characterTilesContainer != null && characterCatalog != null)
            {
                // 자식 i번째 버튼 = characterCatalog.characters[i] - 맵 타일과 완전히 같은 관례
                // (UICanvasBuilder가 같은 순서로 만들어둠).
                for (int i = 0; i < characterTilesContainer.childCount && i < characterCatalog.characters.Length; i++)
                {
                    Button tileButton = characterTilesContainer.GetChild(i).GetComponent<Button>();
                    if (tileButton == null)
                    {
                        continue;
                    }

                    CharacterCatalogSO.CharacterEntry entry = characterCatalog.characters[i];
                    tileButton.onClick.AddListener(() => OnCharacterTileClicked(entry));
                }
            }

            // 261014 추가 - 이 화면이 뜨는 시점(= 캐릭터를 고를 수 있는 시점)에 아직 이번
            // 세션에서 캐릭터를 안 골랐으면 마지막 선택(또는 첫 해금 캐릭터)으로 채운다.
            CurrentRun.EnsureCharacterSelected(characterCatalog);
            RefreshSelectionLabels();

            if (createRoomButton != null)
            {
                createRoomButton.onClick.AddListener(OnCreateRoomClicked);
            }
            if (joinButton != null)
            {
                joinButton.onClick.AddListener(() => _ = JoinAndEnterWaitRoomAsync());
            }
        }

        /// <summary>
        /// 261110 - 방 만들기/참가가 성공하면 버튼을 잠근 채로 대기실로 넘어가서, 나가기로 로비 → 다시 이 화면에 오면
        /// 방 만들기 · 참가 버튼이 계속 잠겨 있었다(방이 다시 안 만들어지던 원인 중 하나). 열릴 때마다 풀고 안내 문구도 비운다.
        /// </summary>
        private void OnEnable()
        {
            SetNetworkButtonsInteractable(true);
            SetStatus(string.Empty);
        }

        private void OnMapTileClicked(MapCatalogSO.MapEntry entry)
        {
            if (entry == null || !entry.isUnlocked || string.IsNullOrEmpty(entry.gameplaySceneName))
            {
                SetStatus("추후 공개 예정인 맵입니다.");
                return;
            }

            CurrentRun.SelectedMap = entry;
            SetStatus($"선택한 맵 · {entry.displayName}");
            RefreshSelectionLabels();
        }

        private void OnCharacterTileClicked(CharacterCatalogSO.CharacterEntry entry)
        {
            if (entry == null || !entry.isUnlocked)
            {
                SetStatus("추후 개발 예정인 캐릭터입니다.");
                return;
            }

            CurrentRun.SelectCharacter(entry);
            SetStatus($"선택한 캐릭터 · {entry.displayName}");
            RefreshSelectionLabels();
        }

        /// <summary>
        /// 261014 추가 - "선택된 맵과 캐릭터는 별도로 표기. 미정일 경우 미정으로 표기"(예섭
        /// 요청). 타일을 클릭할 때뿐 아니라 화면이 처음 뜰 때도 호출해서(Awake 끝에서)
        /// CurrentRun에 이미 들어있는 값(예: EnsureCharacterSelected로 자동 채워진 캐릭터)을
        /// 곧바로 반영한다.
        /// </summary>
        private void RefreshSelectionLabels()
        {
            if (selectedMapText != null)
            {
                selectedMapText.text = CurrentRun.SelectedMap != null
                    ? $"선택된 맵: {CurrentRun.SelectedMap.displayName}"
                    : "선택된 맵: 미정";
            }

            if (selectedCharacterText != null)
            {
                selectedCharacterText.text = CurrentRun.SelectedCharacter != null
                    ? $"선택된 캐릭터: {CurrentRun.SelectedCharacter.displayName}"
                    : "선택된 캐릭터: 미정";
            }
        }

        /// <summary>
        /// 261014 추가 - "방 만들기 자체가 안 되야지 맵 선택 안 하면"(예섭 요청). HostAsync()를
        /// 호출하기 "전에" 먼저 검증한다 - 호출 이후엔 이미 Relay 할당 + StartHost()가 끝나
        /// 있어서 뒤늦게 막아봐야 리소스 낭비(예섭 지적)이기 때문. 검증 순서는 맵 → 캐릭터
        /// (예섭 지시 "우선순위는 맵 선택 -> 캐릭터 선택") - 맵이 없으면 캐릭터는 보지도
        /// 않고 바로 반환. 캐릭터는 EnsureCharacterSelected가 화면 진입 시 이미 자동으로
        /// 채워주므로 이 체크가 실제로 걸릴 일은 거의 없지만(카탈로그가 완전히 비어있는
        /// 등 비정상 상태 대비), 그래도 "우선순위대로 둘 다 확인"하라는 지시를 그대로
        /// 반영해 남겨뒀다.
        /// </summary>
        private void OnCreateRoomClicked()
        {
            if (CurrentRun.SelectedMap == null || string.IsNullOrEmpty(CurrentRun.SelectedMap.gameplaySceneName))
            {
                SetStatus("맵을 먼저 선택해주세요.");
                return;
            }

            if (CurrentRun.SelectedCharacter == null)
            {
                SetStatus("캐릭터를 먼저 선택해주세요.");
                return;
            }

            _ = HostAndEnterWaitRoomAsync();
        }

        private async System.Threading.Tasks.Task HostAndEnterWaitRoomAsync()
        {
            if (sessionManager == null)
            {
                Debug.LogError("[CheonMyeong] ChoiceMapScreen에 SessionManager가 연결돼 있지 않습니다.");
                return;
            }

            // sessionManager.IsBusy 가드 - 응답 오기 전에 버튼을 연타하면 HostAsync가
            // 중복으로 들어가 세션이 두 개 만들어질 수 있어서, 버튼 자체를 꺼서 막는다.
            if (sessionManager.IsBusy)
            {
                return;
            }

            SetNetworkButtonsInteractable(false);
            try
            {
                SetStatus("방 만드는 중...");
                await sessionManager.HostAsync();
                UIFlowManager.Instance.ShowScreen(UIFlowManager.WaitRoom);
            }
            catch (System.Exception e)
            {
                SetStatus($"방 만들기 실패: {e.Message}");
                Debug.LogException(e);
                SetNetworkButtonsInteractable(true); // 실패 시엔 이 화면에 남으니 다시 눌러볼 수 있게 복원
            }
        }

        private async System.Threading.Tasks.Task JoinAndEnterWaitRoomAsync()
        {
            if (sessionManager == null)
            {
                Debug.LogError("[CheonMyeong] ChoiceMapScreen에 SessionManager가 연결돼 있지 않습니다.");
                return;
            }

            if (sessionManager.IsBusy)
            {
                return;
            }

            string code = joinCodeInputField != null ? joinCodeInputField.text : string.Empty;
            if (string.IsNullOrEmpty(code))
            {
                SetStatus("초대 코드를 입력해주세요.");
                return;
            }

            SetNetworkButtonsInteractable(false);
            try
            {
                SetStatus("참가하는 중...");
                await sessionManager.JoinAsync(code);
                UIFlowManager.Instance.ShowScreen(UIFlowManager.WaitRoom);
            }
            catch (System.Exception e)
            {
                SetStatus($"참가 실패: {e.Message}");
                Debug.LogException(e);
                SetNetworkButtonsInteractable(true);
            }
        }

        /// <summary>
        /// 호스트/참가 요청이 진행 중인 동안 두 버튼 + 코드 입력창을 전부 잠가서
        /// 연타로 인한 중복 요청(세션 중복 생성 등)을 막는다.
        /// </summary>
        private void SetNetworkButtonsInteractable(bool interactable)
        {
            if (createRoomButton != null) createRoomButton.interactable = interactable;
            if (joinButton != null) joinButton.interactable = interactable;
            if (joinCodeInputField != null) joinCodeInputField.interactable = interactable;
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }
        }
    }
}
