using Unity.Netcode;
using UnityEngine;

namespace CheonMyeong.Player
{
    /// <summary>
    /// 하영의 시각 연출 전담 - 새 NetworkVariable을 따로 만들지 않고, 이미 Everyone 읽기
    /// 권한으로 복제되고 있는 PlayerMovement.MoveInput / PlayerAutoAttack.AimPosition을
    /// 읽기만 해서 각 클라이언트가 로컬로 Animator를 재생한다. 그래서 오너를 포함한
    /// 모든 클라이언트에서 동일하게(약간의 네트워크 지연만 두고) 보인다.
    ///
    /// 공격 모션만 예외 - "공격을 시도했다"는 사실 자체는 서버에서만 타이머가 도는 로직이라
    /// 서버가 신호를 보내줘야 한다. 예전엔 NetworkVariable&lt;int&gt;의 OnValueChanged를
    /// 구독했는데, 그 방식은 값 변경이 네트워크 틱 단위로만 전파돼서 스윙 연출이 한 박자
    /// 늦게 보이는 버그가 있었다(261013 수정) - 지금은 PlayerAutoAttack.OnAttackTriggered
    /// (Rpc로 쏘는 즉시 신호)를 구독해서 로컬 Animator 트리거를 쏜다.
    ///
    /// 공속(attackInterval)이 바뀌면 Attack 애니메이션 '재생 속도'만 맞춘다 - 클립
    /// 길이(프레임 수/fps) 자체는 건드리지 않고, Animator Controller의 Attack 상태에
    /// speed 파라미터(AttackSpeed)를 걸어두고 매 프레임 (attackClip.length /
    /// attack.AttackInterval)을 계산해서 넣어준다. 공속이 느려지면(간격↑) 값이
    /// 작아져서 재생도 같이 느려지고, 빨라지면 반대로 재생도 빨라진다 - 그래서 공속을
    /// 바꿔도 모션과 실제 타이밍이 항상 맞는다.
    ///
    /// 방향 표현은 회전이 아니라 SpriteRenderer.flipX다 - 몸통+검이 한 장으로 그려진
    /// 실제 아트라서, 예전처럼 transform을 조준 각도로 통째로 돌리면 캐릭터가 기울어지거나
    /// 거꾸로 보인다. 지금은 좌우 반전만 하고 상하 방향은 무시한다(세로 전용 프레임이
    /// 추가되면 그때 확장 - 지금은 미정).
    ///
    /// 첫 애니메이션은 무조건 Idle로 시작(기획 지시, 고정값) - Animator Controller
    /// 쪽의 defaultState도 Idle로 맞춰져 있지만(CheonMyeongPrototypeSetup.
    /// CreateHayoungAnimatorController), 에디터에서 나중에 수동으로 바뀌거나 트랜지션
    /// 설정이 달라져도 항상 보장되도록 OnNetworkSpawn에서 명시적으로
    /// animator.Play(Idle)을 한 번 더 강제한다 - 이중 안전장치.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(SpriteRenderer))]
    public class PlayerAnimator : NetworkBehaviour
    {
        private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
        private static readonly int AttackHash = Animator.StringToHash("Attack");
        private static readonly int AttackSpeedHash = Animator.StringToHash("AttackSpeed");

        /// <summary>Animator Controller의 상태 이름 "Idle"과 반드시 같아야 한다
        /// (CheonMyeongPrototypeSetup.CreateHayoungAnimatorController에서 생성하는 상태명).</summary>
        private const string IdleStateName = "Idle";

        [SerializeField] private Animator animator;
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private PlayerMovement movement;
        [SerializeField] private PlayerAutoAttack attack;

        [Tooltip("Attack 상태 재생 속도 계산용 - attackClip.length를 attack.AttackInterval로 " +
                 "나눠서 Animator의 AttackSpeed 파라미터에 넣는다. 에디터 셋업 스크립트가 자동으로 " +
                 "연결해준다(수동으로 비워두면 재생 속도 보정이 그냥 꺼짐 - 에러는 안 남).")]
        [SerializeField] private AnimationClip attackClip;

        private bool lastIsMoving;
        private bool lastFlipX;
        private bool subscribed;
        private float lastAttackSpeed = 1f;

        public override void OnNetworkSpawn()
        {
            // 기획 지시: 첫 애니메이션은 무조건 Idle로 시작 - defaultState 설정과
            // 별개로 스폰 시점에 한 번 더 명시적으로 강제한다(이중 안전장치).
            // layer 0, normalizedTime 0에서 바로 재생 - 트랜지션 없이 즉시 진입.
            if (animator != null)
            {
                animator.Play(IdleStateName, 0, 0f);
                lastIsMoving = false;
            }

            if (attack != null)
            {
                attack.OnAttackTriggered += HandleAttackTriggered;
                subscribed = true;
            }
        }

        public override void OnNetworkDespawn()
        {
            // 풀링/디스폰 때 구독 해제 안 하면 재사용되거나 파괴된 오브젝트를 향한
            // 콜백이 계속 쌓이는 전형적인 이벤트 구독발 메모리 누수가 된다.
            if (subscribed && attack != null)
            {
                attack.OnAttackTriggered -= HandleAttackTriggered;
                subscribed = false;
            }
        }

        private void Update()
        {
            if (animator == null || movement == null)
            {
                return;
            }

            bool isMoving = movement.MoveInput.sqrMagnitude > 0.01f;
            if (isMoving != lastIsMoving)
            {
                animator.SetBool(IsMovingHash, isMoving);
                lastIsMoving = isMoving;
            }

            UpdateFacing();
            UpdateAttackSpeed();
        }

        /// <summary>
        /// Attack 상태의 재생 속도를 공속(attackInterval)에 맞춘다 - 값이 실제로
        /// 바뀔 때만 SetFloat을 호출해서(Mathf.Approximately 비교) 매 프레임
        /// 불필요한 Animator 호출을 피한다.
        /// </summary>
        private void UpdateAttackSpeed()
        {
            if (animator == null || attack == null || attackClip == null || attackClip.length <= 0f)
            {
                return;
            }

            float interval = attack.AttackInterval;
            float speed = interval > 0.0001f ? attackClip.length / interval : 1f;

            if (!Mathf.Approximately(speed, lastAttackSpeed))
            {
                animator.SetFloat(AttackSpeedHash, speed);
                lastAttackSpeed = speed;
            }
        }

        private void UpdateFacing()
        {
            if (attack == null || spriteRenderer == null)
            {
                return;
            }

            Vector3 toAim = attack.AimPosition - transform.position;
            if (toAim.sqrMagnitude < 0.0001f)
            {
                return; // 아직 조준 정보가 없음(스폰 직후 등) - 이전 방향 유지
            }

            // 원본 아트(Hayoung-Sheet)가 기본으로 왼쪽을 보고 있어서(실제 플레이로 확인됨),
            // 오른쪽을 조준할 때 반전시켜야 커서 방향과 맞는다 - 반대로 하면 커서랑
            // 거꾸로 보이는 증상이 생김.
            bool flipX = toAim.x > 0f;
            if (flipX != lastFlipX)
            {
                spriteRenderer.flipX = flipX;
                lastFlipX = flipX;
            }
        }

        private void HandleAttackTriggered()
        {
            if (animator != null)
            {
                animator.SetTrigger(AttackHash);
            }
        }
    }
}
