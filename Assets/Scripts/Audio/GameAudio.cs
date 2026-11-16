using UnityEngine;

namespace CheonMyeong.Audio
{
    /// <summary>
    /// 261115 - 어디서든 부르는 소리 창구. SoundManager가 없거나 소리가 아직 연결 안 됐으면 아무 일도 안 한다(널 체크 불필요).
    /// 예) GameAudio.Play(SoundKeys.WaveGong); GameAudio.PlayAt(SoundKeys.Hit, hitPosition); GameAudio.PlayBgm(SoundKeys.BgmBoss);
    /// </summary>
    public static class GameAudio
    {
        public static SoundHandle Play(string key)
        {
            SoundManager m = SoundManager.Instance;
            return m != null ? m.Play(key) : SoundHandle.None;
        }

        /// <summary>위치가 있는 소리. fromAlly = 동료가 낸 소리(설정 '동료 소리 크기' 적용).</summary>
        public static SoundHandle PlayAt(string key, Vector2 position, bool fromAlly = false)
        {
            SoundManager m = SoundManager.Instance;
            return m != null ? m.Play(key, position, fromAlly) : SoundHandle.None;
        }

        public static void Stop(SoundHandle handle)
        {
            SoundManager m = SoundManager.Instance;
            if (m != null)
            {
                m.Stop(handle);
            }
        }

        public static void PlayBgm(string key, float fade = -1f)
        {
            SoundManager m = SoundManager.Instance;
            if (m != null)
            {
                m.PlayBgm(key, fade);
            }
        }

        public static void StopBgm(float fade = -1f)
        {
            SoundManager m = SoundManager.Instance;
            if (m != null)
            {
                m.StopBgm(fade);
            }
        }

        public static void SetBgmIntensity(int layers)
        {
            SoundManager m = SoundManager.Instance;
            if (m != null)
            {
                m.SetBgmIntensity(layers);
            }
        }
    }
}
