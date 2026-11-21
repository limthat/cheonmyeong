using UnityEngine;

namespace CheonMyeong.UI
{
    /// <summary>
    /// 261121 추가 - Btn.aseprite 4종(기획서 v1.2 확정, 더 안 그림) 그림 묶음. Resources/ButtonSkin.asset 하나만 둔다.
    /// 순서 = Btn-Sheet 번호: 0 Btn · 1 Btn(흰 테두리) · 2 imprt-Btn(적) · 3 imprt(흰 테두리) · 4 Un_Btn(비활성) · 5 Btn-2ver(나무 간판) · 6 간판(흰 테두리).
    /// 설정 슬라이더(재사용): 막대 = Bar xp 바(Bar-Sheet_2), 손잡이 = 황금 엽전(coin_xp-Sheet_5).
    /// </summary>
    [CreateAssetMenu(fileName = "ButtonSkin", menuName = "CheonMyeong/Button Skin Set")]
    public sealed class ButtonSkinSet : ScriptableObject
    {
        private const string ResourcePath = "ButtonSkin";

        [Tooltip("Btn-Sheet_0 ~ _6.")]
        public Sprite[] buttons = new Sprite[7];
        [Tooltip("설정 슬라이더 막대 - Bar-Sheet_2(xp 바).")]
        public Sprite sliderTrack;
        [Tooltip("설정 슬라이더 손잡이 - coin_xp-Sheet_5(황금 엽전).")]
        public Sprite sliderKnob;

        private static ButtonSkinSet cached;
        private static bool loaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            cached = null;
            loaded = false;
        }

        public static ButtonSkinSet Load()
        {
            if (!loaded)
            {
                loaded = true;
                cached = Resources.Load<ButtonSkinSet>(ResourcePath);
            }
            return cached;
        }

        /// <summary>그림이 4종 중 어느 종류인지(0 Btn · 1 imprt · 2 비활성 · 3 나무 간판). 아니면 -1.</summary>
        public int KindOf(Sprite sprite)
        {
            if (sprite == null || buttons == null)
            {
                return -1;
            }
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == sprite)
                {
                    switch (i)
                    {
                        case 0:
                        case 1: return 0;
                        case 2:
                        case 3: return 1;
                        case 4: return 2;
                        default: return 3;
                    }
                }
            }
            return -1;
        }

        public Sprite Normal(int kind) => Get(kind == 1 ? 2 : kind == 2 ? 4 : kind == 3 ? 5 : 0);
        public Sprite Hover(int kind) => Get(kind == 1 ? 3 : kind == 2 ? 4 : kind == 3 ? 6 : 1);
        public Sprite Disabled => Get(4);

        private Sprite Get(int index) => buttons != null && index >= 0 && index < buttons.Length ? buttons[index] : null;
    }
}
