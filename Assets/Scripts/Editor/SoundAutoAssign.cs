using System.Collections.Generic;
using System.IO;
using CheonMyeong.Audio;
using UnityEditor;
using UnityEngine;

namespace CheonMyeong.EditorTools
{
    /// <summary>
    /// 261117 - 사운드 기획서(261116) 5장 "남은 것" 파일을 넣기만 하면 소리가 연결되게 한다.
    /// 파일 이름이 기획서 ID와 같으면(SFX_HIT.wav 등, 폴더 상관없음 · wav/mp3/ogg) 미리 만들어 둔 Cue의 빈 clips 칸에 자동으로 꽂는다.
    /// 이미 clip이 들어 있는 Cue는 건드리지 않는다(사람이 바꾼 것 보존).
    /// 메뉴 CheonMyeong/사운드/새 소리 다시 연결 = 이미 들어와 있는 파일까지 한 번에 다시 검사.
    /// </summary>
    public sealed class SoundAutoAssign : AssetPostprocessor
    {
        private const string CueFolder = "Assets/Audio/Cues/";

        private static readonly Dictionary<string, string[]> FileToCues = new Dictionary<string, string[]>
        {
            { "SFX_HIT", new[] { "Cue_HIT", "Cue_HIT_Crit", "Cue_Poison_Tick" } },
            { "SFX_BS_Impact", new[] { "Cue_BS_Impact", "Cue_Quake_Impact", "Cue_Kid_Impact" } },
            { "SFX_BS_Warn", new[] { "Cue_BS_Warn", "Cue_Elite_ChargeWarn" } },
            { "SFX_BS_Whoosh", new[] { "Cue_BS_Whoosh", "Cue_Kid_Swing", "Cue_Kid_Fly", "Cue_Kid_Spin" } },
            // 261121 - 사운드 기획서 v1.2: 물귀신 물소리(새로 뽑는 파일 하나). 들어갈 때는 같은 파일을 거꾸로(Cue reverse).
            { "SFX_EN_Splash", new[] { "Cue_Elite_Splash", "Cue_Elite_Splash_Reverse" } },
            { "SFX_BS_Grab", new[] { "Cue_BS_Grab" } },
            { "SFX_Coin", new[] { "Cue_Coin" } },
            { "SFX_Soul", new[] { "Cue_Soul_S", "Cue_Soul_M", "Cue_Soul_L" } },
            { "SFX_Paper_Flip", new[] { "Cue_Paper_Flip", "Cue_Paper_Flip_x3" } },
            { "SFX_UI_Click", new[] { "Cue_UI_Click", "Cue_UI_Back", "Cue_UI_Error" } },
            { "SFX_UI_Hover", new[] { "Cue_UI_Hover" } },
        };

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            int linked = 0;
            for (int i = 0; i < imported.Length; i++)
            {
                linked += TryLink(imported[i]);
            }
            for (int i = 0; i < moved.Length; i++)
            {
                linked += TryLink(moved[i]);
            }
            if (linked > 0)
            {
                Debug.Log($"[사운드] 새 소리 {linked}칸 연결함 (Assets/Audio/Cues).");
            }
        }

        [MenuItem("CheonMyeong/사운드/새 소리 다시 연결")]
        private static void RelinkAll()
        {
            int linked = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets" }))
            {
                linked += TryLink(AssetDatabase.GUIDToAssetPath(guid));
            }
            Debug.Log(linked > 0 ? $"[사운드] 새 소리 {linked}칸 연결함." : "[사운드] 새로 연결할 소리 없음(파일이 없거나 이미 연결됨).");
        }

        private static int TryLink(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".wav" && ext != ".mp3" && ext != ".ogg")
            {
                return 0;
            }
            if (!FileToCues.TryGetValue(Path.GetFileNameWithoutExtension(path), out string[] cueNames))
            {
                return 0;
            }
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null)
            {
                return 0;
            }
            int count = 0;
            for (int i = 0; i < cueNames.Length; i++)
            {
                var cue = AssetDatabase.LoadAssetAtPath<SoundCue>(CueFolder + cueNames[i] + ".asset");
                if (cue == null || HasClip(cue))
                {
                    continue;
                }
                cue.clips = new[] { clip };
                EditorUtility.SetDirty(cue);
                AssetDatabase.SaveAssetIfDirty(cue);
                count++;
            }
            return count;
        }

        private static bool HasClip(SoundCue cue)
        {
            if (cue.clips == null)
            {
                return false;
            }
            for (int i = 0; i < cue.clips.Length; i++)
            {
                if (cue.clips[i] != null)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
