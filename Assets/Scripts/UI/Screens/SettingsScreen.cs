using UnityEngine;
using UnityEngine.UI;

namespace CheonMyeong.UI.Screens
{
    /// <summary>
    /// SETTINGS 화면 - 로비에서 열면 전체화면(Lobby를 대체), ESC 메뉴 위에서 열면
    /// 오버레이(설정 하나 닫히면 ESC 메뉴로 돌아감), 타이틀의 "Settings" 부적을
    /// 자른 경우에도 오버레이(닫으면 타이틀로 돌아감) - 세 경로 전부 같은 화면
    /// 하나를 재사용한다. "뒤로" 버튼은 IsTopOverlay로 지금 자신이 오버레이로
    /// 떠 있는지 확인해서 분기한다(UIFlowManager.IsTopOverlay 참고).
    ///
    /// 전체 음량은 AudioListener.volume으로 바로 실제 동작한다(즉시 구현 가능).
    /// 배경음/효과음은 아직 AudioMixer 구성이 없어서 값만 받아두고 로그만 남긴다 -
    /// 믹서가 생기면 OnBgmVolumeChanged/OnSfxVolumeChanged 안쪽만 채우면 됨.
    /// </summary>
    public class SettingsScreen : MonoBehaviour
    {
        [SerializeField] private Button backButton;
        [Tooltip("261110 - 게임 종료(앱 끄기). 비어 있으면 자식 중 QuitButton을 찾는다.")]
        [SerializeField] private Button quitButton;
        [SerializeField] private Slider masterVolumeSlider;
        [SerializeField] private Slider bgmVolumeSlider;
        [SerializeField] private Slider sfxVolumeSlider;

        private void Awake()
        {
            if (backButton != null)
            {
                backButton.onClick.AddListener(OnBackPressed);
            }
            if (quitButton == null)
            {
                Transform found = FindChild(transform, "QuitButton");
                if (found != null)
                {
                    found.TryGetComponent(out quitButton);
                }
            }
            if (quitButton != null)
            {
                quitButton.onClick.AddListener(QuitGame);
            }
            if (masterVolumeSlider != null)
            {
                masterVolumeSlider.value = AudioListener.volume;
                masterVolumeSlider.onValueChanged.AddListener(v => AudioListener.volume = v);
            }
            if (bgmVolumeSlider != null)
            {
                bgmVolumeSlider.onValueChanged.AddListener(OnBgmVolumeChanged);
            }
            if (sfxVolumeSlider != null)
            {
                sfxVolumeSlider.onValueChanged.AddListener(OnSfxVolumeChanged);
            }
        }

        private void OnBackPressed()
        {
            if (UIFlowManager.Instance.IsTopOverlay(UIFlowManager.Settings))
            {
                UIFlowManager.Instance.CloseOverlay();
            }
            else
            {
                UIFlowManager.Instance.ShowScreen(UIFlowManager.Lobby);
            }
        }

        /// <summary>
        /// 261110 - 게임을 완전히 끈다. 세션은 SessionManager.OnApplicationQuit에서 LeaveAsync로 정리된다.
        /// 에디터에서는 Application.Quit이 무시되므로 플레이 모드를 끈다.
        /// </summary>
        private static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private static Transform FindChild(Transform root, string childName)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == childName)
                {
                    return child;
                }
                Transform deeper = FindChild(child, childName);
                if (deeper != null)
                {
                    return deeper;
                }
            }
            return null;
        }

        private static void OnBgmVolumeChanged(float value)
        {
            Debug.Log($"[CheonMyeong] 배경음(국악) 볼륨 {value:0.00} - AudioMixer 미구성, 실제 반영은 아직 안 됨.");
        }

        private static void OnSfxVolumeChanged(float value)
        {
            Debug.Log($"[CheonMyeong] 효과음 볼륨 {value:0.00} - AudioMixer 미구성, 실제 반영은 아직 안 됨.");
        }
    }
}
