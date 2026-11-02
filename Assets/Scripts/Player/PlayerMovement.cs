using CheonMyeong.Data;
using CheonMyeong.Input;
using CheonMyeong.Progression;
using Unity.Netcode;
using UnityEngine;

namespace CheonMyeong.Player
{
    /// <summary>
    /// 탑뷰 8방향 이동 - 네트워크 버전. 서버 권위로 이동을 계산한다:
    /// 오너(그 하영을 조종하는 클라이언트)는 입력값만 NetworkVariable에 써서
    /// 복제하고, 실제 Rigidbody2D 이동 계산은 서버(호스트)만 FixedUpdate에서
    /// 한다. 결과 위치는 같은 오브젝트의 NetworkTransform이 서버->전 클라이언트로
    /// 동기화한다(NGO 2.13 기준 NetworkTransform은 기본이 서버 권위).
    ///
    /// 클라이언트가 직접 자기 Rigidbody를 움직이면 서버가 보낸 위치랑 매 프레임
    /// 충돌해서 덜덜 떨리고(스터터), 이동 핵도 못 막는다 - 그래서 이동 "계산"은
    /// 반드시 서버만 해야 한다. (입력 지연은 느껴질 수 있는데, 클라이언트 측
    /// 예측/보간은 나중 단계에서 다룬다 - 지금은 "같이 보이고 같이 움직인다"가 목표.)
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerMovement : NetworkBehaviour
    {
        [SerializeField] private PlayerInputReader inputReader;
        [SerializeField] private CharacterStatsSO stats;

        private Rigidbody2D rb;

        private readonly NetworkVariable<Vector2> networkMoveInput = new NetworkVariable<Vector2>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        /// <summary>
        /// PlayerAnimator가 읽는 용도 - 읽기 권한이 Everyone이라 모든 클라이언트가
        /// 추가 네트워크 변수 없이 이 값만 보고 Idle/Walk를 로컬로 판단할 수 있다.
        /// </summary>
        public Vector2 MoveInput => networkMoveInput.Value;

        // 261101 추가 - 억쇠 보스 연동.
        // 제압(배지기): 이동 · 스킬 금지. 모든 클라이언트가 애니메이션(Idle 고정)/공격 중지를 판단하도록 복제한다.
        private readonly NetworkVariable<bool> networkSuppressed = new NetworkVariable<bool>(false);
        private float serverSuppressUntil;
        // 잡힘(배지기 회전 중): 보스가 위치를 직접 옮기므로 여기서는 Rigidbody를 건드리지 않는다.
        private bool serverHeld;
        // 금 나와라 뚝딱: 보스전 동안 누적되는 이동 속도 감소(0~cap).
        private float serverBossSlow;

        /// <summary>제압 중인지(이동 · 스킬 금지) - 모든 클라이언트에서 읽을 수 있다.</summary>
        public bool IsSuppressed => networkSuppressed.Value;

        /// <summary>서버 전용 - 배지기로 잡혔는지. 잡힌 동안 이동 계산을 멈춘다.</summary>
        public void ServerSetHeld(bool held)
        {
            if (!IsServer)
            {
                return;
            }
            serverHeld = held;
            rb.linearVelocity = Vector2.zero;
        }

        /// <summary>서버 전용 - seconds 동안 제압. 이미 제압 중이면 더 긴 쪽으로.</summary>
        public void ServerSuppress(float seconds)
        {
            if (!IsServer || seconds <= 0f)
            {
                return;
            }
            serverSuppressUntil = Mathf.Max(serverSuppressUntil, Time.time + seconds);
            networkSuppressed.Value = true;
            rb.linearVelocity = Vector2.zero;
        }

        /// <summary>서버 전용 - 이동 속도 감소를 더한다(최대 cap).</summary>
        public void ServerAddBossSlow(float amount, float cap)
        {
            if (!IsServer)
            {
                return;
            }
            serverBossSlow = Mathf.Clamp(serverBossSlow + Mathf.Max(0f, amount), 0f, Mathf.Clamp01(cap));
        }

        /// <summary>서버 전용 - 보스전이 끝나면 감속 · 제압 · 잡힘을 모두 푼다.</summary>
        public void ServerClearBossSlow()
        {
            if (!IsServer)
            {
                return;
            }
            serverBossSlow = 0f;
            serverHeld = false;
            serverSuppressUntil = 0f;
            networkSuppressed.Value = false;
        }

        public override void OnNetworkDespawn()
        {
            serverHeld = false;
            serverBossSlow = 0f;
            serverSuppressUntil = 0f;
            base.OnNetworkDespawn();
        }

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            rb.gravityScale = 0f; // 탑뷰이므로 중력 불필요
            rb.freezeRotation = true;

            // 261013 추가 - "이동 중 카메라 떨림" 버그 수정. 이동 계산(rb.linearVelocity 적용)은
            // FixedUpdate(물리 틱, 보통 초당 50회 고정)에서만 일어나는데, 카메라(PlayerCameraRig)는
            // LateUpdate(렌더 프레임마다, 보통 초당 60~144+회)에서 그 Transform을 읽어서 따라간다.
            // Rigidbody2D.interpolation이 기본값 None이면 Transform 위치가 물리 틱이 실제로 도는
            // 프레임에만 바뀌고 그 사이 프레임들은 똑같은 값을 유지하다가 한 번에 점프하므로,
            // 렌더 프레임 수가 물리 틱보다 많을 때(거의 항상 그럼) 카메라가 그 "계단식" 움직임을
            // 그대로 따라가면서 떨리는 것처럼 보인다(Unity 공식 매뉴얼 "Rigidbody2D.interpolation"
            // 문서가 설명하는 정확히 그 증상). Interpolate로 바꾸면 물리 엔진이 "다음 틱에
            // 어디로 갈지"를 미리 알고 매 렌더 프레임마다 그 사이를 보간해서 Transform을
            // 갱신해주므로, 실제 물리 계산(서버 전용, FixedUpdate)은 그대로 두고 화면에 보이는
            // 움직임만 매끄러워진다 - 조작감/판정에는 영향 없음.
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        }

        private void Update()
        {
            // 오너만 자기 입력을 네트워크 변수에 써서 복제한다.
            if (!IsOwner || inputReader == null)
            {
                return;
            }

            networkMoveInput.Value = inputReader.MoveInput;
        }

        private void FixedUpdate()
        {
            // 이동 "적용"은 서버만 - 호스트 자신의 하영도 서버 겸 오너라 여기까지 온다.
            if (!IsServer || stats == null)
            {
                return;
            }

            // 261014 추가 - "증강 선택중 게임 및 캐릭터 정지 없음" 수정. 일시정지 중엔 그냥
            // return만 하면 일시정지 "직전" 프레임의 속도값이 Rigidbody2D에 그대로 남아 계속
            // 미끄러지므로, 속도를 명시적으로 0으로 멈춰야 한다.
            if (PartyProgress.Instance != null && PartyProgress.Instance.IsPaused)
            {
                rb.linearVelocity = Vector2.zero;
                return;
            }

            if (networkSuppressed.Value && Time.time >= serverSuppressUntil)
            {
                networkSuppressed.Value = false;
            }
            if (serverHeld)
            {
                return; // 위치는 억쇠가 직접 옮긴다(EoksoeBoss.SetHeldPosition)
            }
            if (networkSuppressed.Value)
            {
                rb.linearVelocity = Vector2.zero;
                return;
            }

            Vector2 move = networkMoveInput.Value;
            if (move.sqrMagnitude > 1f)
            {
                move.Normalize(); // 대각선 이동 속도 보정
            }

            rb.linearVelocity = move * (stats.moveSpeed * (1f - serverBossSlow));
        }
    }
}
