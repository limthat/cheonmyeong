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
    /// UI 화면 흐름이 아직 기획에 없어서(미정) OnGUI로 임시 버튼만 둔다.
    /// 기획 확정되면 이 OnGUI 블록만 들어내고 정식 UI에서 HostAsync/JoinAsync를
    /// 호출하도록 바꾸면 된다.
    /// </summary>
    public class SessionManager : MonoBehaviour
    {
        [SerializeField] private int maxPlayers = 4; // 인원수 미정 - 임시값

        private ISession currentSession;
        private string statusText = "대기 중";
        private string joinCodeInput = string.Empty;
        private bool isBusy;

        public async Task HostAsync()
        {
            await GameServicesBootstrap.Instance.InitializeAsync();

            SessionOptions options = new SessionOptions { MaxPlayers = maxPlayers }.WithRelayNetwork();
            currentSession = await MultiplayerService.Instance.CreateSessionAsync(options);

            statusText = $"호스트 시작 - 참가 코드: {currentSession.Code}";
            Debug.Log($"[CheonMyeong] 세션 생성 완료 (Id: {currentSession.Id}, Code: {currentSession.Code})");
        }

        public async Task JoinAsync(string code)
        {
            await GameServicesBootstrap.Instance.InitializeAsync();

            currentSession = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);

            statusText = $"참가 완료 (Id: {currentSession.Id})";
            Debug.Log($"[CheonMyeong] 세션 참가 완료: {currentSession.Id}");
        }

        // ----- 임시 디버그 UI (OnGUI) - 정식 UI/UX 확정 전까지만 사용 -----
        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 320, 160));
            GUILayout.Box($"[네트워크 테스트] {statusText}");

            GUI.enabled = !isBusy && currentSession == null;

            if (GUILayout.Button("호스트 시작"))
            {
                _ = RunSafely(HostAsync, "호스트 시작");
            }

            GUILayout.BeginHorizontal();
            joinCodeInput = GUILayout.TextField(joinCodeInput, GUILayout.Width(200));
            if (GUILayout.Button("코드로 참가"))
            {
                string code = joinCodeInput;
                _ = RunSafely(() => JoinAsync(code), "세션 참가");
            }
            GUILayout.EndHorizontal();

            GUI.enabled = true;
            GUILayout.EndArea();
        }

        private async Task RunSafely(Func<Task> action, string label)
        {
            isBusy = true;
            try
            {
                await action();
            }
            catch (Exception e)
            {
                statusText = $"{label} 실패: {e.Message}";
                Debug.LogException(e);
            }
            finally
            {
                isBusy = false;
            }
        }
    }
}
