using System.Collections.Generic;
using CheonMyeong.Settings;
using UnityEngine;
using UnityEngine.Audio;

namespace CheonMyeong.Audio
{
    /// <summary>재생 중인 소리 하나를 가리킨다(반복음 끄기용). 칸이 다른 소리에 넘어가면 자동으로 무효가 된다.</summary>
    public readonly struct SoundHandle
    {
        public readonly int Index;
        public readonly int Generation;

        public SoundHandle(int index, int generation)
        {
            Index = index;
            Generation = generation;
        }

        public bool IsValid => Generation > 0;
        public static SoundHandle None => default;
    }

    /// <summary>
    /// 261115 - 소리 재생 담당(이 PC 전용, 네트워크 X). Bootstrap 씬(SampleScene)의 [Sound] 오브젝트에 있다.
    /// 소리는 이미 모두에게 복제되는 이벤트(웨이브 단계 · 레벨 · 타격 연출 등)에서 각자 화면이 직접 낸다 - 소리용 RPC를 따로 만들지 않는다.
    ///
    /// - 효과음: AudioSource를 미리 voiceCount개 만들어 돌려 쓴다(재생마다 생성/파괴 X → GC · 끊김 없음).
    ///   칸이 모자라면 우선순위가 낮고 오래된 것부터 끊는다. Cue별 MaxInstances · MinInterval로 같은 소리 폭주를 막는다.
    /// - 배경음: 덱 2개로 교차 페이드. 한 덱은 최대 MaxLayers층을 동시에 재생(Layered Cue) - SetBgmIntensity로 층 수 조절.
    /// - 볼륨: 설정의 배경음 · 효과음 · 목소리 값을 곱한다(전체 음량 · 배경 음소거는 GameSettings가 AudioListener로 처리).
    ///   동료가 낸 소리는 '동료 소리 크기'를 곱한다. AudioMixerGroup을 넣으면 그쪽으로도 보낸다(선택).
    /// - 라이브러리에 칸이 비어 있으면 조용히 넘어간다(에디터에선 키마다 한 번만 알림) - 소스는 나중에 넣어도 된다.
    /// </summary>
    public sealed class SoundManager : MonoBehaviour
    {
        public const int MaxLayers = 4;

        [SerializeField] private SoundLibrary library;
        [Tooltip("동시에 낼 수 있는 효과음 개수(칸).")]
        [Range(8, 64)] [SerializeField] private int voiceCount = 32;

        [Header("믹서 (선택 - 비워 두면 바로 출력)")]
        [SerializeField] private AudioMixerGroup sfxGroup;
        [SerializeField] private AudioMixerGroup bgmGroup;
        [SerializeField] private AudioMixerGroup voiceGroup;
        [SerializeField] private AudioMixerGroup uiGroup;

        [Header("배경음")]
        [Min(0f)] [SerializeField] private float defaultBgmFade = 1.2f;
        [Min(0f)] [SerializeField] private float layerFade = 1.5f;

        private sealed class Voice
        {
            public AudioSource Source;
            public SoundCue Cue;
            public float StartedAt;
            public float BaseVolume;
            public bool FromAlly;
            public int Generation;
        }

        private sealed class CueState
        {
            public float LastPlayed = float.NegativeInfinity;
            public int LastClip = -1;
            public int Playing;
        }

        private sealed class BgmDeck
        {
            public readonly AudioSource[] Layers = new AudioSource[MaxLayers];
            public SoundCue Cue;
            public float Volume;        // 덱 전체(교차 페이드)
            public float Target;
            public float FadeSpeed;
            public readonly float[] LayerVolume = new float[MaxLayers];
            public int LayerCount;
        }

        public static SoundManager Instance { get; private set; }

        private Voice[] voices;
        private readonly Dictionary<SoundCue, CueState> cueStates = new Dictionary<SoundCue, CueState>(64);
        private readonly HashSet<string> warnedKeys = new HashSet<string>();
        private readonly BgmDeck[] decks = { new BgmDeck(), new BgmDeck() };
        private int activeDeck;
        private int bgmIntensity = 1;
        private float busSfx = 1f, busBgm = 1f, busVoice = 1f, allyScale = 1f;
        private Transform listener;
        private Camera listenerCamera;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[CheonMyeong] SoundManager가 두 개 이상 있습니다 - 하나만 남기고 제거하세요.");
                Destroy(this);
                return;
            }
            Instance = this;
            BuildVoices();
            BuildDecks();
            ReadSettings();
        }

        private void OnEnable()
        {
            GameSettings.Changed += HandleSettingsChanged;
            ReadSettings();
        }

        private void OnDisable()
        {
            GameSettings.Changed -= HandleSettingsChanged;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        // ---------------- 효과음

        public SoundHandle Play(string key, Vector2? position = null, bool fromAlly = false)
        {
            return Play(Resolve(key), position, fromAlly);
        }

        public SoundHandle Play(SoundCue cue, Vector2? position = null, bool fromAlly = false)
        {
            if (cue == null || !cue.HasClips || voices == null)
            {
                return SoundHandle.None;
            }
            if (cue.bus == SoundBus.Bgm)
            {
                PlayBgm(cue, defaultBgmFade);
                return SoundHandle.None;
            }

            CueState state = StateOf(cue);
            float now = Time.unscaledTime;
            if (now - state.LastPlayed < cue.minInterval)
            {
                return SoundHandle.None;
            }

            float distanceGain = 1f;
            float pan = 0f;
            if (cue.positional && position.HasValue && TryGetListener(out Vector2 ear))
            {
                Vector2 offset = position.Value - ear;
                float distance = offset.magnitude;
                if (distance >= cue.hearRange)
                {
                    return SoundHandle.None;
                }
                float t = distance / cue.hearRange;
                distanceGain = 1f - t * t;
                pan = Mathf.Clamp(offset.x / cue.hearRange, -1f, 1f) * cue.stereoSpread;
            }

            if (cue.maxInstances > 0 && state.Playing >= cue.maxInstances)
            {
                StopOldestOf(cue);
            }

            int index = FindFreeVoice(cue.priority);
            if (index < 0)
            {
                return SoundHandle.None;
            }

            Voice voice = voices[index];
            StopVoice(voice);
            AudioSource src = voice.Source;
            src.clip = PickClip(cue, state);
            src.loop = cue.loop;
            src.priority = cue.priority;
            src.pitch = Random.Range(Mathf.Min(cue.pitchRange.x, cue.pitchRange.y), Mathf.Max(cue.pitchRange.x, cue.pitchRange.y));
            src.panStereo = pan;
            src.outputAudioMixerGroup = GroupOf(cue.bus);
            voice.Cue = cue;
            voice.FromAlly = fromAlly;
            voice.BaseVolume = cue.volume * (1f - Random.value * cue.volumeJitter) * distanceGain;
            voice.StartedAt = now;
            voice.Generation++;
            src.volume = voice.BaseVolume * BusVolume(cue.bus) * (fromAlly ? allyScale : 1f);
            src.Play();

            state.LastPlayed = now;
            state.Playing++;
            return new SoundHandle(index, voice.Generation);
        }

        public void Stop(SoundHandle handle)
        {
            if (!handle.IsValid || voices == null || handle.Index < 0 || handle.Index >= voices.Length)
            {
                return;
            }
            Voice voice = voices[handle.Index];
            if (voice.Generation == handle.Generation)
            {
                StopVoice(voice);
            }
        }

        public void StopAllSfx()
        {
            if (voices == null)
            {
                return;
            }
            for (int i = 0; i < voices.Length; i++)
            {
                StopVoice(voices[i]);
            }
        }

        // ---------------- 배경음

        public void PlayBgm(string key, float fade = -1f)
        {
            SoundCue cue = Resolve(key);
            if (cue == null)
            {
                // 소리가 아직 없는 곡으로 넘어가면 이전 곡은 끈다(엉뚱한 곡이 계속 나오지 않게).
                StopBgm(fade);
                return;
            }
            PlayBgm(cue, fade);
        }

        public void PlayBgm(SoundCue cue, float fade = -1f)
        {
            if (cue == null || !cue.HasClips)
            {
                StopBgm(fade);
                return;
            }
            if (fade < 0f)
            {
                fade = defaultBgmFade;
            }
            BgmDeck current = decks[activeDeck];
            if (current.Cue == cue && current.Target > 0f)
            {
                return;
            }

            // 지금 덱은 꺼지게, 다른 덱에서 새 곡 시작
            FadeDeck(current, 0f, fade);
            activeDeck = 1 - activeDeck;
            BgmDeck next = decks[activeDeck];
            StopDeck(next);
            next.Cue = cue;
            next.LayerCount = cue.layered ? Mathf.Min(cue.clips.Length, MaxLayers) : 1;
            int clipIndex = cue.layered ? 0 : Random.Range(0, cue.clips.Length);
            double startAt = AudioSettings.dspTime + 0.05d; // 층끼리 정확히 맞춰 시작
            for (int i = 0; i < next.LayerCount; i++)
            {
                AudioSource src = next.Layers[i];
                src.clip = cue.clips[cue.layered ? i : clipIndex];
                src.loop = true;
                src.pitch = 1f;
                src.outputAudioMixerGroup = GroupOf(SoundBus.Bgm);
                next.LayerVolume[i] = !cue.layered || i < bgmIntensity ? 1f : 0f;
                src.PlayScheduled(startAt);
            }
            next.Volume = fade > 0f ? 0f : 1f;
            FadeDeck(next, 1f, fade);
            ApplyDeckVolumes();
        }

        public void StopBgm(float fade = -1f)
        {
            if (fade < 0f)
            {
                fade = defaultBgmFade;
            }
            FadeDeck(decks[0], 0f, fade);
            FadeDeck(decks[1], 0f, fade);
        }

        /// <summary>Layered 배경음에서 들릴 층 수(1~MaxLayers). 최종전 원한 단계 등에 쓴다.</summary>
        public void SetBgmIntensity(int layers)
        {
            bgmIntensity = Mathf.Clamp(layers, 1, MaxLayers);
        }

        // ---------------- 내부

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            bool changed = false;

            for (int d = 0; d < decks.Length; d++)
            {
                BgmDeck deck = decks[d];
                if (deck.Cue == null)
                {
                    continue;
                }
                if (!Mathf.Approximately(deck.Volume, deck.Target))
                {
                    deck.Volume = deck.FadeSpeed <= 0f ? deck.Target : Mathf.MoveTowards(deck.Volume, deck.Target, deck.FadeSpeed * dt);
                    changed = true;
                }
                if (deck.Cue.layered)
                {
                    float step = layerFade <= 0f ? 1f : dt / layerFade;
                    for (int i = 0; i < deck.LayerCount; i++)
                    {
                        float target = i < bgmIntensity ? 1f : 0f;
                        if (!Mathf.Approximately(deck.LayerVolume[i], target))
                        {
                            deck.LayerVolume[i] = Mathf.MoveTowards(deck.LayerVolume[i], target, step);
                            changed = true;
                        }
                    }
                }
                if (deck.Target <= 0f && deck.Volume <= 0f)
                {
                    StopDeck(deck);
                }
            }
            if (changed)
            {
                ApplyDeckVolumes();
            }

            // 끝난 효과음 칸을 비운다(재생 수 집계 정리).
            for (int i = 0; i < voices.Length; i++)
            {
                Voice voice = voices[i];
                if (voice.Cue != null && !voice.Source.isPlaying)
                {
                    ReleaseVoice(voice);
                }
            }
        }

        private void BuildVoices()
        {
            voices = new Voice[voiceCount];
            var root = new GameObject("Sfx").transform;
            root.SetParent(transform, false);
            for (int i = 0; i < voiceCount; i++)
            {
                voices[i] = new Voice { Source = CreateSource(root, "Voice_" + i) };
            }
        }

        private void BuildDecks()
        {
            var root = new GameObject("Bgm").transform;
            root.SetParent(transform, false);
            for (int d = 0; d < decks.Length; d++)
            {
                for (int i = 0; i < MaxLayers; i++)
                {
                    decks[d].Layers[i] = CreateSource(root, "Deck" + d + "_Layer" + i);
                    decks[d].Layers[i].priority = 0;
                }
            }
        }

        private static AudioSource CreateSource(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            AudioSource src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f; // 2D(위치감은 SoundCue.positional이 볼륨 · 좌우로 직접 계산)
            src.dopplerLevel = 0f;
            return src;
        }

        private SoundCue Resolve(string key)
        {
            SoundCue cue = library != null ? library.Find(key) : null;
#if UNITY_EDITOR
            if (cue == null && !string.IsNullOrEmpty(key) && warnedKeys.Add(key))
            {
                Debug.Log($"[CheonMyeong] 소리 '{key}' - 아직 SoundLibrary에 연결된 소리가 없어 건너뜀(한 번만 표시).");
            }
#endif
            return cue;
        }

        private CueState StateOf(SoundCue cue)
        {
            if (!cueStates.TryGetValue(cue, out CueState state))
            {
                state = new CueState();
                cueStates.Add(cue, state);
            }
            return state;
        }

        private static AudioClip PickClip(SoundCue cue, CueState state)
        {
            int count = cue.clips.Length;
            int index = count == 1 ? 0 : Random.Range(0, count);
            if (count > 1 && index == state.LastClip)
            {
                index = (index + 1 + Random.Range(0, count - 1)) % count;
            }
            state.LastClip = index;
            return cue.clips[index];
        }

        /// <summary>빈 칸 → 없으면 (덜 중요하고 오래된) 칸을 빼앗는다. 새 소리가 더 덜 중요하면 -1.</summary>
        private int FindFreeVoice(int priority)
        {
            int victim = -1;
            int victimPriority = -1;
            float victimStart = float.MaxValue;
            for (int i = 0; i < voices.Length; i++)
            {
                Voice voice = voices[i];
                if (voice.Cue == null)
                {
                    return i;
                }
                int p = voice.Cue.priority;
                if (p > victimPriority || (p == victimPriority && voice.StartedAt < victimStart))
                {
                    victim = i;
                    victimPriority = p;
                    victimStart = voice.StartedAt;
                }
            }
            return victimPriority >= priority ? victim : -1;
        }

        private void StopOldestOf(SoundCue cue)
        {
            Voice oldest = null;
            for (int i = 0; i < voices.Length; i++)
            {
                Voice voice = voices[i];
                if (voice.Cue == cue && (oldest == null || voice.StartedAt < oldest.StartedAt))
                {
                    oldest = voice;
                }
            }
            if (oldest != null)
            {
                StopVoice(oldest);
            }
        }

        private void StopVoice(Voice voice)
        {
            if (voice.Cue == null)
            {
                return;
            }
            voice.Source.Stop();
            ReleaseVoice(voice);
        }

        private void ReleaseVoice(Voice voice)
        {
            if (voice.Cue != null && cueStates.TryGetValue(voice.Cue, out CueState state))
            {
                state.Playing = Mathf.Max(0, state.Playing - 1);
            }
            voice.Cue = null;
            voice.Source.clip = null;
        }

        private static void FadeDeck(BgmDeck deck, float target, float seconds)
        {
            deck.Target = target;
            deck.FadeSpeed = seconds > 0f ? 1f / seconds : 0f;
        }

        private static void StopDeck(BgmDeck deck)
        {
            for (int i = 0; i < MaxLayers; i++)
            {
                AudioSource src = deck.Layers[i];
                if (src.isPlaying)
                {
                    src.Stop();
                }
                src.clip = null;
                deck.LayerVolume[i] = 0f;
            }
            deck.Cue = null;
            deck.Volume = 0f;
            deck.Target = 0f;
            deck.LayerCount = 0;
        }

        private void ApplyDeckVolumes()
        {
            for (int d = 0; d < decks.Length; d++)
            {
                BgmDeck deck = decks[d];
                if (deck.Cue == null)
                {
                    continue;
                }
                float baseVolume = deck.Cue.volume * deck.Volume * busBgm;
                for (int i = 0; i < deck.LayerCount; i++)
                {
                    deck.Layers[i].volume = baseVolume * deck.LayerVolume[i];
                }
            }
        }

        private void HandleSettingsChanged()
        {
            ReadSettings();
        }

        private void ReadSettings()
        {
            SettingsData s = GameSettings.Active;
            busSfx = s.Sfx;
            busBgm = s.Bgm;
            busVoice = s.Voice;
            allyScale = GameSettings.AllyVolumeScale;
            ApplyDeckVolumes();
            if (voices == null)
            {
                return;
            }
            // 반복 효과음 등 지금 울리는 소리도 새 볼륨으로.
            for (int i = 0; i < voices.Length; i++)
            {
                Voice voice = voices[i];
                if (voice.Cue != null)
                {
                    voice.Source.volume = voice.BaseVolume * BusVolume(voice.Cue.bus) * (voice.FromAlly ? allyScale : 1f);
                }
            }
        }

        private float BusVolume(SoundBus bus)
        {
            switch (bus)
            {
                case SoundBus.Bgm: return busBgm;
                case SoundBus.Voice: return busVoice;
                default: return busSfx; // Sfx · Ui
            }
        }

        private AudioMixerGroup GroupOf(SoundBus bus)
        {
            switch (bus)
            {
                case SoundBus.Bgm: return bgmGroup;
                case SoundBus.Voice: return voiceGroup;
                case SoundBus.Ui: return uiGroup != null ? uiGroup : sfxGroup;
                default: return sfxGroup;
            }
        }

        private bool TryGetListener(out Vector2 position)
        {
            if (listener == null || listenerCamera == null || !listenerCamera.isActiveAndEnabled)
            {
                listenerCamera = Camera.main;
                listener = listenerCamera != null ? listenerCamera.transform : null;
            }
            if (listener == null)
            {
                position = default;
                return false;
            }
            position = listener.position;
            return true;
        }
    }
}
