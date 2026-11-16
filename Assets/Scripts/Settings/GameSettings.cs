using System;
using System.Collections.Generic;
using UnityEngine;

namespace CheonMyeong.Settings
{
    /// <summary>
    /// 261115 - 설정 값 한 벌. 설정창은 이걸 복사해서(작업본) 고치고, 저장할 때만 GameSettings.Save로 넘긴다.
    /// 값 타입(struct)이라 복사 · 비교가 할당 없이 된다.
    /// </summary>
    [Serializable]
    public struct SettingsData : IEquatable<SettingsData>
    {
        // 화면
        public int ScreenMode;      // 0 전체 화면(독점) / 1 테두리 없는 창 / 2 창 모드
        public int Width;
        public int Height;
        public bool VSync;
        public int FrameCapIndex;   // GameSettings.FrameCaps 인덱스
        public int Shake;           // 0 끔 / 1 약하게 / 2 보통

        // 소리(전체 음량만 실제 반영, 나머지는 틀만 - 오디오 연결 시 GameSettings에서 값만 읽으면 됨)
        public float Master;
        public float Bgm;
        public float Sfx;
        public float Voice;
        public int AllyVolume;      // 0 작게 / 1 보통 / 2 크게
        public bool MuteInBackground;

        // 조작
        public int CursorSize;      // 0 보통 / 1 크게
        public bool ConfineCursor;

        // 기타
        public bool DamageNumbers;
        public bool HitEffects;

        public bool Equals(SettingsData o)
        {
            return ScreenMode == o.ScreenMode && Width == o.Width && Height == o.Height && VSync == o.VSync
                && FrameCapIndex == o.FrameCapIndex && Shake == o.Shake
                && Mathf.Approximately(Master, o.Master) && Mathf.Approximately(Bgm, o.Bgm)
                && Mathf.Approximately(Sfx, o.Sfx) && Mathf.Approximately(Voice, o.Voice)
                && AllyVolume == o.AllyVolume && MuteInBackground == o.MuteInBackground
                && CursorSize == o.CursorSize && ConfineCursor == o.ConfineCursor
                && DamageNumbers == o.DamageNumbers && HitEffects == o.HitEffects;
        }

        public override bool Equals(object obj) => obj is SettingsData other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(ScreenMode, Width, Height, FrameCapIndex, Shake, CursorSize);
    }

    /// <summary>
    /// 261115 - 게임 설정(PlayerPrefs 저장). 다른 게임들 기본 구성(화면 · 소리 · 조작 · 기타)을 따른다.
    /// - Saved: 저장된 값. Preview: 설정창에서 고치는 중인 값을 바로 보여 준다(저장 X). Revert: 저장된 값으로 되돌린다.
    /// - 해상도 · 화면 모드는 시작할 때 다시 적용하지 않는다. Unity 독립 실행 빌드가 마지막 해상도 · 모드를 스스로 기억하고,
    ///   시연 때 쓰는 -screen-width 같은 실행 인자를 덮어쓰지 않기 위해서다. 그래서 화면 값은 늘 현재 Screen에서 읽는다.
    /// - 소리는 전체 음량만 AudioListener.volume으로 반영한다. 배경음/효과음/목소리/동료 소리는 값만 저장(소리 연결 전).
    /// </summary>
    public static class GameSettings
    {
        public static readonly int[] FrameCaps = { 30, 60, 120, 144, -1 };
        private static readonly float[] ShakeScales = { 0f, 0.5f, 1f };
        private static readonly float[] AllyScales = { 0.5f, 1f, 1.5f };

        private const string Prefix = "cm.settings.";
        private const int Version = 1;

        private static SettingsData saved;
        private static SettingsData active;
        private static bool loaded;
        private static bool focused = true;
        private static readonly List<Vector2Int> resolutions = new List<Vector2Int>(16);

        /// <summary>적용 중인 값이 바뀔 때(미리보기 포함). 커서 · HUD 등이 구독한다.</summary>
        public static event Action Changed;

        public static SettingsData Saved
        {
            get
            {
                EnsureLoaded();
                return saved;
            }
        }

        /// <summary>지금 화면에 반영된 값(설정창 미리보기 중이면 그 값).</summary>
        public static SettingsData Active
        {
            get
            {
                EnsureLoaded();
                return active;
            }
        }

        public static float ShakeScale => ShakeScales[Mathf.Clamp(Active.Shake, 0, ShakeScales.Length - 1)];
        public static bool DamageNumbers => Active.DamageNumbers;
        public static bool HitEffects => Active.HitEffects;
        public static float AllyVolumeScale => AllyScales[Mathf.Clamp(Active.AllyVolume, 0, AllyScales.Length - 1)];

        public static SettingsData Defaults()
        {
            return new SettingsData
            {
                ScreenMode = ModeToIndex(Screen.fullScreenMode),
                Width = Screen.width,
                Height = Screen.height,
                VSync = true,
                FrameCapIndex = 1,
                Shake = 2,
                Master = 1f,
                Bgm = 0.8f,
                Sfx = 0.8f,
                Voice = 0.8f,
                AllyVolume = 1,
                MuteInBackground = false,
                CursorSize = 0,
                ConfineCursor = false,
                DamageNumbers = true,
                HitEffects = true,
            };
        }

        public static void EnsureLoaded()
        {
            if (loaded)
            {
                return;
            }
            loaded = true;
            saved = Load();
            active = saved;
        }

        /// <summary>설정창에서 고치는 중인 값을 바로 반영(저장 X).</summary>
        public static void Preview(SettingsData data)
        {
            EnsureLoaded();
            Apply(data, active);
        }

        /// <summary>저장된 값으로 되돌린다(설정창을 저장 없이 닫을 때).</summary>
        public static void Revert()
        {
            EnsureLoaded();
            Apply(saved, active);
        }

        /// <summary>설정창을 열 때 - Alt+Enter · 창 크기 조절 등으로 바뀐 화면 상태를 값에 맞춘다.</summary>
        public static void SyncScreen()
        {
            EnsureLoaded();
            saved = CurrentScreen(saved);
            active = CurrentScreen(active);
        }

        public static void Save(SettingsData data)
        {
            EnsureLoaded();
            Apply(data, active);
            saved = data;
            Write(data);
        }

        /// <summary>해상도 목록(가로×세로 중복 제거, 작은 순). 처음 한 번만 만든다.</summary>
        public static IReadOnlyList<Vector2Int> Resolutions
        {
            get
            {
                if (resolutions.Count == 0)
                {
                    Resolution[] all = Screen.resolutions;
                    for (int i = 0; i < all.Length; i++)
                    {
                        var size = new Vector2Int(all[i].width, all[i].height);
                        if (!resolutions.Contains(size))
                        {
                            resolutions.Add(size);
                        }
                    }
                    var now = new Vector2Int(Screen.width, Screen.height);
                    if (!resolutions.Contains(now))
                    {
                        resolutions.Add(now);
                    }
                    resolutions.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
                }
                return resolutions;
            }
        }

        public static FullScreenMode IndexToMode(int index)
        {
            switch (index)
            {
                case 0: return FullScreenMode.ExclusiveFullScreen;
                case 1: return FullScreenMode.FullScreenWindow;
                default: return FullScreenMode.Windowed;
            }
        }

        public static int ModeToIndex(FullScreenMode mode)
        {
            switch (mode)
            {
                case FullScreenMode.ExclusiveFullScreen: return 0;
                case FullScreenMode.FullScreenWindow:
                case FullScreenMode.MaximizedWindow: return 1;
                default: return 2;
            }
        }

        private static SettingsData Load()
        {
            SettingsData d = Defaults();
            if (PlayerPrefs.GetInt(Prefix + "version", 0) != Version)
            {
                return d;
            }
            d.VSync = PlayerPrefs.GetInt(Prefix + "vsync", d.VSync ? 1 : 0) != 0;
            d.FrameCapIndex = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "frameCap", d.FrameCapIndex), 0, FrameCaps.Length - 1);
            d.Shake = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "shake", d.Shake), 0, ShakeScales.Length - 1);
            d.Master = Mathf.Clamp01(PlayerPrefs.GetFloat(Prefix + "master", d.Master));
            d.Bgm = Mathf.Clamp01(PlayerPrefs.GetFloat(Prefix + "bgm", d.Bgm));
            d.Sfx = Mathf.Clamp01(PlayerPrefs.GetFloat(Prefix + "sfx", d.Sfx));
            d.Voice = Mathf.Clamp01(PlayerPrefs.GetFloat(Prefix + "voice", d.Voice));
            d.AllyVolume = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "ally", d.AllyVolume), 0, AllyScales.Length - 1);
            d.MuteInBackground = PlayerPrefs.GetInt(Prefix + "muteBg", d.MuteInBackground ? 1 : 0) != 0;
            d.CursorSize = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "cursorSize", d.CursorSize), 0, 1);
            d.ConfineCursor = PlayerPrefs.GetInt(Prefix + "confine", d.ConfineCursor ? 1 : 0) != 0;
            d.DamageNumbers = PlayerPrefs.GetInt(Prefix + "dmgNum", d.DamageNumbers ? 1 : 0) != 0;
            d.HitEffects = PlayerPrefs.GetInt(Prefix + "hitFx", d.HitEffects ? 1 : 0) != 0;
            return d;
        }

        private static void Write(SettingsData d)
        {
            PlayerPrefs.SetInt(Prefix + "version", Version);
            PlayerPrefs.SetInt(Prefix + "vsync", d.VSync ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "frameCap", d.FrameCapIndex);
            PlayerPrefs.SetInt(Prefix + "shake", d.Shake);
            PlayerPrefs.SetFloat(Prefix + "master", d.Master);
            PlayerPrefs.SetFloat(Prefix + "bgm", d.Bgm);
            PlayerPrefs.SetFloat(Prefix + "sfx", d.Sfx);
            PlayerPrefs.SetFloat(Prefix + "voice", d.Voice);
            PlayerPrefs.SetInt(Prefix + "ally", d.AllyVolume);
            PlayerPrefs.SetInt(Prefix + "muteBg", d.MuteInBackground ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "cursorSize", d.CursorSize);
            PlayerPrefs.SetInt(Prefix + "confine", d.ConfineCursor ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "dmgNum", d.DamageNumbers ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "hitFx", d.HitEffects ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>화면 값은 저장본 대신 지금 Screen 상태를 쓴다(실행 인자 · OS 창 조절 반영).</summary>
        private static SettingsData CurrentScreen(SettingsData d)
        {
            d.ScreenMode = ModeToIndex(Screen.fullScreenMode);
            d.Width = Screen.width;
            d.Height = Screen.height;
            return d;
        }

        private static void Apply(SettingsData next, SettingsData previous)
        {
            active = next;

            if (next.ScreenMode != previous.ScreenMode || next.Width != previous.Width || next.Height != previous.Height)
            {
                FullScreenMode mode = IndexToMode(next.ScreenMode);
                if (mode != Screen.fullScreenMode || next.Width != Screen.width || next.Height != Screen.height)
                {
                    Screen.SetResolution(next.Width, next.Height, mode);
                }
            }

            QualitySettings.vSyncCount = next.VSync ? 1 : 0;
            // vSyncCount가 0이 아니면 독립 실행 빌드에서 targetFrameRate는 무시된다(Unity 문서).
            Application.targetFrameRate = FrameCaps[Mathf.Clamp(next.FrameCapIndex, 0, FrameCaps.Length - 1)];

            ApplyVolume();
            ApplyCursorConfine();
            Changed?.Invoke();
        }

        internal static void ResetStatics()
        {
            loaded = false;
            focused = true;
            Changed = null;
            resolutions.Clear();
        }

        internal static void ApplyStartup()
        {
            EnsureLoaded();
            active = CurrentScreen(saved);
            saved = CurrentScreen(saved);
            QualitySettings.vSyncCount = active.VSync ? 1 : 0;
            Application.targetFrameRate = FrameCaps[Mathf.Clamp(active.FrameCapIndex, 0, FrameCaps.Length - 1)];
            ApplyVolume();
            ApplyCursorConfine();
            Changed?.Invoke();
        }

        internal static void SetFocused(bool hasFocus)
        {
            focused = hasFocus;
            ApplyVolume();
            ApplyCursorConfine();
        }

        private static void ApplyVolume()
        {
            AudioListener.volume = active.MuteInBackground && !focused ? 0f : active.Master;
        }

        private static void ApplyCursorConfine()
        {
            Cursor.lockState = active.ConfineCursor && focused ? CursorLockMode.Confined : CursorLockMode.None;
        }
    }

    /// <summary>
    /// 261115 - 시작할 때 설정을 적용하고, 창 포커스(배경 음소거 · 커서 가두기)를 받아 넘기는 숨은 오브젝트.
    /// </summary>
    internal sealed class GameSettingsRunner : MonoBehaviour
    {
        private static GameSettingsRunner instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            GameSettings.ResetStatics();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (instance != null)
            {
                return;
            }
            var go = new GameObject("[GameSettings]");
            go.hideFlags = HideFlags.HideInHierarchy;
            DontDestroyOnLoad(go);
            instance = go.AddComponent<GameSettingsRunner>();
            GameSettings.ApplyStartup();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            GameSettings.SetFocused(hasFocus);
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }
    }
}
