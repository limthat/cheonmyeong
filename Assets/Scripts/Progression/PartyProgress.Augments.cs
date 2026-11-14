using CheonMyeong.UI;
using Unity.Netcode;
using UnityEngine;

namespace CheonMyeong.Progression
{
    /// <summary>
    /// 261104 추가 - 레벨업 신통력 고르기 진행(기획서 v0.9.4 07장).
    /// - 레벨업하면 게임이 멈추고 각자 카드 3장 중 1장을 고른다. 둘 다 고르면 재개.
    /// - 제한 10초 - 지나면 아직 안 고른 사람은 무작위로 골라진다.
    /// - 한 번에 여러 레벨이 오르면 레벨마다 한 라운드씩 이어서 고른다(멈춘 채로).
    /// - 리롤권 1장으로 내 카드 3장을 다시 뽑는다.
    /// 서버가 진행하고, 화면은 라운드 번호(choiceRound)가 바뀌면 신통력 화면을 연다.
    /// </summary>
    public partial class PartyProgress
    {
        [Header("신통력 (261104)")]
        [Tooltip("신통력 목록 · 규칙 수치. 비워두면 코드 기본값(기획서 v0.9.4)을 쓴다.")]
        [SerializeField] private AugmentCatalogSO augmentCatalog;

        private readonly NetworkVariable<int> choiceRound = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<double> choiceDeadline = new NetworkVariable<double>(
            0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private int pendingChoices;
        private double choiceHoldUntil;
        private bool choosing;

        public AugmentCatalogSO AugmentCatalog => augmentCatalog != null ? augmentCatalog : AugmentCatalogSO.Default;
        public int ChoiceRound => choiceRound.Value;

        /// <summary>이번 라운드 남은 시간(초). 고르는 중이 아니면 0.</summary>
        public float ChoiceSecondsLeft
        {
            get
            {
                if (!isPaused.Value || NetworkManager == null)
                {
                    return 0f;
                }
                return Mathf.Max(0f, (float)(choiceDeadline.Value - NetworkManager.ServerTime.Time));
            }
        }

        private void AugmentChoiceOnNetworkSpawn()
        {
            choiceRound.OnValueChanged += HandleChoiceRoundChanged;
            pendingChoices = 0;
            choosing = false;
            choiceHoldUntil = 0d;
        }

        /// <summary>
        /// 261110 서버 전용 - 웨이브 종료 흡수 연출 동안 신통력 화면을 미룬다. 그 사이 레벨업은 쌓아뒀다가
        /// 시간이 지나면 AugmentChoiceUpdate가 연다.
        /// </summary>
        public void ServerDelayAugmentChoices(float seconds)
        {
            if (!IsServer || !IsSpawned || seconds <= 0f)
            {
                return;
            }
            double until = NetworkManager.ServerTime.Time + seconds;
            if (until > choiceHoldUntil)
            {
                choiceHoldUntil = until;
            }
        }

        private void AugmentChoiceOnNetworkDespawn()
        {
            choiceRound.OnValueChanged -= HandleChoiceRoundChanged;
        }

        private void HandleChoiceRoundChanged(int previous, int current)
        {
            // 라운드가 이어지면(여러 레벨업) 화면은 이미 열려 있다 - 한 번만 연다.
            if (UIFlowManager.Instance != null && !UIFlowManager.Instance.IsOverlayOpen(UIFlowManager.Upgrade))
            {
                UIFlowManager.Instance.OpenOverlay(UIFlowManager.Upgrade);
            }
        }

        /// <summary>서버 전용 - 레벨업 수만큼 고르기를 예약한다(AddExp).</summary>
        private void ServerQueueAugmentChoices(int count)
        {
            if (!IsServer || count <= 0)
            {
                return;
            }
            pendingChoices += count;
            if (!choosing && NetworkManager.ServerTime.Time >= choiceHoldUntil)
            {
                ServerStartChoiceRound();
            }
        }

        private void ServerStartChoiceRound()
        {
            choosing = true;
            isPaused.Value = true;
            foreach (NetworkClient client in NetworkManager.ConnectedClientsList)
            {
                PlayerProgress progress = ProgressOf(client);
                if (progress != null)
                {
                    progress.ServerBeginChoice();
                }
            }
            choiceDeadline.Value = NetworkManager.ServerTime.Time + AugmentCatalog.choiceSeconds;
            choiceRound.Value++;
        }

        /// <summary>서버 - 10초가 지나면 아직 안 고른 사람은 무작위로. Update는 PartyProgress.Pause.cs에서 부른다.</summary>
        private void AugmentChoiceUpdate()
        {
            if (!IsServer || !IsSpawned)
            {
                return;
            }
            if (!choosing)
            {
                // 261110 - 미뤄둔 레벨업(흡수 연출 끝)
                if (pendingChoices > 0 && NetworkManager.ServerTime.Time >= choiceHoldUntil)
                {
                    ServerStartChoiceRound();
                }
                return;
            }
            if (NetworkManager.ServerTime.Time < choiceDeadline.Value)
            {
                return;
            }

            // 기획서: 10초가 지나면 무작위 선택.
            foreach (NetworkClient client in NetworkManager.ConnectedClientsList)
            {
                PlayerProgress progress = ProgressOf(client);
                if (progress != null && !progress.HasPicked)
                {
                    progress.ServerPickRandom();
                }
            }
            ServerFinishChoiceRound();
        }

        /// <summary>신통력 화면의 카드를 골랐을 때(각자). 고르는 순간 적용되고, 둘 다 고르면 재개.</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void PickAugmentRpc(int cardIndex, RpcParams rpcParams = default)
        {
            if (!choosing)
            {
                return;
            }
            PlayerProgress progress = ProgressOf(rpcParams.Receive.SenderClientId);
            if (progress == null || !progress.ServerPick(cardIndex))
            {
                return;
            }
            ServerCheckAllPicked();
        }

        /// <summary>리롤권 1장으로 내 카드를 다시 뽑는다.</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RerollAugmentsRpc(RpcParams rpcParams = default)
        {
            if (!choosing)
            {
                return;
            }
            PlayerProgress progress = ProgressOf(rpcParams.Receive.SenderClientId);
            if (progress != null)
            {
                progress.ServerTryReroll();
            }
        }

        /// <summary>261109 - 리롤권 1장으로 내 카드 한 장(cardIndex)만 다시 뽑는다(아트 카드 디자인: 카드마다 리롤 버튼).</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RerollAugmentCardRpc(int cardIndex, RpcParams rpcParams = default)
        {
            if (!choosing)
            {
                return;
            }
            PlayerProgress progress = ProgressOf(rpcParams.Receive.SenderClientId);
            if (progress != null)
            {
                progress.ServerTryRerollCard(cardIndex);
            }
        }

        private void ServerCheckAllPicked()
        {
            foreach (NetworkClient client in NetworkManager.ConnectedClientsList)
            {
                PlayerProgress progress = ProgressOf(client);
                if (progress != null && !progress.HasPicked)
                {
                    return;
                }
            }
            ServerFinishChoiceRound();
        }

        private void ServerFinishChoiceRound()
        {
            pendingChoices = Mathf.Max(0, pendingChoices - 1);
            if (pendingChoices > 0)
            {
                ServerStartChoiceRound();
                return;
            }
            choosing = false;
            isPaused.Value = false;
        }

        private PlayerProgress ProgressOf(ulong clientId)
        {
            return NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) ? ProgressOf(client) : null;
        }

        private static PlayerProgress ProgressOf(NetworkClient client)
        {
            return client != null && client.PlayerObject != null && client.PlayerObject.TryGetComponent(out PlayerProgress progress)
                ? progress
                : null;
        }
    }
}
