using CheonMyeong.Progression;
using UnityEditor;

namespace CheonMyeong.EditorTools
{
    /// <summary>
    /// 261114 테스트용 - 엽전 증감. 플레이 중이 아니어도 PlayerPrefs에 바로 반영된다. 단축키는 게임 화면에서 F4(+) / F3(-)와 같다.
    /// </summary>
    public static class WalletDebugMenu
    {
        [MenuItem("CheonMyeong/Debug/엽전 +1,000 (F4)")]
        private static void Add() => DevYeopjeonCheat.Apply(DevYeopjeonCheat.Step);

        [MenuItem("CheonMyeong/Debug/엽전 -1,000 (F3)")]
        private static void Subtract() => DevYeopjeonCheat.Apply(-DevYeopjeonCheat.Step);

        [MenuItem("CheonMyeong/Debug/엽전 +10,000 (Shift+F4)")]
        private static void AddMany() => DevYeopjeonCheat.Apply(DevYeopjeonCheat.Step * 10);

        [MenuItem("CheonMyeong/Debug/신내림 초기화 - 보고 있는 · 쓰는 캐릭터 (F8)")]
        private static void ResetSinnaerim() => DevYeopjeonCheat.ResetSinnaerim();
    }
}
