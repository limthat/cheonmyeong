using UnityEngine;
using UnityEngine.UI;

namespace CheonMyeong.UI
{
    /// <summary>
    /// 261115 - 화면 위쪽 가운데에 잠깐 뜨는 알림(연결 끊김 · 동료 입장/퇴장 등). UICanvas/NoticeToast에 붙어 있다.
    /// - NoticeToast.Show("...")로 어디서든 띄운다. 새 알림이 오면 이전 알림을 바꿔 쓴다.
    /// - 쉬는 동안엔 Update를 끈다(enabled = false). 시간은 unscaled라 일시정지와 상관없다.
    /// - 오브젝트가 아직 없을 때 들어온 알림은 하나만 맡아 뒀다가 생기면 띄운다.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class NoticeToast : MonoBehaviour
    {
        private const float FadeSeconds = 0.25f;
        public const float DefaultSeconds = 3.5f;

        [SerializeField] private Text message;

        private static NoticeToast instance;
        private static string pendingText;
        private static float pendingSeconds;

        private CanvasGroup group;
        private float timeLeft;
        private float totalSeconds;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            pendingText = null;
        }

        public static void Show(string text, float seconds = DefaultSeconds)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            Debug.Log("[CheonMyeong] 알림: " + text);
            if (instance == null)
            {
                pendingText = text;
                pendingSeconds = seconds;
                return;
            }
            instance.Display(text, seconds);
        }

        private void Awake()
        {
            instance = this;
            group = GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
            if (message == null)
            {
                message = GetComponentInChildren<Text>(true);
            }
            enabled = false;
            if (pendingText != null)
            {
                Display(pendingText, pendingSeconds);
                pendingText = null;
            }
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        private void Display(string text, float seconds)
        {
            if (message != null)
            {
                message.text = text;
            }
            totalSeconds = Mathf.Max(FadeSeconds * 2f, seconds);
            timeLeft = totalSeconds;
            transform.SetAsLastSibling();
            enabled = true;
            CheonMyeong.Audio.GameAudio.Play(CheonMyeong.Audio.SoundKeys.UiNotice);
        }

        private void Update()
        {
            timeLeft -= Time.unscaledDeltaTime;
            if (timeLeft <= 0f)
            {
                group.alpha = 0f;
                enabled = false;
                return;
            }
            float shown = totalSeconds - timeLeft;
            group.alpha = Mathf.Clamp01(Mathf.Min(shown / FadeSeconds, timeLeft / FadeSeconds));
        }
    }
}
