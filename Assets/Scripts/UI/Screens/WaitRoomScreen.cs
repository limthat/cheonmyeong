using CheonMyeong.Data;
using CheonMyeong.Networking;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace CheonMyeong.UI.Screens
{
    /// <summary>
    /// WAIT_ROOM 화면 - 초대 코드 표시 + START 버튼. 방장/참가자 구분, 레디 상태 등은
    /// 아직 "방장이 누구인지/준비 완료 동기화"를 서버에서 알려주는 네트워크 로직이
    /// 없어서(2인 이상 네트워크 테스트는 우선순위 뒤쪽 항목) START 버튼은 지금은
    /// 누구나 누를 수 있게 둔다 - 방장 전용 제한은 그 네트워크 작업과 함께 추가 예정.
    ///
    /// 씬 분리(261013) 반영 - START를 누르면 로컬 UI는 즉시 Loading으로 넘어가고,
    /// 이 클라이언트가 실제 호스트일 때만 GameplaySceneLoader가 Gameplay 씬을 추가 로드한다
    /// (참가자가 눌러도 GameplaySceneLoader 내부 서버 권한 체크로 조용히 무시됨 - 참가자는
    /// 호스트가 로드를 시작하면 NGO가 알아서 같은 씬을 동기화해주므로, 자기 화면은 아래
    /// LoadingScreen이 GameplaySceneLoader.OnGameplaySceneLoaded를 구독해서 알아서 넘어간다).
    ///
    /// 261014 수정 - "맵 선택 안 하면 시작 안 되게 해줘. 버튼 누르면 문구 뜨게"(예섭 요청).
    /// 원래는 CurrentRun.SelectedMap이 비어있어도 일단 UI를 Loading 화면으로 넘겨버리고
    /// 콘솔에만 경고를 남겼는데(참가자는 맵을 안 골라도 정상이라 그렇게 짜둔 것), 정작
    /// "방을 만든 사람(호스트) 자신이 맵을 안 고르고 START를 누른" 경우에도 똑같이 Loading
    /// 화면으로 넘어가버려서 실제로 로드될 씬이 없으니 거기서 그대로 멈춰버리는 버그였다.
    /// 지금 이 클라이언트가 호스트인지(NetworkManager.Singleton.IsServer)를 먼저 확인해서,
    /// 호스트인데 맵이 안 골라져 있으면 화면 전환 자체를 막고 안내 문구만 띄운다 - 참가자는
    /// 맵을 고르지 않는 게 정상이라(CurrentRun.cs 주석 참고) 이 가드에서 제외한다.
    ///
    /// 261014 2차 수정 - "방 만들기 자체가 안 되야지 맵 선택 안 하면. 만들고 뒤로 하면
    /// 리소스 낭비임"(예섭 지적). 진짜 1차 방어선은 이제 ChoiceMapScreen.OnCreateRoomClicked
    /// (방 만들기 버튼 - HostAsync() 호출 "이전"에 막아서 Relay 할당 자체를 안 하게 함)로
    /// 옮겼다. 정상 경로라면 맵 없이는 애초에 여기(WAIT_ROOM)까지 올 수가 없으므로 아래
    /// 체크는 이제 비정상 상태(예: CurrentRun이 중간에 다른 코드로 초기화된 경우) 대비용
    /// 2차 방어선일 뿐이다.
    /// </summary>
    public class WaitRoomScreen : MonoBehaviour
    {
        [SerializeField] private SessionManager sessionManager;
        [SerializeField] private Text joinCodeText;
        [SerializeField] private Text statusText;
        [SerializeField] private Button backButton;
        [SerializeField] private Button startButton;

        // 매 프레임 같은 값을 또 문자열로 합쳐 Text.text에 쓰면 변화가 없어도 GC 할당이
        // 생긴다(실무에서 피하는 패턴) - 마지막으로 반영한 코드값을 기억해뒀다가
        // 실제로 바뀐 프레임에만 문자열을 새로 만든다.
        private string lastAppliedJoinCode;

        private void Awake()
        {
            if (backButton != null)
            {
                backButton.onClick.AddListener(() => UIFlowManager.Instance.ShowScreen(UIFlowManager.ChoiceMap));
            }
            if (startButton != null)
            {
                startButton.onClick.AddListener(OnStartPressed);
            }
        }

        private void OnStartPressed()
        {
            string sceneName = CurrentRun.SelectedMap != null ? CurrentRun.SelectedMap.gameplaySceneName : null;
            bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

            // 261014 추가 - 호스트인데 맵을 안 골랐으면(sceneName 없음) 여기서 막는다. 화면
            // 전환(Loading)까지 가버리면 실제로 로드할 씬이 없어 그대로 멈춰버리므로, 전환
            // 자체를 하지 않고 안내 문구만 띄운 채 이 화면에 그대로 남긴다. 참가자는 맵을
            // 직접 고르지 않는 게 정상이라(CurrentRun.cs 참고) 이 가드에서 제외한다.
            if (isHost && string.IsNullOrEmpty(sceneName))
            {
                SetStatus("맵을 선택해주세요.");
                return;
            }

            UIFlowManager.Instance.ShowScreen(UIFlowManager.Loading);

            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogWarning("[CheonMyeong] 선택된 맵 정보가 없어 Gameplay 씬을 로드하지 않습니다 " +
                                 "(참가자는 정상 - 호스트 쪽에서만 실제 로드가 일어남).");
                return;
            }
            GameplaySceneLoader.Instance?.HostLoadGameplayScene(sceneName);
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }
        }

        private void Update()
        {
            if (joinCodeText == null || sessionManager == null)
            {
                return;
            }

            string currentCode = sessionManager.JoinCode;
            if (currentCode == lastAppliedJoinCode)
            {
                return; // 코드가 안 바뀌었으면 문자열을 다시 만들지 않는다.
            }

            lastAppliedJoinCode = currentCode;
            joinCodeText.text = string.IsNullOrEmpty(currentCode)
                ? "초대 코드: --"
                : $"초대 코드: {currentCode}";
        }
    }
}
