using System;
using System.Threading.Tasks;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace CheonMyeong.Networking
{
    /// <summary>
    /// Multiplayer Services Session API로 호스트 세션 생성 / 참가 코드로 참가를
    /// 처리한다. WithRelayNetwork()를 쓰면 Session SDK가 내부적으로 Lobby +
    /// Relay를 묶어서 처리하고, 세션이 준비되면 NGO NetworkManager를 찾아
    /// host/client로 자동 시작해준다 - 직접 NetworkManager.StartHost() /
    /// StartClient()를 호출할 필요가 없다(공식 문서 기준, 2.4 버전).
    ///
    /// 기획 확정(261013, UI 전반 구현 1순위) - 임시 OnGUI 디버그 버튼은 제거했다.
    /// 이제 ChoiceMapScreen(방 만들기/참가 코드 입력)과 WaitRoomScreen(초대 코드
    /// 표시)이 HostAsync/JoinAsync/JoinCode를 직접 호출·참조한다.
    /// </summary>
    public class SessionManager : MonoBehaviour
    {
        [SerializeField] private int maxPlayers = 4; // 인원수 미정 - 임시값

        private ISession currentSession;

        /// <summary>참가 코드. 세션이 없으면 null - WaitRoomScreen이 매 프레임 읽어서 표시한다.</summary>
        public string JoinCode => currentSession?.Code;

        /// <summary>호스트/참가 작업이 진행 중인지 - UI가 중복 클릭을 막는 데 쓸 수 있다.</summary>
        public bool IsBusy { get; private set; }

        public async Task HostAsync()
        {
            IsBusy = true;
            try
            {
                await GameServicesBootstrap.Instance.InitializeAsync();

                SessionOptions options = new SessionOptions { MaxPlayers = maxPlayers }.WithRelayNetwork();
                currentSession = await MultiplayerService.Instance.CreateSessionAsync(options);

                Debug.Log($"[CheonMyeong] 세션 생성 완료 (Id: {currentSession.Id}, Code: {currentSession.Code})");
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task JoinAsync(string code)
        {
            IsBusy = true;
            try
            {
                await GameServicesBootstrap.Instance.InitializeAsync();

                currentSession = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);

                Debug.Log($"[CheonMyeong] 세션 참가 완료: {currentSession.Id}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// 261014 추가 - Play 모드 종료 시 콘솔에 뜨던
        /// "GameObjectsNetcodeNetworkHandler.StopAsync: Failed to stop session: session was
        /// never started" 경고 보완. 원인: 이 프로젝트 어디에도 세션을 정리하는 코드가 없어서
        /// (currentSession을 만들기만 하고 끝낼 때 아무것도 안 호출함), Play 모드를 끄면 Unity
        /// Services Multiplayer SDK가 내부적으로 자체 정리를 시도하다 이 경고를 남긴 것으로
        /// 보인다(SDK 내부 소스는 파일 접근 한계로 끝까지 확인은 못 했음 - 확인 못 함). Unity
        /// 공식 문서("Best practices for Joining and Leaving multiple Sessions")가 세션을 끝낼
        /// 때의 유일한 정석 경로로 명시한 ISession.LeaveAsync()를 OnApplicationQuit에서 호출해서
        /// 세션이 존재하면 정상적으로 정리하게 한다 - 에디터에서 Play 모드를 끌 때도, 실제
        /// 빌드에서 앱을 종료할 때도 OnApplicationQuit은 동일하게 호출된다. 메모리 누수 예방
        /// 차원에서도(세션/Relay 리소스를 정리 없이 그냥 버려두지 않음) 맞는 방향이라 적용함.
        /// 기존 HostAsync/JoinAsync 동작은 전혀 건드리지 않았다 - 순수 추가.
        /// </summary>
        private async void OnApplicationQuit()
        {
            if (currentSession == null)
            {
                return;
            }

            try
            {
                await currentSession.LeaveAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CheonMyeong] 세션 정리(LeaveAsync) 중 오류: {e.Message}");
            }
            finally
            {
                currentSession = null;
            }
        }
    }
}
