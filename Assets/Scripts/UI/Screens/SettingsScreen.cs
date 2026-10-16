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
        [SerializeField] private Slider masterVolumeSlider;
        [SerializeField] private Slider bgmVolumeSlider;
        [SerializeField] private Slider sfxVolumeSlider;

        private void Awake()
        {
            if (backButton != null)
            {
                backButton.onClick.AddListener(OnBackPressed);
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
