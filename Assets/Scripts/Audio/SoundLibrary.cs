using System;
using System.Collections.Generic;
using UnityEngine;

namespace CheonMyeong.Audio
{
    /// <summary>
    /// 261115 - 소리 이름(SoundKeys) → SoundCue 연결표. Assets/Audio/SoundLibrary.asset 하나를 SoundManager가 쓴다.
    /// 인스펙터에서 칸마다 SoundCue를 끌어다 놓기만 하면 된다. SoundKeys에 새 이름이 생기면 에디터에서 칸이 자동으로 추가된다.
    /// </summary>
    [CreateAssetMenu(menuName = "CheonMyeong/Sound Library", fileName = "SoundLibrary")]
    public sealed class SoundLibrary : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string key;
            public SoundCue cue;
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();

        private Dictionary<string, SoundCue> lookup;

        public int Count => entries.Count;

        public SoundCue CueAt(int index) => index >= 0 && index < entries.Count ? entries[index].cue : null;

        public SoundCue Find(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }
            if (lookup == null)
            {
                BuildLookup();
            }
            return lookup.TryGetValue(key, out SoundCue cue) ? cue : null;
        }

        private void BuildLookup()
        {
            lookup = new Dictionary<string, SoundCue>(entries.Count, StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
            {
                Entry e = entries[i];
                if (!string.IsNullOrEmpty(e.key) && e.cue != null)
                {
                    lookup[e.key] = e.cue;
                }
            }
        }

        private void OnEnable()
        {
            lookup = null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            lookup = null;
            var known = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
            {
                known.Add(entries[i].key);
            }
            foreach (string key in SoundKeys.All)
            {
                if (known.Add(key))
                {
                    entries.Add(new Entry { key = key });
                }
            }
        }
#endif
    }
}
