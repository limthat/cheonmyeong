using CheonMyeong.UI;
using CheonMyeong.UI.Screens;
using Unity.Netcode;
using UnityEngine;

namespace CheonMyeong.Progression
{
    /// <summary>
    /// 파티(두 플레이어) 공유 레벨/경험치 - INGAME HUD 와이어프레임 캡션 "Lv.12 · 영혼 공유"에
    /// 따라 레벨은 플레이어 개인이 아니라 세션 전체가 공유한다. Gameplay 씬에 미리 배치해두는
    /// (in-scene placed) NetworkObject로 둔다 - Enemy처럼 프리팹을 코드로 Instantiate하는 대신,
    /// 맵마다 딱 하나만 있으면 되는 세션 전역 상태라서(PlayerCameraRig/InGameHudScreen과 같은
    /// 씬 싱글턴 관례 + NetworkBehaviour를 결합).
    ///
    /// 확인 못 함 - in-scene 배치 NetworkObject가 NetworkSceneManager의 Additive 로드 시 전
    /// 클라이언트에 자동으로 스폰되는지는 NGO의 "in-scene placed NetworkObjects" 공식 가이드를
    /// 근거로 한 추정이고, 이 프로젝트에서 아직 실기로 검증한 적은 없다 - 레벨이 전혀 안 오르거나
    /// 참가자 화면에 레벨이 안 보이면 이 가정부터 의심할 것(9번 항목 "2인 이상 실기 네트워크
    /// 테스트"와 함께 검증 필요).
    ///
    /// 경험치 곡선: 레벨마다 필요한 경험치를 배열로 미리 못박지 않고 "기준치 + 레벨당 증가분"의
    /// 간단한 1차식으로 계산한다 - 정확한 레벨 디자인(0~30레벨 분배)은 증강 시스템(미정)과 함께
    /// 설계해야 하는 영역이었으나 261027 기획서 5-10 공식으로 교체(expCurveBase/Linear/Quadratic,
    /// Inspector에서 바로 조정 가능).
    /// </summary>
    public class PartyProgress : NetworkBehaviour
    {
        public static PartyProgress Instance { get; private set; }

        [Header("경험치 곡선 (261027 - 기획서 5-10: 필요 영혼 = 6 + 5×L + 0.2×L², 반올림, 0→30레벨)")]
        [Tooltip("상수항 - 기획서 6.")]
        [SerializeField] private float expCurveBase = 6f;

        [Tooltip("현재 레벨에 곱하는 값 - 기획서 5.")]
        [SerializeField] private float expCurveLinear = 5f;

        [Tooltip("현재 레벨의 제곱에 곱하는 값 - 기획서 0.2.")]
        [SerializeField] private float expCurveQuadratic = 0.2f;

        [Tooltip("최대 레벨 - 기획서 30.")]
        [SerializeField] private int maxLevel = 30;

        // 261027 수정 - "레벨은 0부터"(예섭 요청, 기획서 "0레벨에서 시작해 최대 30레벨").
        private readonly NetworkVariable<int> level = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> currentExp = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>
        /// 261014 추가 - "증강 선택중 게임 및 캐릭터 정지 없음(추가할것)" 요청(예섭) 대응.
        /// 레벨업 증강 선택 오버레이가 떠 있는 동안 게임 전체(이동/공격/적 AI/적 스폰 타이머)를
        /// 멈추는 서버 권위 플래그. Time.timeScale은 클라이언트 로컬 값이라 서버 FixedUpdate
        /// 권위 흐름(이동/피해 계산)을 멈추지 못하므로 쓰지 않는다 - PlayerHealth.currentHealth,
        /// PartyProgress.level/currentExp와 같은 "서버만 쓰고 전원이 읽는" NetworkVariable
        /// 패턴을 그대로 따른다.
        /// </summary>
        private readonly NetworkVariable<bool> isPaused = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>
        /// 261027 추가 - "양측 플레이어 다 준비완료면 넘어가는 걸로"(예섭 요청) 대응. 지금까지
        /// 누가 "동료 선택 완료"를 누르든 즉시 재개되던 걸, 접속한 전원이 각자 준비 완료를
        /// 누를 때까지 기다리도록 바꾼다. 서버만 쓰고(RequestReadyRpc 안에서) 전원이 읽는
        /// NetworkVariable 패턴과 달리 이건 "누가" 준비했는지 집합을 들고 있어야 해서
        /// NetworkList를 쓴다 - clientId는 접속마다 값이 달라지므로 bool 두 개로 못 박아두지
        /// 않고 가변 목록으로 둔다(최대 인원은 기획상 2명이지만, 굳이 "플레이어 수는 항상
        /// 2"라고 코드에 못박지 않기 위함).
        /// </summary>
        private readonly NetworkList<ulong> readyClientIds = new NetworkList<ulong>();

        [Header("원한(怨恨) (261027 - 기획서 5-3: 웨이브 끝에 남은 원귀 1마리 = 원한 1, 누적)")]
        [Tooltip("원한 누적값이 각 값 이상이면 한 단계씩 올라간다(오름차순). 기획서 5-3 기본값:\n" +
                 "0~199 맑음 / 200~499 짙음 / 500~999 한 / 1000+ 폭주.")]
        [SerializeField] private int[] grudgeThresholds = { 200, 500, 1000 };

        /// <summary>원한 누적값. 단계(0=맑음..3=폭주)는 이 값과 grudgeThresholds로 계산한다.</summary>
        private readonly NetworkVariable<int> grudge = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public int Level => level.Value;
        public int CurrentExp => currentExp.Value;
        public bool IsPaused => isPaused.Value;
        public int Grudge => grudge.Value;
        public int GrudgeStage => GetGrudgeStage(grudge.Value);
        public bool IsMaxLevel => level.Value >= maxLevel;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[CheonMyeong] PartyProgress가 씬에 두 개 이상 있습니다 - 하나만 남기고 제거하세요.");
            }
            Instance = this;
        }

        /// <summary>
        /// 261014 수정 - 컴파일 경고(CS0114) 수정: NetworkBehaviour.OnDestroy()가
        /// public virtual로 선언돼 있어서(NGO 2.13.3, NetworkBehaviour.cs 소스로 확인함) 이
        /// 메서드가 그걸 오버라이드하지 않고 그냥 같은 이름으로 가려버리고 있었다(섀도잉) -
        /// override 키워드가 없으면 이 GameObject가 파괴될 때 NetworkBehaviour.OnDestroy()의
        /// 내부 로직(NetworkVariableFields의 각 NetworkVariable에 대해 Dispose() 호출,
        /// NetworkObject.OnNetworkBehaviourDestroyed 알림 등 - 소스로 확인)이 전혀 실행되지
        /// 않는다. 즉 단순 경고가 아니라 이 컴포넌트가 파괴될 때마다 level/currentExp/isPaused
        /// 세 NetworkVariable이 정리(Dispose)가 안 되는 실제 누수였다. NGO 공식 주석("If you
        /// override this, you must always invoke the base class version")대로 override +
        /// base.OnDestroy() 호출로 수정.
        /// </summary>
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
            level.OnValueChanged += HandleLevelChanged;
            // 261013 추가 - 경험치 바(SetExpProgress)가 레벨이 안 올라도 경험치만 쌓일 때
            // 매번 갱신되려면 currentExp 변화도 따로 구독해야 한다(레벨 변경은 HandleLevelChanged가
            // 이미 RefreshHud를 부르지만, 그 사이의 경험치 증가분은 이게 아니면 바에 안 반영됨).
            currentExp.OnValueChanged += HandleExpChanged;
            // 261027 추가 - 오버레이를 "여는 쪽"(레벨업 순간)은 HandleLevelChanged가 직접
            // 담당하지만, "닫는 쪽"(전원 준비 완료 후 재개)은 이 콜백으로 분리했다. isPaused는
            // Everyone 읽기라 이 콜백도 서버/참가자 클라이언트 전부에서 실행되므로, 서버가
            // RequestReadyRpc 안에서 isPaused.Value = false로 바꾸는 순간 전원이 동시에 각자
            // 화면에서 오버레이를 닫는다.
            isPaused.OnValueChanged += HandleIsPausedChanged;
            // 261027 추가 - 원한 단계 자체는 HUD만 갱신하면 되므로 RefreshHud처럼 레벨/경험치와
            // 묶지 않고 별도 콜백으로 구독한다(상태 성격이 다름 - 레벨은 서버 계산 기준값, 원한은
            // 주기적 재계산값).
            grudge.OnValueChanged += HandleGrudgeChanged;
            RefreshHud();
            RefreshGrudgeHud();
        }

        public override void OnNetworkDespawn()
        {
            level.OnValueChanged -= HandleLevelChanged;
            currentExp.OnValueChanged -= HandleExpChanged;
            isPaused.OnValueChanged -= HandleIsPausedChanged;
            grudge.OnValueChanged -= HandleGrudgeChanged;
        }

        /// <summary>
        /// 261027 수정 - 서버 전용. WaveManager가 웨이브를 끝낼 때(남은 원귀 흡수량) 호출한다.
        /// 나중에 5웨이브 억쇠를 못 쫓아냈을 때의 "원한 +200"도 같은 메서드로 더하면 된다.
        /// </summary>
        public void ServerAddGrudge(int amount)
        {
            if (!IsServer || amount <= 0)
            {
                return;
            }
            grudge.Value += amount;
        }

        public int GetGrudgeStage(int grudgeValue)
        {
            int stage = 0;
            if (grudgeThresholds != null)
            {
                for (int i = 0; i < grudgeThresholds.Length; i++)
                {
                    if (grudgeValue >= grudgeThresholds[i])
                    {
                        stage = i + 1;
                    }
                }
            }
            return stage;
        }

        private void HandleGrudgeChanged(int previous, int current)
        {
            RefreshGrudgeHud();
        }

        /// <summary>261027 추가 - HUD가 켜질 때 현재 레벨/경험치/원한을 직접 당겨가는 용도(InGameHudScreen.OnEnable).</summary>
        public void RefreshHudNow()
        {
            RefreshHud();
            RefreshGrudgeHud();
        }

        private void RefreshGrudgeHud()
        {
            if (InGameHudScreen.Instance != null)
            {
                InGameHudScreen.Instance.SetGrudge(grudge.Value, GetGrudgeStage(grudge.Value));
            }
        }

        /// <summary>
        /// 서버 전용 - LootPickup(Exp)가 수거될 때 호출한다. 한 번에 큰 경험치를 주워서 여러
        /// 레벨이 한꺼번에 오를 수 있어 while로 처리한다.
        /// </summary>
        public void AddExp(int amount)
        {
            if (!IsServer || amount <= 0)
            {
                return;
            }

            currentExp.Value += amount;

            while (level.Value < maxLevel && currentExp.Value >= ExpRequiredForNextLevel(level.Value))
            {
                currentExp.Value -= ExpRequiredForNextLevel(level.Value);
                level.Value++;
            }
        }

        private int ExpRequiredForNextLevel(int fromLevel)
        {
            return Mathf.Max(1, Mathf.RoundToInt(expCurveBase + expCurveLinear * fromLevel
                                                 + expCurveQuadratic * fromLevel * fromLevel));
        }

        private void HandleExpChanged(int previous, int current)
        {
            RefreshHud();
        }

        private void HandleLevelChanged(int previous, int current)
        {
            RefreshHud();

            if (current > previous)
            {
                // 261014 추가 - 오버레이를 띄우는 것과 함께 게임을 일시정지시킨다. 이 콜백은
                // level이 Everyone 읽기라 서버/클라이언트 전부에서 실행되지만, isPaused는
                // Server만 쓸 수 있는 NetworkVariable이라 서버(호스트)에서만 값을 바꾼다 -
                // 참가자 클라이언트에서 그대로 쓰면 NGO 권한 검사에 막힌다.
                if (IsServer)
                {
                    isPaused.Value = true;
                    // 261027 추가 - 혹시 모를 이전 생애의 찌꺼기 방지(정상 경로라면 전원 준비
                    // 완료 시점에 이미 비워져 있어야 하지만, 방어적으로 한 번 더 비운다).
                    readyClientIds.Clear();
                }

                // 레벨업 - 두 플레이어 모두의 화면에서 증강 선택 오버레이를 띄운다(협동이라
                // 둘 다 골라야 함 - UpgradeScreen.cs 참고).
                if (UIFlowManager.Instance != null)
                {
                    UIFlowManager.Instance.OpenOverlay(UIFlowManager.Upgrade);
                }
            }
        }

        /// <summary>
        /// 261027 추가 - isPaused가 false로 바뀌는 순간(= 전원 준비 완료, RequestReadyRpc 참고)
        /// 모든 클라이언트가 각자 자기 화면의 오버레이를 닫는다. isPaused가 Everyone 읽기라
        /// 서버/참가자 클라이언트 구분 없이 이 콜백이 전부 실행되므로 동시에 닫힌다 - 예전처럼
        /// UpgradeScreen이 클릭 즉시 CloseOverlay()를 직접 부르는 방식(한 명이 눌러도 바로 닫힘)과
        /// 달리, 이제 "닫힘"은 오직 서버 상태(isPaused)만 보고 결정된다.
        /// </summary>
        private void HandleIsPausedChanged(bool previous, bool current)
        {
            if (previous && !current && UIFlowManager.Instance != null)
            {
                UIFlowManager.Instance.CloseOverlay();
            }
        }

        /// <summary>
        /// 261027 수정 - "양측 플레이어 다 준비완료면 넘어가는 걸로"(예섭 요청). UpgradeScreen의
        /// "✓ 준비 완료" 버튼이 호출한다(UpgradeScreen.cs 참고). 이전까지는 RequestUnpauseRpc라는
        /// 이름으로 "누가 누르든 즉시" 재개시켰는데, 이제는 호출한 클라이언트의 clientId를
        /// readyClientIds에 기록해두고, 그 시점에 접속해 있는 전원(NetworkManager.
        /// ConnectedClientsIds.Count)이 다 기록됐을 때만 실제로 재개한다.
        ///
        /// 같은 클라이언트가 두 번 눌러도(연타 등) Contains로 걸러서 중복 집계하지 않는다.
        /// 재개 직후엔 readyClientIds를 비워서 다음 레벨업 때 다시 0명부터 센다.
        ///
        /// rpcParams(RpcParams, 기본값 default)로 "누가 호출했는지"(SenderClientId)를 받는다 -
        /// NGO 2.x Rpc API가 서버에서 받는 요청의 발신자를 알아내는 공식 방법(공식 문서 확인).
        /// InvokePermission = RpcInvokePermission.Everyone을 쓰는 이유는 기존 RequestUnpauseRpc와
        /// 동일(참가자도 호출 가능해야 함 - 위 기존 설명 참고, 동작 변화 없음).
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestReadyRpc(RpcParams rpcParams = default)
        {
            ulong senderId = rpcParams.Receive.SenderClientId;
            if (!readyClientIds.Contains(senderId))
            {
                readyClientIds.Add(senderId);
            }

            int connectedCount = NetworkManager.Singleton != null
                ? NetworkManager.Singleton.ConnectedClientsIds.Count
                : 1;

            if (readyClientIds.Count >= connectedCount)
            {
                readyClientIds.Clear();
                isPaused.Value = false;
            }
        }

        private void RefreshHud()
        {
            if (InGameHudScreen.Instance != null)
            {
                InGameHudScreen.Instance.SetLevel(level.Value);
                InGameHudScreen.Instance.SetExpProgress(currentExp.Value, ExpRequiredForNextLevel(level.Value));
            }
        }
    }
}
