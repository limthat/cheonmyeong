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

        // 261114 - 화면 멈춤: 누구든 인게임 화면이 아닌 곳(대기실 · 로딩 · 스토리 컷신 등)에 있으면 판 시간이 흐르지 않는다.
        // 각자 자기 화면 상태를 서버에 알리고, 서버가 접속자 전원이 인게임 화면일 때만 푼다(아직 안 알린 사람 = 멈춤).
        private readonly NetworkVariable<bool> screenPaused = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly Dictionary<ulong, bool> clientsInGameView = new Dictionary<ulong, bool>();
        private bool hasReportedView;
        private bool lastReportedInGameView;
        private PauseVoteView pauseVoteView;

        // 261115 - 동료가 나갔을 때(서버) 다음 Update에서 투표 · 화면 멈춤 · 신통력 선택을 남은 사람 기준으로 다시 본다.
        // 끊김 콜백 시점엔 ConnectedClients에 아직 남아 있을 수 있어서 한 프레임 미룬다.
        private NetworkManager rosterManager;
        private bool rosterDirty;

        /// <summary>투표가 끝났을 때(모든 화면). true = 일시정지됨, false = 거절 · 시간 초과.</summary>
        public static event Action<bool> PauseVoteFinished;

        public bool IsMenuPaused => menuPaused.Value;
        public bool IsScreenPaused => screenPaused.Value;
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
            clientsInGameView.Clear();
            hasReportedView = false;
            if (menuPaused.Value)
            {
                HandleMenuPausedChanged(false, true);
            }
            if (IsServer)
            {
                rosterManager = NetworkManager;
                rosterManager.OnClientDisconnectCallback += HandleRosterClientDisconnected;
            }
        }

        private void HandleRosterClientDisconnected(ulong clientId)
        {
            rosterDirty = true;
        }

        /// <summary>서버 - 나간 사람을 투표 · 화면 상태에서 빼고, 남은 사람만으로 결정을 다시 내린다.</summary>
        private void ServerRefreshRoster()
        {
            rosterDirty = false;
            var connected = NetworkManager.ConnectedClients;
            pauseVoteYes.RemoveWhere(id => !connected.ContainsKey(id));
            staleViewIds.Clear();
            foreach (ulong id in clientsInGameView.Keys)
            {
                if (!connected.ContainsKey(id))
                {
                    staleViewIds.Add(id);
                }
            }
            for (int i = 0; i < staleViewIds.Count; i++)
            {
                clientsInGameView.Remove(staleViewIds[i]);
            }

            if (IsPauseVoteActive)
            {
                if (!connected.ContainsKey(pauseVoteRequester.Value))
                {
                    ServerFinishPauseVote(false);
                }
                else
                {
                    // 요청한 사람만 남았으면 그대로 멈춘다(혼자면 바로 멈추는 규칙과 같게).
                    ServerAcceptPauseVote(pauseVoteRequester.Value);
                }
            }
            if (choosing)
            {
                ServerCheckAllPicked();
            }
        }

        private readonly List<ulong> staleViewIds = new List<ulong>(4);

        private void PauseOnNetworkDespawn()
        {
            menuPaused.OnValueChanged -= HandleMenuPausedChanged;
            if (rosterManager != null)
            {
                rosterManager.OnClientDisconnectCallback -= HandleRosterClientDisconnected;
                rosterManager = null;
            }
            rosterDirty = false;
            clientsInGameView.Clear();
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
            ScreenPauseUpdate();
        }

        private void ScreenPauseUpdate()
        {
            if (!IsSpawned)
            {
                return;
            }
            UIFlowManager flow = UIFlowManager.Instance;
            bool inGameView = flow != null && flow.CurrentScreen == UIFlowManager.InGame;
            if (!hasReportedView || inGameView != lastReportedInGameView)
            {
                hasReportedView = true;
                lastReportedInGameView = inGameView;
                ReportInGameViewRpc(inGameView);
            }
            if (IsServer)
            {
                ServerRefreshScreenPause();
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ReportInGameViewRpc(bool inGameView, RpcParams rpcParams = default)
        {
            clientsInGameView[rpcParams.Receive.SenderClientId] = inGameView;
        }

        private void ServerRefreshScreenPause()
        {
            bool paused = false;
            IReadOnlyList<NetworkClient> clients = NetworkManager.ConnectedClientsList;
            for (int i = 0; i < clients.Count; i++)
            {
                if (!clientsInGameView.TryGetValue(clients[i].ClientId, out bool inGameView) || !inGameView)
                {
                    paused = true;
                    break;
                }
            }
            if (screenPaused.Value != paused)
            {
                screenPaused.Value = paused;
            }
        }

        private void PauseUpdate()
        {
            if (IsSpawned && pauseVoteView == null && InGameHudScreen.Instance != null)
            {
                pauseVoteView = PauseVoteView.Create(InGameHudScreen.Instance.transform);
            }

            if (IsServer && IsSpawned && rosterDirty)
            {
                ServerRefreshRoster();
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
