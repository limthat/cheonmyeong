using CheonMyeong.Data;
using CheonMyeong.Networking;
using CheonMyeong.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace CheonMyeong.UI.Screens
{
    /// <summary>
    /// ESC(일시 메뉴) - "계속하기"/"설정"/"나가기(로비로)". 와이어프레임 캡션
    /// "온라인이라 게임은 멈추지 않음"을 그대로 따른다 - 그래서 이 스크립트는
    /// Time.timeScale을 절대 건드리지 않는다(UI만 덮어 보여주는 순수 오버레이).
    ///
    /// "나가기"(261110 수정)는 세션을 나가고 NetworkManager를 멈춘 뒤 Gameplay 씬을 내린다(SessionManager.LeaveAsync).
    /// </summary>
    public class EscMenuScreen : MonoBehaviour
    {
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button exitButton;

        private void Awake()
        {
            if (resumeButton != null)
            {
                resumeButton.onClick.AddListener(OnResumePressed);
            }
            if (settingsButton != null)
            {
                settingsButton.onClick.AddListener(() => UIFlowManager.Instance.OpenOverlay(UIFlowManager.Settings));
            }
            if (exitButton != null)
            {
                exitButton.onClick.AddListener(OnExitPressed);
            }
        }

        /// <summary>261110 - 일시정지(투표) 중이면 서버에 풀어달라고 한다. 닫힘은 PartyProgress가 상태를 보고 처리.</summary>
        private static void OnResumePressed()
        {
            PartyProgress party = PartyProgress.Instance;
            if (party != null && party.IsSpawned && party.IsMenuPaused)
            {
                party.RequestMenuResumeRpc();
                return;
            }
            UIFlowManager.Instance.CloseOverlay(UIFlowManager.EscMenu);
        }

        private static void OnExitPressed()
        {
            PartyProgress party = PartyProgress.Instance;
            if (party != null && party.IsSpawned && party.IsMenuPaused)
            {
                party.RequestMenuResumeRpc();
            }

            // 261101 - 중간에 나가도 그때까지 주운 엽전은 받는다(예섭 확정, 기획서 "연결 끊김" 규칙과 같게).
            PlayerProgress.SettleLocalPlayerRunGold();

            // 261110 - 세션 · 네트워크까지 정리한다(안 하면 로비로 와서 방을 다시 못 만듦). Gameplay 씬도 여기서 내린다.
            // 호스트가 나가면 동료는 끊김 처리(SessionManager.HandleClientStopped)로 로비에 온다.
            LeaveSession();
            UIFlowManager.Instance.ShowScreen(UIFlowManager.Lobby);
        }

        private static void LeaveSession()
        {
            if (SessionManager.Instance != null)
            {
                _ = SessionManager.Instance.LeaveAsync();
                return;
            }
            // SessionManager가 없는 테스트 씬 - 예전처럼 호스트가 Gameplay 씬만 내린다.
            string sceneName = CurrentRun.SelectedMap != null ? CurrentRun.SelectedMap.gameplaySceneName : null;
            if (!string.IsNullOrEmpty(sceneName))
            {
                GameplaySceneLoader.Instance?.HostUnloadGameplayScene(sceneName);
            }
        }
    }
}
