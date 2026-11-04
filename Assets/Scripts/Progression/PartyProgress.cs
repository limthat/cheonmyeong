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
    /// 설계해야 하는 영역이었으나 261104 기획서 v0.9.4 10장 공식으로 교체(expFormula*,
    /// Inspector에서 바로 조정 가능).
    /// </summary>
    public partial class PartyProgress : NetworkBehaviour
    {
        public static PartyProgress Instance { get; private set; }

        // 261104 수정 - 기획서 v0.9.4 10장: 필요 영혼 = (20 + 11×L + 0.04×L²) × 5/12, 반올림, 레벨 제한 없음
        // (30레벨에 신통력 전부 최대, 그 뒤는 무한 스탯). 필드 이름을 바꿔서 씬에 저장돼 있던 옛 값(6/5/0.2, 최대 30)이
        // 이 새 기본값으로 바뀐다.
        [Header("경험치 곡선 (261104 - 기획서 v0.9.4 10장)")]
        [Tooltip("상수항 - 기획서 20.")]
        [SerializeField] private float expFormulaBase = 20f;

        [Tooltip("현재 레벨에 곱하는 값 - 기획서 11.")]
        [SerializeField] private float expFormulaLinear = 11f;

        [Tooltip("현재 레벨의 제곱에 곱하는 값 - 기획서 0.04.")]
        [SerializeField] private float expFormulaQuadratic = 0.04f;

        [Tooltip("전체에 곱하는 값 - 기획서 5/12(원귀 수를 줄인 비율).")]
        [SerializeField] private float expFormulaScale = 5f / 12f;

        [Tooltip("최대 레벨. 0이면 제한 없음 - 기획서 v0.9.4 '레벨 제한 없음'.")]
        [SerializeField] private int levelCap = 0;

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

        // 261104 - 예전 "준비 완료" 집합(readyClientIds)은 신통력 고르기(PlayerProgress.HasPicked)로 대체돼서 지웠다.

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
        public bool IsMaxLevel => levelCap > 0 && level.Value >= levelCap;

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
            AugmentChoiceOnNetworkSpawn();
            RefreshHud();
            RefreshGrudgeHud();
        }

        public override void OnNetworkDespawn()
        {
            level.OnValueChanged -= HandleLevelChanged;
            currentExp.OnValueChanged -= HandleExpChanged;
            isPaused.OnValueChanged -= HandleIsPausedChanged;
            grudge.OnValueChanged -= HandleGrudgeChanged;
            AugmentChoiceOnNetworkDespawn();
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

            int gained = 0;
            while (!IsMaxLevel && currentExp.Value >= ExpRequiredForNextLevel(level.Value))
            {
                currentExp.Value -= ExpRequiredForNextLevel(level.Value);
                level.Value++;
                gained++;
            }

            // 261104 - 한 번에 여러 레벨이 올라도 레벨마다 신통력을 한 번씩 고른다(PartyProgress.Augments).
            if (gained > 0)
            {
                ServerQueueAugmentChoices(gained);
            }
        }

        private int ExpRequiredForNextLevel(int fromLevel)
        {
            float raw = (expFormulaBase + expFormulaLinear * fromLevel + expFormulaQuadratic * fromLevel * fromLevel) * expFormulaScale;
            return Mathf.Max(1, Mathf.RoundToInt(raw));
        }

        private void HandleExpChanged(int previous, int current)
        {
            RefreshHud();
        }

        private void HandleLevelChanged(int previous, int current)
        {
            // 261104 - 일시정지 · 신통력 화면 열기는 레벨업 라운드(PartyProgress.Augments)가 맡는다. 여기선 HUD만.
            RefreshHud();
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
                UIFlowManager.Instance.CloseOverlay(UIFlowManager.Upgrade); // 261104 - ESC 메뉴가 위에 있어도 신통력 화면만 닫는다
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
