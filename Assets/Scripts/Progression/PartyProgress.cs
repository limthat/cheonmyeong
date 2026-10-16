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
    /// 설계해야 하는 영역이라 지금은 플레이스홀더 수치다(expPerLevelBase/expPerLevelGrowth,
    /// Inspector에서 바로 조정 가능).
    /// </summary>
    public class PartyProgress : NetworkBehaviour
    {
        public static PartyProgress Instance { get; private set; }

        [Header("경험치 곡선 (미정 - 플레이스홀더, Inspector에서 조정)")]
        [Tooltip("1레벨에서 2레벨로 오르는 데 필요한 경험치.")]
        [SerializeField] private int expPerLevelBase = 20;

        [Tooltip("레벨이 1 오를 때마다 '다음' 레벨업에 필요한 경험치가 늘어나는 양(1차식 증가).")]
        [SerializeField] private int expPerLevelGrowth = 8;

        private readonly NetworkVariable<int> level = new NetworkVariable<int>(
            1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

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

        public int Level => level.Value;
        public int CurrentExp => currentExp.Value;
        public bool IsPaused => isPaused.Value;

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
            RefreshHud();
        }

        public override void OnNetworkDespawn()
        {
            level.OnValueChanged -= HandleLevelChanged;
            currentExp.OnValueChanged -= HandleExpChanged;
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

            while (currentExp.Value >= ExpRequiredForNextLevel(level.Value))
            {
                currentExp.Value -= ExpRequiredForNextLevel(level.Value);
                level.Value++;
            }
        }

        private int ExpRequiredForNextLevel(int fromLevel)
        {
            return expPerLevelBase + expPerLevelGrowth * (fromLevel - 1);
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
                }

                // 레벨업 - 두 플레이어 모두의 화면에서 증강 선택 오버레이를 띄운다(협동이라
                // 둘 다 골라야 함 - UpgradeScreen.cs 참고). "동료 선택 완료" 동기화는 아직
                // 네트워크 쪽이 없어서 지금은 누가 눌러도 바로 닫힌다 - 기존에 이미 알려진
                // 보류 사항이고 이번 작업 범위 밖이다. (일시정지 해제는 RequestUnpauseRpc
                // 참고 - 같은 "누가 누르든 즉시" 성격을 그대로 따른다.)
                if (UIFlowManager.Instance != null)
                {
                    UIFlowManager.Instance.OpenOverlay(UIFlowManager.Upgrade);
                }
            }
        }

        /// <summary>
        /// 261014 추가 - UpgradeScreen의 "동료 선택 완료" 버튼이 호출한다(UpgradeScreen.cs
        /// 참고). 2인 모두 선택을 완료할 때까지 기다리는 동기화는 아직 구현돼 있지 않다 -
        /// 기존에 이미 알려진 보류 사항(이번 작업 범위 밖)이라, 지금은 오버레이를 닫는 것과
        /// 똑같이 "누구 하나가 눌러도 즉시" 재개시킨다. 나중에 둘 다 완료를 기다리는 로직이
        /// 들어가면 이 메서드 안의 isPaused.Value = false 호출 위치만 그쪽으로 옮기면 된다.
        ///
        /// 확인함(Unity 공식 문서, NGO 2.x) - [Rpc(SendTo.Server, ...)]는 "Send to the server,
        /// regardless of ownership"라고 명시돼 있고, RequireOwnership 파라미터 자체의 기본값도
        /// false다. 즉 아래 InvokePermission = RpcInvokePermission.Everyone은 동작을 바꾸는 게
        /// 아니라 이미 기본값인 설정을 코드에 드러내 "참가자(비호스트) 클라이언트도 호출
        /// 가능해야 한다"는 의도를 명시적으로 남겨두는 것뿐이다(Owner로 바꾸면 소유자만 호출
        /// 가능해지므로 오히려 그쪽이 동작을 바꾸는 변경이 된다).
        ///
        /// 261014 수정 - 컴파일 경고(CS0618) 수정: RequireOwnership은 NGO 2.13.3 소스
        /// (RpcAttributes.cs)에서 [Obsolete("RequireOwnership is deprecated. Please use
        /// InvokePermission = RpcInvokePermission.Owner or ... Everyone instead.")]로 공식
        /// 폐기됐다 - 같은 의미(기본값, 전원 호출 가능)를 유지하는 대체 API로 교체. 동작 변화 없음.
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestUnpauseRpc()
        {
            isPaused.Value = false;
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
