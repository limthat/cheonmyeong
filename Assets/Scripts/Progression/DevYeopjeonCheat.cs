using CheonMyeong.Data;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CheonMyeong.Progression
{
    /// <summary>
    /// 261114 개발자 모드 - 엽전(PlayerWallet) 증감 치트. 에디터 / Development Build에서만 생긴다(정식 빌드엔 오브젝트 자체가 없음).
    /// F4 = +1,000 · F3 = -1,000, Shift를 같이 누르면 ×10(10,000). 어느 화면에서나 동작하고 화면들은 YeopjeonChanged로 바로 갱신된다.
    /// F8 = 신내림 초기화: 신내림 화면이 열려 있으면 거기서 보고 있는 캐릭터, 아니면 지금 쓰는 캐릭터(CurrentRun)의 칸을 전부 닫는다.
    /// (F1 · F2 · F5 · F6 · F7 · F10 · F11은 WaveManager 치트가 쓴다 - 인게임 · 방장 화면에서만.)
    /// 261116 - 치트를 쓰면 결과를 왼쪽 아래에 잠깐 보여 준다. 키 안내는 F9로 켜고 끈다(261117 - 기본 숨김).
    ///   안내가 안 보이면 Development Build가 아닌 것. 노트북은 F키가 밝기 · 볼륨으로 잡혀 있으면 Fn을 같이 눌러야 한다.
    /// </summary>
    public sealed class DevYeopjeonCheat : MonoBehaviour
    {
        public const int Step = 1000;

        private static DevYeopjeonCheat instance;

        private const string HelpText =
            "[DEV] F3/F4 엽전 -/+1000 (Shift ×10) · F8 신내림 초기화 · F9 안내 숨기기\n" +
            "인게임(방장): F1 배속 · F2 단계 끝 · F5/F10/F11 이동 · F6 레벨업 · F7 무적";
        private const float MessageSeconds = 2.5f;

        private bool showHelp; // 261117 - 기본은 숨김(오른쪽 아래 Development Build 표시로 충분). F9로 볼 수 있다.
        private static string message;
        private static float messageUntil;
        private GUIStyle style;

        private static int cheatsEnabled = -1;

        /// <summary>
        /// 261117 - 치트를 켤지: 에디터 · Development Build, 또는 실행 인자 -cheats(일반 빌드 시연용).
        /// 예) CM.exe -cheats
        /// </summary>
        public static bool CheatsEnabled
        {
            get
            {
                if (cheatsEnabled < 0)
                {
                    bool arg = false;
                    string[] args = System.Environment.GetCommandLineArgs();
                    for (int i = 0; i < args.Length; i++)
                    {
                        if (string.Equals(args[i], "-cheats", System.StringComparison.OrdinalIgnoreCase))
                        {
                            arg = true;
                            break;
                        }
                    }
                    cheatsEnabled = Application.isEditor || Debug.isDebugBuild || arg ? 1 : 0;
                }
                return cheatsEnabled == 1;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            cheatsEnabled = -1;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (instance != null || !CheatsEnabled)
            {
                return;
            }
            var go = new GameObject("[Dev] YeopjeonCheat");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<DevYeopjeonCheat>();
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }
            if (keyboard.f9Key.wasPressedThisFrame)
            {
                showHelp = !showHelp;
            }
            if (keyboard.f8Key.wasPressedThisFrame)
            {
                ResetSinnaerim();
            }
            int sign = keyboard.f4Key.wasPressedThisFrame ? 1 : keyboard.f3Key.wasPressedThisFrame ? -1 : 0;
            if (sign == 0)
            {
                return;
            }
            bool shift = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            Apply(sign * Step * (shift ? 10 : 1));
        }

        public static void ResetSinnaerim()
        {
            string id = CharacterProgressStore.FocusedCharacterId;
            if (string.IsNullOrEmpty(id) && CurrentRun.SelectedCharacter != null)
            {
                id = CurrentRun.SelectedCharacter.characterId;
            }
            if (string.IsNullOrEmpty(id))
            {
                Debug.Log("[CheonMyeong] 개발자 모드 신내림 초기화 - 대상 캐릭터가 없음");
                return;
            }
            CharacterProgressStore.DebugResetSinnaerim(id);
            Debug.Log($"[CheonMyeong] 개발자 모드 신내림 초기화 → {id}");
            Notify("신내림 초기화: " + id);
        }

        public static void Apply(int delta)
        {
            PlayerWallet.DebugAdjust(delta);
            Debug.Log($"[CheonMyeong] 개발자 모드 엽전 {(delta > 0 ? "+" : string.Empty)}{delta:N0} → {PlayerWallet.Yeopjeon:N0}");
            Notify($"엽전 {(delta > 0 ? "+" : string.Empty)}{delta:N0} → {PlayerWallet.Yeopjeon:N0}");
        }

        /// <summary>치트 결과를 화면에 잠깐 보여 준다(WaveManager 치트도 부른다).</summary>
        public static void Notify(string text)
        {
            message = "> " + text;
            messageUntil = Time.unscaledTime + MessageSeconds;
        }

        private void OnGUI()
        {
            bool hasMessage = message != null && Time.unscaledTime < messageUntil;
            if (!showHelp && !hasMessage)
            {
                return;
            }
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { fontSize = 14, font = CheonMyeong.UI.GameFontSet.Get(CheonMyeong.UI.FontRole.Body) };
                style.normal.textColor = new Color(1f, 1f, 0.75f, 0.9f);
            }
            float y = Screen.height - 52f;
            if (showHelp)
            {
                GUI.Label(new Rect(8f, y, 900f, 48f), HelpText, style);
            }
            if (hasMessage)
            {
                GUI.Label(new Rect(8f, y - 24f, 900f, 24f), message, style);
            }
        }
    }
}
