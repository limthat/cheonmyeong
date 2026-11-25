using UnityEngine;

namespace CheonMyeong.Data
{
    /// <summary>
    /// 261125 - 스토리 저장소 열람 기록(각자 자기 PC, PlayerPrefs). 맵을 한 번이라도 클리어하면 그 화가 열린다.
    /// 지금은 1화(달밤의 폐장터 · 억쇠) 하나뿐이라 화 번호 = 슬롯 번호(0부터).
    /// </summary>
    public static class StoryProgressStore
    {
        private const string ClearedKeyPrefix = "CheonMyeong.Story.Cleared.";

        public const int EpisodeEoksoe = 0;

        public static bool IsCleared(int episode)
        {
            return PlayerPrefs.GetInt(ClearedKeyPrefix + episode, 0) == 1;
        }

        public static void MarkCleared(int episode)
        {
            if (IsCleared(episode))
            {
                return;
            }
            PlayerPrefs.SetInt(ClearedKeyPrefix + episode, 1);
            PlayerPrefs.Save();
        }
    }
}
