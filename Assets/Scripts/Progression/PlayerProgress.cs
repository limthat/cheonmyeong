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
    /// 로비 화면(LobbyScreen)에 표시할 "여러 판에 걸쳐 누적되는" 엽전은 PlayerWallet(로컬 저장)이
    /// 담당한다 - 판이 끝날 때 DepositRunGoldRpc로 이번 판 엽전이 거기에 입금된다.
    /// </summary>
    public partial class PlayerProgress : NetworkBehaviour
    {
        private readonly NetworkVariable<int> gold = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // 261027 추가 - 결과 화면용 "이번 판" 기록(기획서 7: 결과 화면 "각자 받은 엽전"). gold는 판이 바뀌어도
        // 이어지는 소지금이고, 이 둘은 새 판 시작 때(WaveManager.BeginPreparation) 0으로 돌아간다.
        private readonly NetworkVariable<int> runGold = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> runKills = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // 261101 - 이번 판 엽전 중 이미 로비 지갑(PlayerWallet)에 넣은 양(소유자 로컬). 판 종료 입금(DepositRunGoldRpc)과
        // 중도 이탈 입금(SettleUnpaidRunGold)이 겹쳐도 같은 엽전을 두 번 넣지 않도록 "차액만" 입금한다.
        private int settledRunGold;
        private bool ownedLocally;

        // 261101 추가 - 기획서 9장: 5웨이브 억쇠를 시간 안에 쫓아내면 각자 리롤권 1장. 261104 - 신통력 선택 화면의
        // 리롤 버튼이 1장씩 쓴다(ServerTryReroll).
        private readonly NetworkVariable<int> rerollTickets = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public int RerollTickets => rerollTickets.Value;

        /// <summary>서버 전용 - 리롤권 추가.</summary>
        public void ServerAddRerollTickets(int amount)
        {
            if (IsServer && amount > 0)
            {
                rerollTickets.Value += amount;
            }
        }

        public int Gold => gold.Value;
        public int RunGold => runGold.Value;
        public int RunKills => runKills.Value;

        /// <summary>서버 전용 - 이 플레이어의 공격으로 원귀가 쓰러졌을 때(PlayerAutoAttack).</summary>
        public void ServerAddKill()
        {
            if (IsServer)
            {
                runKills.Value++;
            }
        }

        /// <summary>서버 전용 - 새 판 시작 때 이번 판 기록 초기화.</summary>
        public void ServerResetRunStats()
        {
            if (!IsServer)
            {
                return;
            }
            runGold.Value = 0;
            runKills.Value = 0;
            rerollTickets.Value = 0;
            ServerResetAugments();
        }

        public override void OnNetworkSpawn()
        {
            gold.OnValueChanged += HandleGoldChanged;
            runGold.OnValueChanged += HandleRunGoldChanged;
            ownedLocally = IsOwner;
            settledRunGold = 0;
            AugmentsOnNetworkSpawn();

            if (IsOwner)
            {
                RefreshHud(gold.Value);
            }
        }

        public override void OnNetworkDespawn()
        {
            // 기획서 v0.9.3 "연결 끊김 - 나간 사람도 그때까지 주운 엽전은 받는다". 세션 종료/접속 끊김으로 내 플레이어가
            // 사라질 때 아직 안 넣은 이번 판 엽전을 넣는다.
            if (ownedLocally)
            {
                SettleUnpaidRunGold();
            }
            gold.OnValueChanged -= HandleGoldChanged;
            runGold.OnValueChanged -= HandleRunGoldChanged;
            AugmentsOnNetworkDespawn();
        }

        /// <summary>
        /// 261101 추가 - 소유자 로컬. 이번 판에 주운 엽전 중 아직 로비 지갑에 안 넣은 만큼 넣는다.
        /// ESC "나가기"(예섭 확정: 중간에 나가도 지급)와 접속 끊김에서 부른다.
        /// </summary>
        public void SettleUnpaidRunGold()
        {
            if (!ownedLocally)
            {
                return;
            }
            int unpaid = runGold.Value - settledRunGold;
            if (unpaid > 0)
            {
                PlayerWallet.AddYeopjeon(unpaid);
                settledRunGold = runGold.Value;
            }
        }

        /// <summary>261101 추가 - 이 PC의 내 플레이어를 찾아 SettleUnpaidRunGold를 부른다. 없으면 아무것도 안 함.</summary>
        public static void SettleLocalPlayerRunGold()
        {
            NetworkManager manager = NetworkManager.Singleton;
            NetworkObject playerObject = manager != null && manager.IsListening && manager.LocalClient != null
                ? manager.LocalClient.PlayerObject
                : null;
            if (playerObject != null && playerObject.TryGetComponent(out PlayerProgress progress))
            {
                progress.SettleUnpaidRunGold();
            }
        }

        private void HandleRunGoldChanged(int previous, int current)
        {
            // 새 판 시작(ServerResetRunStats)으로 0이 되면 입금 기록도 처음부터.
            if (current == 0)
            {
                settledRunGold = 0;
            }
        }

        /// <summary>서버 전용 - LootPickup(Gold)이 수거될 때 호출한다.</summary>
        public void AddGold(int amount)
        {
            if (!IsServer || amount <= 0)
            {
                return;
            }
            gold.Value += amount;
            runGold.Value += amount;
        }

        /// <summary>
        /// 261027 추가 - 판이 끝났을 때 서버가 이번 판 엽전을 이 플레이어 소유자에게 보낸다(WaveManager.FinishRun).
        /// 금액을 파라미터로 넘겨서, runGold NetworkVariable 갱신과 이 신호의 도착 순서에 상관없이 정확한 값이 입금된다.
        /// </summary>
        [Rpc(SendTo.Owner)]
        public void DepositRunGoldRpc(int amount)
        {
            int unpaid = amount - settledRunGold;
            if (unpaid > 0)
            {
                PlayerWallet.AddYeopjeon(unpaid);
            }
            settledRunGold = Mathf.Max(settledRunGold, amount);
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
