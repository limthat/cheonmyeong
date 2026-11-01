using System;
using CheonMyeong.Enemies;
using CheonMyeong.Maps;
using CheonMyeong.Player;
using CheonMyeong.Progression;
using CheonMyeong.UI;
using CheonMyeong.UI.Screens;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace CheonMyeong.Waves
{
    /// <summary>
    /// 261027 추가 - 한 판의 진행(준비 → 1웨이브 → … → 마지막 웨이브 → 클리어 / 전원 쓰러짐 → 실패).
    /// 숫자는 전부 WaveConfigSO에서 읽고 여기엔 진행 규칙만 있다(예섭: "하드코딩 말고 유연하게").
    ///
    /// 규칙 출처: [기획서] 천명_261013
    /// - 5-2 웨이브 종료(징): 남은 원귀는 원한으로 흡수, 바닥의 영혼 자동 흡수(공유), 엽전은 반씩,
    ///   두 사람 체력 조금 회복, 쓰러진 동료 부활.
    /// - 5-2 게임오버: 둘 다 쓰러지면 끝.
    /// - 5-3 원한: 남은 원귀 1마리 = 원한 1(누적).
    /// - 5-10 원귀 체력은 웨이브마다 증가.
    ///
    /// 서버 권위 - 타이머/전환은 서버만 계산하고 결과(단계/현재 웨이브/전체 수/남은 초)만
    /// NetworkVariable로 복제한다. 남은 시간은 "정수 초가 바뀔 때만" 써서 초당 1번만 전송된다.
    /// ProgressionSystems(PartyProgress가 붙은 Gameplay 씬의 in-scene NetworkObject)에 같이 붙는다.
    ///
    /// 억쇠(5웨이브 예행연습, 최종전)는 보스가 아직 없어서 빠져 있다 - 지금은 마지막 웨이브를
    /// 버티면 클리어로 처리한다(보스가 생기면 "최종전에서 억쇠 처치 = 클리어"로 교체).
    /// </summary>
    public class WaveManager : NetworkBehaviour
    {
        public static WaveManager Instance { get; private set; }

        public const int PhasePreparing = 0;
        public const int PhaseRunning = 1;
        public const int PhaseCleared = 2;
        public const int PhaseFailed = 3;

        [SerializeField] private WaveConfigSO config;

        private readonly NetworkVariable<int> phase = new NetworkVariable<int>(
            PhasePreparing, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>0 = 준비 중(첫 웨이브 전), 1부터 웨이브 번호.</summary>
        private readonly NetworkVariable<int> currentWave = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> totalWaves = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> remainingSeconds = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private float timer;
        private float currentDuration;
        private float runElapsed;

        // 261027 추가 - 결과 화면용(기획서 7: 결과 화면 "최종 원한, 각자 받은 엽전, 클리어 보상").
        private readonly NetworkVariable<int> finalRunSeconds = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> clearRewardPerPlayer = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>판이 끝났을 때 웨이브 진행 시간(준비 시간·일시정지 제외, 초).</summary>
        public int FinalRunSeconds => finalRunSeconds.Value;

        /// <summary>클리어 보상으로 각자 받은 엽전(실패면 0).</summary>
        public int ClearRewardPerPlayer => clearRewardPerPlayer.Value;

        /// <summary>서버에서만 발생. 인자 = 시작된 웨이브 번호(1부터).</summary>
        public event Action<int> ServerWaveStarted;

        /// <summary>서버에서만 발생. 인자 = 끝난 웨이브 번호(징이 울린 직후).</summary>
        public event Action<int> ServerWaveEnded;

        /// <summary>서버에서만 발생. 인자 = 클리어 여부.</summary>
        public event Action<bool> ServerRunFinished;

        public int Phase => phase.Value;
        public bool IsWaveRunning => phase.Value == PhaseRunning;
        public int CurrentWave => currentWave.Value;
        public int TotalWaves => totalWaves.Value;
        public int RemainingSeconds => remainingSeconds.Value;

        /// <summary>지금 진행 중인 웨이브의 정의. 준비/종료 상태면 null.</summary>
        public WaveDefinition CurrentDefinition =>
            config != null && IsWaveRunning ? config.GetWave(currentWave.Value) : null;

        /// <summary>서버 전용 - 이번 웨이브가 얼마나 지났는지(0~1). EnemySpawner가 원귀를 시간에 맞춰 나누는 데 쓴다.</summary>
        public float ServerWaveProgress =>
            IsWaveRunning && currentDuration > 0f ? Mathf.Clamp01(1f - timer / currentDuration) : 0f;

        /// <summary>261101 추가 - 서버 전용. 이번 웨이브가 시작되고 지난 시간 / 전체 길이(초). 패턴 소환 시점 계산용.</summary>
        public float ServerWaveElapsed => IsWaveRunning ? Mathf.Max(0f, currentDuration - timer) : 0f;
        public float ServerWaveDuration => IsWaveRunning ? currentDuration : 0f;

        /// <summary>261101 추가 - 소환 패턴 설정을 EnemySpawner가 읽는다.</summary>
        public WaveConfigSO Config => config;

        /// <summary>261101 추가 - 서버 전용. 원귀 접촉 피해 배율(기획서 v0.9.3 웨이브마다 +7%).</summary>
        public float CurrentDamageMultiplier =>
            config != null && currentWave.Value > 0 ? config.GetDamageMultiplier(currentWave.Value) : 1f;

        /// <summary>서버 전용 - 지금 스폰되는 원귀의 체력 배율(Enemy.ServerInitialize가 읽는다).</summary>
        public float CurrentHealthMultiplier =>
            config != null && currentWave.Value > 0 ? config.GetHealthMultiplier(currentWave.Value) : 1f;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[CheonMyeong] WaveManager가 씬에 두 개 이상 있습니다 - 하나만 남기고 제거하세요.");
            }
            Instance = this;
        }

        /// <summary>PartyProgress와 같은 이유로 override + base 호출(NetworkVariable Dispose 누락 방지).</summary>
        public override void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            currentWave.OnValueChanged += HandleHudValueChanged;
            totalWaves.OnValueChanged += HandleHudValueChanged;
            remainingSeconds.OnValueChanged += HandleHudValueChanged;
            phase.OnValueChanged += HandlePhaseChanged;

            if (IsServer)
            {
                if (config == null)
                {
                    Debug.LogError("[CheonMyeong] WaveManager에 WaveConfigSO가 연결되지 않았습니다 - " +
                                   "'CheonMyeong/Setup/Create Wave System'을 실행하세요.");
                }
                BeginPreparation();
            }

            RefreshHud();
        }

        public override void OnNetworkDespawn()
        {
            currentWave.OnValueChanged -= HandleHudValueChanged;
            totalWaves.OnValueChanged -= HandleHudValueChanged;
            remainingSeconds.OnValueChanged -= HandleHudValueChanged;
            phase.OnValueChanged -= HandlePhaseChanged;
        }

        private void Update()
        {
            if (!IsServer || !IsSpawned || config == null || IsFinished())
            {
                return;
            }

            // 레벨업 증강 선택 중엔 몹/플레이어와 같이 웨이브 시간도 멈춘다(기획서 5-2 "레벨업 때만 멈춘다").
            if (PartyProgress.Instance != null && PartyProgress.Instance.IsPaused)
            {
                return;
            }

            if (PlayerHealth.AreAllPlayersDown())
            {
                FinishRun(false);
                return;
            }

            if (phase.Value == PhaseRunning)
            {
                runElapsed += Time.deltaTime;
            }

            timer -= Time.deltaTime;
            SyncRemainingSeconds();
            if (timer > 0f)
            {
                return;
            }

            if (phase.Value == PhasePreparing)
            {
                StartWave(1);
                return;
            }

            int next = currentWave.Value + 1;
            bool hasNext = config.GetWave(next) != null;
            EndCurrentWave(hasNext);

            if (hasNext)
            {
                StartWave(next);
            }
            else
            {
                FinishRun(true);
            }
        }

        private bool IsFinished()
        {
            return phase.Value == PhaseCleared || phase.Value == PhaseFailed;
        }

        private void BeginPreparation()
        {
            // 261027 추가 - 이전 판에서 살아남은 원귀/드랍 정리("다시 하기" 하자마자 맞아 죽던 문제).
            Enemy.ServerDespawnAll();
            LootPickup.ServerDespawnAll();

            // 새 판 시작 - 이전 판에서 쓰러진 플레이어가 그대로 남아있지 않게 전원 초기화(다시 하기 대응).
            for (int i = 0; i < PlayerHealth.ActivePlayers.Count; i++)
            {
                PlayerHealth player = PlayerHealth.ActivePlayers[i];
                if (player == null)
                {
                    continue;
                }
                player.ServerResetForNewRun();
                MoveToStart(player, i);
                if (player.TryGetComponent(out PlayerProgress progress))
                {
                    progress.ServerResetRunStats();
                }
            }

            runElapsed = 0f;
            finalRunSeconds.Value = 0;
            clearRewardPerPlayer.Value = 0;

            totalWaves.Value = config != null ? config.WaveCount : 0;
            currentWave.Value = 0;
            phase.Value = PhasePreparing;
            timer = config != null ? config.preparationSeconds : 0f;
            SyncRemainingSeconds();
        }

        /// <summary>
        /// 261027 추가 - 새 판 시작 위치(MapBounds.startPoint, 남쪽 입구)로 이동. 서버 권위 NetworkTransform이라
        /// 서버에서 Teleport로 옮기면 클라이언트는 보간 없이 바로 그 자리로 간다.
        /// </summary>
        private static void MoveToStart(PlayerHealth player, int index)
        {
            MapBounds bounds = MapBounds.Instance;
            if (bounds == null)
            {
                return;
            }

            Vector3 target = bounds.GetStartPosition(index);
            if (player.TryGetComponent(out Rigidbody2D rb))
            {
                rb.position = target;
                rb.linearVelocity = Vector2.zero;
            }
            if (player.TryGetComponent(out NetworkTransform networkTransform))
            {
                networkTransform.Teleport(target, player.transform.rotation, player.transform.localScale);
            }
            else
            {
                player.transform.position = target;
            }
        }

        private void StartWave(int waveNumber)
        {
            currentWave.Value = waveNumber;
            phase.Value = PhaseRunning;
            currentDuration = config.GetDuration(config.GetWave(waveNumber));
            timer = currentDuration;
            SyncRemainingSeconds();
            ServerWaveStarted?.Invoke(waveNumber);
        }

        /// <summary>징 - 남은 원귀 → 원한, 바닥 드랍 흡수, 다음 웨이브가 있으면 회복/부활.</summary>
        private void EndCurrentWave(bool hasNextWave)
        {
            int absorbedGrudge = Enemy.ServerAbsorbAllIntoGrudge();
            if (PartyProgress.Instance != null)
            {
                PartyProgress.Instance.ServerAddGrudge(absorbedGrudge);
            }

            LootPickup.ServerAbsorbAllAtWaveEnd();

            if (hasNextWave)
            {
                for (int i = 0; i < PlayerHealth.ActivePlayers.Count; i++)
                {
                    PlayerHealth player = PlayerHealth.ActivePlayers[i];
                    if (player == null)
                    {
                        continue;
                    }
                    player.ServerHealByRatio(config.endOfWaveHealRatio);
                    player.ServerReviveIfDead(config.reviveHealthRatio);
                }
            }

            ServerWaveEnded?.Invoke(currentWave.Value);
        }

        private void FinishRun(bool cleared)
        {
            // 261027 추가 - 판이 끝나면 남은 원귀/드랍을 바로 치운다. 씬을 언로드해도 스폰된 NetworkObject는
            // 활성 씬으로 옮겨져 살아남아서, 다시 하기 때 새 판으로 그대로 넘어왔다.
            // 261027 수정 - 실패(전원 쓰러짐)로 끝나면 그 순간 살아있던 원귀도 원한으로 흡수한다(예섭 요청 -
            // 결과 화면 "최종 원한"에 반영). 클리어는 마지막 징(EndCurrentWave)에서 이미 흡수가 끝나서 남은
            // 원귀가 없으므로 그냥 정리만 한다. 새 판 준비(BeginPreparation)의 정리는 원한에 넣지 않는다.
            if (cleared)
            {
                Enemy.ServerDespawnAll();
            }
            else if (PartyProgress.Instance != null)
            {
                PartyProgress.Instance.ServerAddGrudge(Enemy.ServerAbsorbAllIntoGrudge());
            }
            else
            {
                Enemy.ServerDespawnAll();
            }
            LootPickup.ServerDespawnAll();

            // 기획서 5-10: 클리어 보상 +200(각자), 실패하면 없음.
            int reward = cleared && config != null ? config.clearRewardGold : 0;
            if (reward > 0)
            {
                for (int i = 0; i < PlayerHealth.ActivePlayers.Count; i++)
                {
                    PlayerHealth player = PlayerHealth.ActivePlayers[i];
                    if (player != null && player.TryGetComponent(out PlayerProgress progress))
                    {
                        progress.AddGold(reward);
                    }
                }
            }
            // 261027 추가 - 로비 엽전(PlayerWallet) 입금. 기획서 5-8: 실패해도 주운 엽전은 받는다.
            for (int i = 0; i < PlayerHealth.ActivePlayers.Count; i++)
            {
                PlayerHealth player = PlayerHealth.ActivePlayers[i];
                if (player != null && player.TryGetComponent(out PlayerProgress progress) && progress.RunGold > 0)
                {
                    progress.DepositRunGoldRpc(progress.RunGold);
                }
            }

            clearRewardPerPlayer.Value = reward;
            finalRunSeconds.Value = Mathf.FloorToInt(runElapsed);

            // 결과 화면이 phase 변경 신호로 열리므로, 표시할 값들을 먼저 다 쓰고 phase를 마지막에 바꾼다.
            phase.Value = cleared ? PhaseCleared : PhaseFailed;
            timer = 0f;
            SyncRemainingSeconds();
            ServerRunFinished?.Invoke(cleared);
            Debug.Log(cleared
                ? "[CheonMyeong] 마지막 웨이브 종료 - 클리어 처리(억쇠 최종전은 보스 구현 후 교체)."
                : "[CheonMyeong] 전원 쓰러짐 - 게임오버.");
        }

        private void SyncRemainingSeconds()
        {
            int seconds = Mathf.Max(0, Mathf.CeilToInt(timer));
            if (remainingSeconds.Value != seconds)
            {
                remainingSeconds.Value = seconds;
            }
        }

        private void HandleHudValueChanged(int previous, int current)
        {
            RefreshHud();
        }

        /// <summary>
        /// 클리어/실패로 바뀌는 순간 전원 각자 화면에서 결과 화면을 연다(phase가 Everyone 읽기라
        /// 서버/참가자 모두에서 실행됨). 결과 화면은 판마다 한 번만 열리므로 FindFirstObjectByType
        /// 비용은 문제 없다.
        /// </summary>
        private void HandlePhaseChanged(int previous, int current)
        {
            if (current != PhaseCleared && current != PhaseFailed)
            {
                return;
            }

            RewardScreen rewardScreen = FindFirstObjectByType<RewardScreen>(FindObjectsInactive.Include);
            if (rewardScreen != null)
            {
                rewardScreen.SetResult(current == PhaseCleared);
            }
            if (UIFlowManager.Instance != null)
            {
                UIFlowManager.Instance.ShowScreen(UIFlowManager.Reward);
            }
        }

        /// <summary>261027 추가 - HUD가 켜질 때 현재 값을 직접 당겨가는 용도(InGameHudScreen.OnEnable).</summary>
        public void RefreshHudNow()
        {
            RefreshHud();
        }

        private void RefreshHud()
        {
            if (InGameHudScreen.Instance != null)
            {
                InGameHudScreen.Instance.SetWaveStatus(currentWave.Value, totalWaves.Value, remainingSeconds.Value);
            }
        }
    }
}
