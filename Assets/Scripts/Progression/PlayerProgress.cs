using CheonMyeong.UI.Screens;
using Unity.Netcode;
using UnityEngine;

namespace CheonMyeong.Progression
{
    /// <summary>
    /// 플레이어 1명의 재화(엽전) - PlayerHealth와 같은 플레이어 프리팹에 같이 붙는다.
    /// REWARD 화면 와이어프레임 캡션("엽전은 각자 따로 받음")에 따라 플레이어별로 분리된
    /// NetworkVariable이다. 레벨/경험치는 반대로 "영혼 공유"(INGAME HUD 캡션)라 여기 두지
    /// 않고 PartyProgress(씬 전역, 파티 공유)에 둔다.
    ///
    /// 로비 화면(LobbyScreen)에 표시할 "여러 판에 걸쳐 누적되는" 엽전은 세이브/로드 체계가
    /// 아직 기획/코드 어디에도 없어서(확인 필요 - 보류) 다루지 않는다. 이 값은 어디까지나
    /// "이번 판에서 주운 양"만 추적한다.
    /// </summary>
    public class PlayerProgress : NetworkBehaviour
    {
        private readonly NetworkVariable<int> gold = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public int Gold => gold.Value;

        public override void OnNetworkSpawn()
        {
            gold.OnValueChanged += HandleGoldChanged;

            if (IsOwner)
            {
                RefreshHud(gold.Value);
            }
        }

        public override void OnNetworkDespawn()
        {
            gold.OnValueChanged -= HandleGoldChanged;
        }

        /// <summary>서버 전용 - LootPickup(Gold)이 수거될 때 호출한다.</summary>
        public void AddGold(int amount)
        {
            if (!IsServer || amount <= 0)
            {
                return;
            }
            gold.Value += amount;
        }

        private void HandleGoldChanged(int previous, int current)
        {
            // HUD의 엽전 칸은 "내 화면 = 내 소지금" 하나뿐이라(상대방 엽전을 보여주는 칸은
            // wireframe INGAME HUD에 없음) 소유자 클라이언트에서만 갱신한다.
            if (IsOwner)
            {
                RefreshHud(current);
            }
        }

        private static void RefreshHud(int amount)
        {
            if (InGameHudScreen.Instance != null)
            {
                InGameHudScreen.Instance.SetGold(amount);
            }
        }
    }
}
