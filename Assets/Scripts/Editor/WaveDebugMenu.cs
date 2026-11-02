using CheonMyeong.Waves;
using UnityEditor;
using UnityEngine;

namespace CheonMyeong.EditorTools
{
    /// <summary>
    /// 261101 테스트용 - 플레이 중(호스트)에 웨이브 건너뛰기. 단축키는 게임 화면에서 F5 / F10 / F11과 같다.
    /// </summary>
    public static class WaveDebugMenu
    {
        [MenuItem("CheonMyeong/Debug/Skip To Wave 5 (F5)")]
        private static void SkipToWave5() => Skip(5);

        [MenuItem("CheonMyeong/Debug/Skip To Wave 10 (F10)")]
        private static void SkipToWave10() => Skip(10);

        [MenuItem("CheonMyeong/Debug/Skip To Final Battle (F11)")]
        private static void SkipToFinal() => Skip(WaveManager.Instance.TotalWaves + 1);

        [MenuItem("CheonMyeong/Debug/Skip To Wave 5 (F5)", true)]
        [MenuItem("CheonMyeong/Debug/Skip To Wave 10 (F10)", true)]
        [MenuItem("CheonMyeong/Debug/Skip To Final Battle (F11)", true)]
        private static bool CanSkip()
        {
            return Application.isPlaying && WaveManager.Instance != null && WaveManager.Instance.IsServer;
        }

        private static void Skip(int waveNumber)
        {
            WaveManager.Instance.ServerDebugSkipToWave(waveNumber);
        }
    }
}
