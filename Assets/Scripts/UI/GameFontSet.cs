using UnityEngine;
using UnityEngine.UI;

namespace CheonMyeong.UI
{
    public enum FontRole
    {
        Body,
        Title,
    }

    /// <summary>
    /// 261117 - 게임 글꼴 묶음(Resources/GameFonts.asset).
    /// - 261117 2차: 전부 픽셀 글꼴 Galmuri11(12px 격자라 12의 배수 크기에서 가장 또렷함).
    /// - 본문: Galmuri11 / 굵게는 Galmuri11 Bold 파일(가짜 굵게는 끈다). 제목: Galmuri11 Bold.
    /// 씬 글자는 씬에 직접 박혀 있고, 코드로 만드는 글자(보스 바 · 피해 숫자 · 투표 창 · 신내림 상세 등)는 여기서 가져간다.
    /// 에셋을 못 읽으면 씬이 이미 불러온 같은 이름 글꼴을 찾아 쓰고, 그것도 없을 때만 Unity 기본 글꼴(LegacyRuntime).
    /// </summary>
    [CreateAssetMenu(menuName = "CheonMyeong/Font Set", fileName = "GameFonts")]
    public class GameFontSet : ScriptableObject
    {
        [SerializeField] private Font body;
        [SerializeField] private Font bodyBold;
        [SerializeField] private Font title;

        private const string ResourcePath = "GameFonts";

        private const string BodyName = "Galmuri11";
        private const string BodyBoldName = "Galmuri11-Bold";
        private const string TitleName = "Galmuri11-Bold";

        private static GameFontSet cached;
        private static bool loaded;
        private static Font builtin;
        private static Font foundBody;
        private static Font foundBodyBold;
        private static Font foundTitle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            cached = null;
            loaded = false;
            builtin = null;
            foundBody = null;
            foundBodyBold = null;
            foundTitle = null;
        }

        private static GameFontSet Active
        {
            get
            {
                if (!loaded)
                {
                    loaded = true;
                    cached = Resources.Load<GameFontSet>(ResourcePath);
                    if (cached == null || cached.body == null || cached.bodyBold == null || cached.title == null)
                    {
                        // 에셋이 스크립트보다 먼저 임포트되면 연결이 비어 있을 수 있다 - 이미 씬이 불러온 글꼴을 이름으로 찾아 메운다.
                        Debug.LogWarning("[GameFontSet] Resources/GameFonts.asset 을 못 읽음 - 씬에 불러온 글꼴로 대신함. 에셋을 Reimport 해주세요.");
                        FindLoadedFonts();
                    }
                }
                return cached;
            }
        }

        private static void FindLoadedFonts()
        {
            Font[] all = Resources.FindObjectsOfTypeAll<Font>();
            for (int i = 0; i < all.Length; i++)
            {
                Font f = all[i];
                if (f == null)
                {
                    continue;
                }
                if (f.name == BodyName)
                {
                    foundBody = f;
                }
                else if (f.name == BodyBoldName)
                {
                    foundBodyBold = f;
                }
                else if (f.name == TitleName)
                {
                    foundTitle = f;
                }
            }
        }

        private Font BodyFont => body != null ? body : foundBody;
        private Font BodyBoldFont => bodyBold != null ? bodyBold : foundBodyBold;
        private Font TitleFont => title != null ? title : foundTitle;

        private static Font Builtin
        {
            get
            {
                if (builtin == null)
                {
                    builtin = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }
                return builtin;
            }
        }

        /// <summary>역할에 맞는 글꼴과, 그 글꼴에 줄 글자 모양(굵은 파일이 있으면 Normal).</summary>
        public static Font Get(FontRole role, bool bold, out FontStyle style)
        {
            GameFontSet set = Active;
            Font t = set != null ? set.TitleFont : foundTitle;
            Font bb = set != null ? set.BodyBoldFont : foundBodyBold;
            Font b = set != null ? set.BodyFont : foundBody;
            if (role == FontRole.Title && t != null)
            {
                style = FontStyle.Normal;
                return t;
            }
            if (bold && bb != null)
            {
                style = FontStyle.Normal;
                return bb;
            }
            if (b != null)
            {
                style = bold ? FontStyle.Bold : FontStyle.Normal;
                return b;
            }
            style = bold ? FontStyle.Bold : FontStyle.Normal;
            return Builtin;
        }

        public static Font Get(FontRole role, bool bold = false)
        {
            return Get(role, bold, out _);
        }

        public static void Apply(Text text, FontRole role, bool bold)
        {
            if (text == null)
            {
                return;
            }
            text.font = Get(role, bold, out FontStyle style);
            text.fontStyle = style;
        }

        public static void Apply(TextMesh text, FontRole role, bool bold)
        {
            if (text == null)
            {
                return;
            }
            text.font = Get(role, bold, out FontStyle style);
            text.fontStyle = style;
        }
    }
}
