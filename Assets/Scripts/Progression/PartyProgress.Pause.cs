using System;
using System.Collections.Generic;
using CheonMyeong.UI;
using CheonMyeong.UI.Screens;
using Unity.Netcode;
using UnityEngine;

namespace CheonMyeong.Progression
{
    /// <summary>
    /// 261110 추가 - 인게임 ESC 일시정지(투표).
    /// - 혼자면 ESC를 누르는 즉시 멈추고 ESC 메뉴가 열린다.
    /// - 둘이면 누른 사람이 일시정지를 요청하고, 동료가 수락하면 둘 다 멈추고 ESC 메뉴가 열린다. 거절 · 시간 초과면 그대로 진행.
    /// - 멈춘 뒤엔 누구든 "계속하기"(또는 ESC)로 풀 수 있다.
    /// 판정은 서버, 멈춤 상태(menuPaused)는 모두에게 복제되고 IsPaused에 합쳐져서 기존 일시정지 처리(이동 · 공격 · 원귀 · 웨이브)가 그대로 멈춘다.
    /// 신통력 고르는 중(이미 멈춤)에는 요청을 받지 않는다.
    /// </summary>
    public partial class PartyProgress
    {
        private const ulong NoPauseVote = ulong.MaxValue;

        [Header("일시정지 투표 (261110)")]
        [Tooltip("동료가 응답할 시간(초). 지나면 거절로 본다.")]
        [Min(1f)] [SerializeField] private float pauseVoteSeconds = 10f;

        private readonly NetworkVariable<bool> menuPaused = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<ulong> pauseVoteRequester = new NetworkVariable<ulong>(
            NoPauseVote, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<double> pauseVoteDeadline = new NetworkVariable<double>(
            0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly HashSet<ulong> pauseVoteYes = new HashSet<ulong>();
        private PauseVoteView pauseVoteView;

        /// <summary>투표가 끝났을 때(모든 화면). true = 일시정지됨, false = 거절 · 시간 초과.</summary>
        public static event Action<bool> PauseVoteFinished;

        public bool IsMenuPaused => menuPaused.Value;
        public bool IsPauseVoteActive => pauseVoteRequester.Value != NoPauseVote;
        public ulong PauseVoteRequester => pauseVoteRequester.Value;

        public float PauseVoteSecondsLeft
        {
            get
            {
                if (!IsPauseVoteActive || NetworkManager == null)
                {
                    return 0f;
                }
                return Mathf.Max(0f, (float)(pauseVoteDeadline.Value - NetworkManager.ServerTime.Time));
            }
        }

        private void PauseOnNetworkSpawn()
        {
            menuPaused.OnValueChanged += HandleMenuPausedChanged;
            pauseVoteYes.Clear();
            if (menuPaused.Value)
            {
                HandleMenuPausedChanged(false, true);
            }
        }

        private void PauseOnNetworkDespawn()
        {
            menuPaused.OnValueChanged -= HandleMenuPausedChanged;
            if (pauseVoteView != null)
            {
                Destroy(pauseVoteView.gameObject);
                pauseVoteView = null;
            }
        }

        private void Update()
        {
            AugmentChoiceUpdate();
            PauseUpdate();
        }

        private void PauseUpdate()
        {
            if (IsSpawned && pauseVoteView == null && InGameHudScreen.Instance != null)
            {
                pauseVoteView = PauseVoteView.Create(InGameHudScreen.Instance.transform);
            }

            if (!IsServer || !IsSpawned || !IsPauseVoteActive)
            {
                return;
            }
            bool requesterGone = !NetworkManager.ConnectedClients.ContainsKey(pauseVoteRequester.Value);
            if (requesterGone || NetworkManager.ServerTime.Time >= pauseVoteDeadline.Value)
            {
                ServerFinishPauseVote(false);
            }
        }

        private static void HandleMenuPausedChanged(bool previous, bool current)
        {
            UIFlowManager flow = UIFlowManager.Instance;
            if (flow == null)
            {
                return;
            }
            if (current)
            {
                if (!flow.IsOverlayOpen(UIFlowManager.EscMenu))
                {
                    flow.OpenOverlay(UIFlowManager.EscMenu);
                }
                return;
            }
            if (flow.IsOverlayOpen(UIFlowManager.Settings))
            {
                flow.CloseOverlay(UIFlowManager.Settings);
            }
            flow.CloseOverlay(UIFlowManager.EscMenu);
        }

        /// <summary>ESC - 일시정지 요청. 혼자면 바로 멈춘다. 동료가 요청 중이면 수락으로 친다.</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestPauseRpc(RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (menuPaused.Value || choosing)
            {
                return;
            }
            if (IsPauseVoteActive)
            {
                if (sender != pauseVoteRequester.Value)
                {
                    ServerAcceptPauseVote(sender);
                }
                return;
            }
            if (NetworkManager.ConnectedClientsList.Count <= 1)
            {
                menuPaused.Value = true;
                return;
            }
            pauseVoteYes.Clear();
            pauseVoteYes.Add(sender);
            pauseVoteDeadline.Value = NetworkManager.ServerTime.Time + pauseVoteSeconds;
            pauseVoteRequester.Value = sender;
        }

        /// <summary>동료의 일시정지 요청에 답한다.</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void AnswerPauseVoteRpc(bool accept, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (!IsPauseVoteActive || sender == pauseVoteRequester.Value)
            {
                return;
            }
            if (accept)
            {
                ServerAcceptPauseVote(sender);
            }
            else
            {
                ServerFinishPauseVote(false);
            }
        }

        /// <summary>일시정지를 푼다(누구든). ESC 메뉴의 계속하기 · ESC 키 · 나가기.</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestMenuResumeRpc(RpcParams rpcParams = default)
        {
            menuPaused.Value = false;
        }

        /// <summary>서버 전용 - 새 판 시작 때 일시정지 · 투표를 정리한다.</summary>
        public void ServerClearMenuPause()
        {
            if (!IsServer)
            {
                return;
            }
            pauseVoteYes.Clear();
            pauseVoteRequester.Value = NoPauseVote;
            menuPaused.Value = false;
        }

        private void ServerAcceptPauseVote(ulong clientId)
        {
            pauseVoteYes.Add(clientId);
            foreach (NetworkClient client in NetworkManager.ConnectedClientsList)
            {
                if (!pauseVoteYes.Contains(client.ClientId))
                {
                    return;
                }
            }
            ServerFinishPauseVote(true);
        }

        private void ServerFinishPauseVote(bool passed)
        {
            pauseVoteYes.Clear();
            pauseVoteRequester.Value = NoPauseVote;
            if (passed && !choosing)
            {
                menuPaused.Value = true;
            }
            PauseVoteResultRpc(passed);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PauseVoteResultRpc(bool passed)
        {
            PauseVoteFinished?.Invoke(passed);
        }

        /// <summary>
        /// 이 PC의 ESC 처리(인게임, 위에 열린 창이 없을 때). 동료 요청이 떠 있으면 수락, 아니면 요청.
        /// </summary>
        public void LocalPressPause()
        {
            if (!IsSpawned)
            {
                return;
            }
            if (IsPauseVoteActive && NetworkManager.LocalClientId != pauseVoteRequester.Value)
            {
                AnswerPauseVoteRpc(true);
                return;
            }
            if (!IsPauseVoteActive)
            {
                RequestPauseRpc();
            }
        }
    }
}
