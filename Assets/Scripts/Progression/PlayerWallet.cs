using System;
using UnityEngine;

namespace CheonMyeong.Progression
{
    /// <summary>
    /// 261027 추가 - 판 밖 엽전(기획서 5-8: 판이 끝나면 받은 엽전으로 스킬트리/캐릭터 해금). 판 안의 엽전
    /// (PlayerProgress.gold, NetworkVariable)과 달리 각자 자기 PC에만 저장되는 로컬 지갑이다 - 엽전은 "각자 따로
    /// 받음"이라 동기화할 상대가 없다. 저장은 PlayerPrefs(프로토타입 단계 - 세이브 파일 체계가 생기면 이 클래스
    /// 내부만 바꾸면 된다).
    ///
    /// 입금 시점: WaveManager.FinishRun(봉인 성공/실패 둘 다)에서 서버가 플레이어마다 이번 판 엽전(runGold, 클리어
    /// 보상 포함)을 그 플레이어 소유자에게 Rpc로 보낸다(PlayerProgress.DepositRunGoldRpc).
    /// </summary>
    public static class PlayerWallet
    {
        private const string YeopjeonPrefsKey = "CheonMyeong.Wallet.Yeopjeon";

        private static int cachedYeopjeon = -1;

        /// <summary>엽전이 바뀔 때(현재 값). 구독한 쪽은 반드시 OnDisable/OnDestroy에서 해제할 것.</summary>
        public static event Action<int> YeopjeonChanged;

        public static int Yeopjeon
        {
            get
            {
                if (cachedYeopjeon < 0)
                {
                    cachedYeopjeon = Mathf.Max(0, PlayerPrefs.GetInt(YeopjeonPrefsKey, 0));
                }
                return cachedYeopjeon;
            }
        }

        public static void AddYeopjeon(int amount)
        {
            if (amount <= 0)
            {
                return;
            }
            SetYeopjeon(Yeopjeon + amount);
        }

        /// <summary>해금/노드 구매용 - 모자라면 false를 돌려주고 아무것도 안 바꾼다.</summary>
        public static bool TrySpendYeopjeon(int amount)
        {
            if (amount < 0 || Yeopjeon < amount)
            {
                return false;
            }
            SetYeopjeon(Yeopjeon - amount);
            return true;
        }

        private static void SetYeopjeon(int value)
        {
            cachedYeopjeon = Mathf.Max(0, value);
            PlayerPrefs.SetInt(YeopjeonPrefsKey, cachedYeopjeon);
            PlayerPrefs.Save();
            YeopjeonChanged?.Invoke(cachedYeopjeon);
        }

        // Enter Play Mode 옵션으로 도메인 리로드를 끈 경우에도 이전 플레이의 캐시/구독이 남지 않게 초기화.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            cachedYeopjeon = -1;
            YeopjeonChanged = null;
        }
    }
}
