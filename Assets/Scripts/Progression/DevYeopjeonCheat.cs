using CheonMyeong.Data;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CheonMyeong.Progression
{
    /// <summary>
    /// 261114 개발자 모드 - 엽전(PlayerWallet) 증감 치트. 에디터 / Development Build에서만 생긴다(정식 빌드엔 오브젝트 자체가 없음).
    /// F4 = +1,000 · F3 = -1,000, Shift를 같이 누르면 ×10(10,000). 어느 화면에서나 동작하고 화면들은 YeopjeonChanged로 바로 갱신된다.
    /// F8 = 신내림 초기화: 신내림 화면이 열려 있으면 거기서 보고 있는 캐릭터, 아니면 지금 쓰는 캐릭터(CurrentRun)의 칸을 전부 닫는다.
    /// (F1 · F2 · F5 · F10 · F11은 WaveManager 웨이브 치트가 쓴다.)
    /// </summary>
    public sealed class DevYeopjeonCheat : MonoBehaviour
    {
        public const int Step = 1000;

        private static DevYeopjeonCheat instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (instance != null || (!Application.isEditor && !Debug.isDebugBuild))
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
        }

        public static void Apply(int delta)
        {
            PlayerWallet.DebugAdjust(delta);
            Debug.Log($"[CheonMyeong] 개발자 모드 엽전 {(delta > 0 ? "+" : string.Empty)}{delta:N0} → {PlayerWallet.Yeopjeon:N0}");
        }
    }
}
