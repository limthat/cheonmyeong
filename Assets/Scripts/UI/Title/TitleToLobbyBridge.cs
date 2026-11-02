using CheonMyeong.UI.Screens;
using UnityEngine;

namespace CheonMyeong.UI.Title
{
    /// <summary>
    /// 타이틀 화면의 세 부적(Start/Settings/Exit - CheonMyeongPrototypeSetup.
    /// CreateTitleTalismanPrototype()이 만드는 이름 그대로)이 잘렸을 때 UI 플로우로
    /// 연결하는 다리 역할. TitleTalisman.OnCutCompleted에 거는 용도로 설계된
    /// 스크립트(TitleTalisman.cs 주석 "버튼 로직을 여기 연결한다" 참고).
    ///
    /// Start 부적을 자르면: 타이틀 오브젝트를 숨기고 UIFlowManager로 Lobby를 연다.
    /// Settings 부적을 자르면: 타이틀은 그대로 둔 채 Settings를 오버레이로만 연다
    /// (currentBaseScreenId가 아직 없는 상태라 ApplyVisibility가 Settings 패널만
    /// 켜고 나머지 화면은 그대로 꺼진 채로 둔다 - 타이틀 오브젝트는애초에
    /// UIFlowManager가 관리하는 화면이 아니라서 영향받지 않는다). 설정 화면의
    /// "뒤로" 버튼이 CloseOverlay()를 부르면 자동으로 타이틀로 돌아온다.
    /// Exit 부적을 자르면: 에디터에서는 플레이 모드를 멈추고, 빌드에서는 앱을 끈다.
    /// </summary>
    public class TitleToLobbyBridge : MonoBehaviour
    {
        [SerializeField] private TitleTalisman startTalisman;
        [SerializeField] private TitleTalisman settingsTalisman;
        [SerializeField] private TitleTalisman exitTalisman;
        [SerializeField] private GameObject titleRoot;
        [Tooltip("261101 - 타이틀 대문. 있으면 시작 부적을 눌렀을 때 문이 열린 뒤 로비로 넘어간다.")]
        [SerializeField] private TitleGate gate;

        private void OnEnable()
        {
            if (startTalisman != null) startTalisman.OnCutCompleted += HandleStartCut;
            if (settingsTalisman != null) settingsTalisman.OnCutCompleted += HandleSettingsCut;
            if (exitTalisman != null) exitTalisman.OnCutCompleted += HandleExitCut;

            if (UIFlowManager.Instance != null)
            {
                UIFlowManager.Instance.OnOverlayClosed += HandleOverlayClosed;
            }
        }

        private void OnDisable()
        {
            if (startTalisman != null) startTalisman.OnCutCompleted -= HandleStartCut;
            if (settingsTalisman != null) settingsTalisman.OnCutCompleted -= HandleSettingsCut;
            if (exitTalisman != null) exitTalisman.OnCutCompleted -= HandleExitCut;

            if (UIFlowManager.Instance != null)
            {
                UIFlowManager.Instance.OnOverlayClosed -= HandleOverlayClosed;
            }
        }

        private void HandleStartCut()
        {
            if (gate != null && gate.isActiveAndEnabled)
            {
                SetTalismansVisible(false); // 열리는 문(그림 속 부적이 같이 움직임) 위에 덮개 부적이 남지 않게
                gate.PlayOpen(FinishStart);
                return;
            }
            FinishStart();
        }

        private void FinishStart()
        {
            if (titleRoot != null)
            {
                titleRoot.SetActive(false);
            }
            // 다음에 타이틀이 다시 켜질 때를 위해 부적을 원래대로 돌려둔다(titleRoot가 꺼져 있어 화면엔 안 보임).
            SetTalismansVisible(true);
            if (startTalisman != null)
            {
                startTalisman.Reset();
            }
            UIFlowManager.Instance.ShowScreen(UIFlowManager.Lobby);
        }

        private void SetTalismansVisible(bool visible)
        {
            if (startTalisman != null) startTalisman.gameObject.SetActive(visible);
            if (settingsTalisman != null) settingsTalisman.gameObject.SetActive(visible);
            if (exitTalisman != null) exitTalisman.gameObject.SetActive(visible);
        }

        private void HandleSettingsCut()
        {
            UIFlowManager.Instance.OpenOverlay(UIFlowManager.Settings);
        }

        /// <summary>
        /// 261013 추가 - Settings를 닫고 타이틀로 돌아왔을 때 그 부적을 다시 벨 수 있는
        /// "온전한" 상태로 되돌린다. Start/Exit는 애초에 타이틀을 떠나는(Lobby로 가거나
        /// 앱이 꺼지는) 일회성 동작이라 리셋 대상이 아니다.
        /// </summary>
        private void HandleOverlayClosed(string screenId)
        {
            if (screenId == UIFlowManager.Settings && settingsTalisman != null)
            {
                settingsTalisman.Reset();
            }
        }

        private static void HandleExitCut()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
