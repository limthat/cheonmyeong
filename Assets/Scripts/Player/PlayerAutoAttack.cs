using CheonMyeong.Combat;
using CheonMyeong.Data;
using CheonMyeong.Enemies;
using CheonMyeong.Input;
using CheonMyeong.Progression;
using Unity.Netcode;
using UnityEngine;

namespace CheonMyeong.Player
{
    /// <summary>
    /// 하영의 기본 무기 - 네트워크 버전. 조준 방향은 오너가 네트워크 변수에
    /// 써서 전원이 복제받는다. 실제 피해 판정(Attack)은 서버만 수행한다 -
    /// 클라이언트가 직접 때리면 치팅/중복 피해 위험이 있다.
    ///
    /// 방향 연출(회전)은 이 스크립트에서 더 이상 하지 않는다 - 예전엔 facingPivot을
    /// 한 번도 안 연결해서(에디터 셋업에서 할당 누락) 사실상 루트 transform 전체를
    /// 조준 각도로 계속 회전시키고 있었는데, 몸통+검이 한 장으로 그려진 실제 도트
    /// 아트가 들어오면서 이 방식대로 두면 조준 방향에 따라 캐릭터가 기울어지거나
    /// 거꾸로 보인다. 지금은 PlayerAnimator가 AimPosition만 읽어서 SpriteRenderer.flipX로
    /// 좌우만 반전시키는 방식으로 바꿨다 - 그래서 networkAimPosition은 그대로 두고
    /// 외부에서 읽을 수 있게 AimPosition 프로퍼티만 추가했다.
    /// </summary>
    public class PlayerAutoAttack : NetworkBehaviour
    {
        [SerializeField] private PlayerInputReader inputReader;
        [SerializeField] private CharacterStatsSO stats;
        [SerializeField] private LayerMask enemyLayer;

        private const int MaxHitsPerAttack = 32;
        private readonly Collider2D[] hitBuffer = new Collider2D[MaxHitsPerAttack];
        private ContactFilter2D contactFilter;

        private float attackTimer;

        /// <summary>
        /// 261027 추가 - "증강 선택 후 공격/애니메이션에 딜레이가 생긴다"(예섭 지적) 원인
        /// 수정용. attackTimer/pendingHitDelay는 일시정지(IsPaused) 중엔 그대로 멈춰있다가
        /// 재개 시 멈췄던 그 자리에서부터 다시 카운트된다 - 그 자체는 의도대로지만, 레벨업
        /// 순간 공격 애니메이션이 막 트리거된 직후(pendingHitDelay가 채워진 채) 바로 일시정지가
        /// 걸리면, 화면엔 이미 스윙 모션이 재생돼버린 지 한참 지난 뒤(증강 고르는 동안 애니메이터는
        /// 멈추지 않으므로) 실제 판정(ApplyHit)이 뒤늦게 들어가거나, attackTimer도 멈췄던
        /// 어중간한 값부터 다시 줄어들어 다음 공격 타이밍이 눈에 띄게 어긋나 보였다. 일시정지가
        /// "막 풀린" 바로 그 프레임을 감지해서 둘 다 리셋한다 - 공격 한 주기 쉬고 새로 시작하는
        /// 셈이라 더 자연스럽다.
        /// </summary>
        private bool wasPausedLastFrame;

        /// <summary>
        /// 261014 추가 - "공격 판정이 모션보다 먼저 온다"(예섭 표현: "번개처럼 빛(판정)이 먼저
        /// 치고 소리(모션)가 뒤늦게 따라온다") 버그의 진짜 원인 수정용 타이머. 이전까지는
        /// Attack()이 PlayAttackAnimRpc()(모션 트리거)와 실제 OverlapCircle+TakeDamage 판정을
        /// 같은 메서드, 같은 프레임에서 동시에 처리했다 - 그래서 피해는 공격을 "결정한" 바로 그
        /// 순간 즉발로 들어가는데, 화면에 칼을 휘두르는 모션이 실제로 보이기까지는 Animator가
        /// Attack 상태로 전환하고 몇 프레임 재생되는 시간이 더 걸렸다. 음수(-1)면 "대기 중인
        /// 타격 없음" - Attack()이 모션 트리거를 쏘는 순간 이 값을 양수로 채워두고, Update()가
        /// 매 프레임 줄이다가 0 이하가 되는 순간 ApplyHit()(실제 판정)을 그제서야 실행한다.
        /// </summary>
        private float pendingHitDelay = -1f;

        /// <summary>
        /// 261014 추가 - 모션 트리거 시점부터 실제 타격 판정까지 기다리는 시간을, 공격 한
        /// 주기(AttackInterval) 대비 비율로 둔다(초 단위 고정값 대신 비율을 쓰는 이유 -
        /// attacksPerSecond가 바뀌면 애니메이션 재생 속도도 같이 바뀌므로, 고정 초 단위로
        /// 두면 공속이 빨라질수록 "스윙 전체보다 긴 대기시간"이 되는 모순이 생긴다).
        /// 261014 확정 - AttackRangeDebugVisual 타이밍을 실제 판정 시점(OnAttackResolved)에
        /// 맞춘 뒤 예섭이 직접 0.35~0.9 범위를 테스트해보고 "0.8 정도가 좋을듯. 하영은
        /// 이대로 고정"으로 확인 - 하영 기준값은 0.8로 확정, 더 이상 임시값 아님. 다른
        /// 캐릭터가 추가되면 그 캐릭터의 실제 스윙 클립을 보고 각자 따로 맞출 것(지금
        /// 수치를 그대로 복붙하지 말 것).
        /// </summary>
        [SerializeField] private float attackHitDelayRatio = 0.8f;

        private readonly NetworkVariable<Vector3> networkAimPosition = new NetworkVariable<Vector3>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        /// <summary>
        /// 261013 수정 - 원래 NetworkVariable&lt;int&gt; 카운터 + OnValueChanged 구독 방식이었는데,
        /// "스윙 애니메이션/피격 연출이 한 박자 늦게 보인다"는 재현 버그의 원인이었다. 공식 문서
        /// 확인(NGO 2.x, 2026-10-13): NetworkVariable 변경사항은 "매 프레임이 아니라 네트워크
        /// 틱마다 모아서" 전파되기 때문에, 같은 프로세스인 호스트 자기 자신이 봐도 값이 바뀐
        /// 프레임에 바로 OnValueChanged가 오지 않고 다음 틱까지 미뤄질 수 있었다(틱레이트가
        /// 렌더 프레임레이트보다 낮으면 몇 프레임 지연으로 보임). 반면 Rpc는 "호스트가 호출하면
        /// 호스트 자기 자신에게는 같은 스택에서 즉시 실행된다"고 공식 문서에 명시돼 있어서,
        /// 상태 동기화가 필요 없는 일회성 신호(스윙 연출 트리거)는 Rpc로 바꿨다. 호스트의 손맛
        /// 테스트뿐 아니라 실제 접속 클라이언트에게도(ClientsAndHost) 프레임 끝에 바로 전달된다.
        /// </summary>
        public event System.Action OnAttackTriggered;

        /// <summary>
        /// 261014 추가 - "빨간 원뿔(AttackRangeDebugVisual) 타이밍이 실제 대미지 타이밍과
        /// 안 맞는다"(예섭 지적)는 문제 수정용. OnAttackTriggered는 모션 트리거 시점(= 예전
        /// 즉발 판정 시점)에 울리는 이벤트라 그대로 뒀는데, 실제 피해는 이제 pendingHitDelay만큼
        /// 늦게 ApplyHit()에서 들어가므로 디버그 시각화가 이 이벤트를 구독하면 여전히 옛날
        /// 타이밍(모션 트리거 순간)에 뜬다. ApplyHit() 시작 시점에 AttackResolvedVisualRpc를 통해
        /// 호출되는 별도 이벤트를 새로 둬서, 디버그 시각화 쪽에서 구독 대상만 이걸로 바꾸면
        /// 실제 판정 순간과 정확히 일치하게 된다(OnAttackTriggered 자체는 애니메이션 트리거
        /// 용도로 계속 필요해서 남겨둠 - 용도가 다른 두 이벤트). ApplyHit()은 IsServer
        /// 가드 안에서만 실행되므로(서버만 피해 판정), 호스트가 아닌 원격 클라이언트
        /// 화면에도 디버그 도형을 띄우려면 OnAttackTriggered처럼 Rpc로 ClientsAndHost에
        /// 전파해야 한다 - 이벤트를 직접 여기서 Invoke하지 않고 AttackResolvedVisualRpc를
        /// 거치는 이유.
        /// </summary>
        public event System.Action OnAttackResolved;

        public Vector3 AimPosition => networkAimPosition.Value;

        /// <summary>261027 추가 - 공격 주기 중 실제 판정이 들어가는 지점 비율(PlayerAttackEffect가 이펙트 길이 계산에 씀).</summary>
        public float HitDelayRatio => attackHitDelayRatio;

        /// <summary>
        /// PlayerAnimator가 읽는 용도 - 공속(attacksPerSecond)이 바뀌어도 Attack
        /// 애니메이션 클립 길이는 그대로 두고 재생 속도만 맞추기 위해 "초 단위 간격"으로
        /// 변환해서 노출한다. stats.attacksPerSecond는 공식 스탯 시트와 같은 의미
        /// (값이 클수록 빠름)라서, 여기서는 1 / attacksPerSecond로 뒤집어 간격(초)을
        /// 계산한다. stats가 비어있거나 attacksPerSecond가 0 이하면(에디터 셋업 누락 등)
        /// 1초를 기본값으로 둬서 0으로 나누는 사고를 방지.
        /// </summary>
        public float AttackInterval =>
            stats != null && stats.attacksPerSecond > 0.0001f ? 1f / stats.attacksPerSecond : 1f;

        private void Awake()
        {
            // Physics2D.OverlapCircleNonAlloc은 Unity 6000.x부터 Obsolete 처리됐다.
            // 대신 ContactFilter2D + 배열을 받는 OverlapCircle 오버로드를 쓴다 (여전히 논얼록).
            contactFilter = new ContactFilter2D();
            contactFilter.SetLayerMask(enemyLayer);
            contactFilter.useTriggers = true; // 적 콜라이더가 트리거로 구성될 수도 있어 포함

            // 261014 추가 - "몬스터 보자마자 즉발 공격"(예섭 지적) 원인 수정. attackTimer는
            // 필드 선언만 있고 초기화를 안 해서 C# 기본값인 0으로 시작했다 - Update()의
            // "attackTimer <= 0f면 공격" 조건이 처음부터 참이라, 몬스터가 인지 범위에
            // 들어오는 바로 그 프레임에 윈드업 없이 즉시 Attack()이 발동했었다(이후
            // 공격들은 attackTimer가 AttackInterval로 채워지니 정상적으로 간격을 둠 - 유독
            // "첫 공격"만 쿨다운을 건너뛰는 비일관적 동작이었음). 시작값을 AttackInterval로
            // 채워서 첫 공격도 다른 공격들과 동일하게 한 주기를 기다리게 함(예섭 확인-
            // "고치기" 선택, 261014).
            attackTimer = AttackInterval;
            TryGetComponent(out movement); // 261101 - 제압(억쇠 배지기) 확인용
        }

        private PlayerMovement movement;

        private void Update()
        {
            if (IsOwner && inputReader != null)
            {
                networkAimPosition.Value = inputReader.AimWorldPosition;
            }

            // 공격 판정(피해 적용)은 서버만 - 클라이언트 각자 판정하면 중복/불일치 생긴다.
            if (!IsServer || stats == null)
            {
                return;
            }

            // 261014 추가 - 일시정지 중엔 공격 타이머도 함께 멈춘다. 여기서 멈추지 않으면
            // 증강 선택 화면이 떠 있는 동안에도 attackTimer가 계속 줄어들다가, 재개되는
            // 순간 밀린 공격이 한꺼번에 나가버린다.
            bool isPaused = PartyProgress.Instance != null && PartyProgress.Instance.IsPaused;
            if (isPaused)
            {
                wasPausedLastFrame = true;
                return;
            }

            if (wasPausedLastFrame)
            {
                // 261027 추가 - 방금 일시정지가 풀린 첫 프레임. 멈춰있던 동안 어중간하게
                // 남아있던 타이머를 전부 리셋해서, 레벨업 직전에 쌓여있던 공격 타이밍이
                // 재개 직후 어긋나 보이지 않게 한다(위 wasPausedLastFrame 필드 설명 참고).
                wasPausedLastFrame = false;
                attackTimer = AttackInterval;
                pendingHitDelay = -1f;
            }

            // 261101 추가 - 제압(억쇠 배지기) 중엔 공격 금지. 예약된 판정도 취소한다.
            if (movement != null && movement.IsSuppressed)
            {
                pendingHitDelay = -1f;
                return;
            }

            // 261014 추가 - 대기 중인 타격 판정(Attack()이 예약해둔 것)을 먼저 처리한다.
            // attackTimer와는 완전히 독립적인 별개의 타이머 - 공격 "트리거 여부"를 결정하는
            // 주기와 "이미 트리거된 공격의 판정이 언제 들어가는지"는 서로 다른 문제라서
            // 하나로 합치지 않았다.
            if (pendingHitDelay >= 0f)
            {
                pendingHitDelay -= Time.deltaTime;
                if (pendingHitDelay <= 0f)
                {
                    pendingHitDelay = -1f;
                    ApplyHit();
                }
            }

            attackTimer -= Time.deltaTime;
            if (attackTimer <= 0f)
            {
                // attackRange * attackAwarenessRangeMultiplier(몬스터 인지거리, 기본값은
                // attackRange와 동일한 1배) 반경 안에 적이 하나도 없으면 공격을 쏘지 않는다.
                // 이때 타이머는 리셋하지 않고 0 이하로 그대로 둔다 - 그래야 적이 범위
                // 안으로 들어오는 순간 대기 없이 바로 공격이 나간다. 이동 중 Idle/Walk는
                // PlayerAnimator가 MoveInput만 보고 따로 판단하므로 여기선 신경 쓸 필요 없음.
                if (HasTargetInRange(stats.attackRange * stats.attackAwarenessRangeMultiplier))
                {
                    Attack();
                    // attacksPerSecond(공식 스탯 시트 단위 - 클수록 빠름)를 타이머가 쓰는
                    // "초 단위 간격"으로 변환. 0 이하 방지 가드는 AttackInterval 프로퍼티와
                    // 동일하게 여기서도 둔다(에디터 셋업 누락 등으로 0이 들어올 가능성 대비).
                    attackTimer = stats.attacksPerSecond > 0.0001f ? 1f / stats.attacksPerSecond : 1f;
                }
            }
        }

        /// <summary>
        /// 실제 피해 판정과 같은 enemyLayer 필터를 재사용해서 "공격을 시도할 가치가
        /// 있는지"만 싼 값에 확인한다 - hitBuffer를 공유해도 Attack()이 곧바로 다시
        /// 채워 넣어서 결과가 섞일 일은 없다(같은 프레임 안에서 순차 실행).
        /// </summary>
        private bool HasTargetInRange(float radius)
        {
            Vector2 origin = transform.position;
            int hitCount = Physics2D.OverlapCircle(origin, radius, contactFilter, hitBuffer);
            for (int i = 0; i < hitCount; i++)
            {
                // 261101 - 억쇠 '정신병' 차원 안/밖이 다르면 대상이 아니다.
                if (Boss.DeathRealm.CanInteract(gameObject, hitBuffer[i].gameObject))
                {
                    return true;
                }
            }
            return false;
        }

        private void Attack()
        {
            // 맞았는지와 무관하게 "시도" 자체를 애니메이션 트리거로 삼는다 - 허공을
            // 휘둘러도 모션은 재생돼야 자연스럽다.
            PlayAttackAnimRpc();

            // 261014 추가 - 실제 타격 판정(ApplyHit)은 여기서 바로 하지 않고 attackHitDelayRatio만큼
            // 미뤄서 Update()가 나중에 실행하게 한다(위 pendingHitDelay 필드 설명 참고) - "판정이
            // 모션보다 먼저 온다" 버그 수정의 핵심.
            pendingHitDelay = AttackInterval * attackHitDelayRatio;
        }

        /// <summary>
        /// 261014 추가 - 기존 Attack()에 있던 실제 타격 판정 부분을 그대로 분리해온 것(로직
        /// 변경 없음, 호출 시점만 늦춤). pendingHitDelay가 다 지난 뒤 Update()가 호출한다 -
        /// 그 사이 적이 움직였을 수 있으므로 판정 시점에 origin/적 위치를 다시 계산하는 게
        /// 오히려 더 정확하다(트리거 시점 값을 스냅샷해두지 않는 이유).
        /// </summary>
        private void ApplyHit()
        {
            // 261014 추가 - 실제 판정이 일어나는 바로 이 순간을 디버그 시각화(AttackRangeDebugVisual)에
            // 알린다. OnAttackTriggered(모션 트리거 시점)가 아니라 여기서 울려야 "빨간 원뿔이
            // 뜨는 타이밍 = 실제 대미지가 들어가는 타이밍"이 맞아떨어진다. ApplyHit()은 서버에서만
            // 실행되므로 Rpc로 ClientsAndHost(호스트 자신 포함 모든 접속 클라이언트)에 전파.
            AttackResolvedVisualRpc();

            Vector2 origin = transform.position;

            // 버그 수정: 공격을 "시작"하는 판정(attackRange * attackAwarenessRangeMultiplier
            // = 몬스터 인지거리)과 실제 "타격" 판정 반경이 서로 달랐었다 - 예전엔 여기서
            // stats.attackRange(좁은 쪽)만 썼는데, 그러면 몬스터가 시작 판정엔 걸렸지만
            // 아직 좁은 사거리까진 덜 들어온 타이밍에 스윙이 나가면 완전히 빗나가고,
            // 계속 다가오던 몬스터가 다음 공속 주기(attackInterval)가 돼서야 진짜 사거리에
            // 들어와 맞는 "스윙 이후 한 박자 늦게 피격되는" 증상이 생겼다. 공격을
            // 시작하기로 한 판정 반경 그대로 실제 타격도 판정하도록 통일해서 해결.
            // attackAwarenessRangeMultiplier 기본값을 attackRange와 동일한 1배로 둬서,
            // 이 반경이 attackRange보다 실질적으로 더 커지지 않도록(=사거리 롤백이
            // 무의미해지지 않도록) 함.
            float hitRadius = stats.attackRange * stats.attackAwarenessRangeMultiplier;
            int hitCount = Physics2D.OverlapCircle(origin, hitRadius, contactFilter, hitBuffer);

            // 조준 방향을 중심으로 한 부채꼴(attackArcDegrees)만 실제 피해 판정에 포함한다
            // (기획 지시: "공격 폭이 너무 넓다, 지금 거의 180도인데 반으로 줄이자").
            // 조준 정보가 아직 없으면(스폰 직후 한 프레임 등) 판정을 통째로 누락시키지
            // 않기 위해 전방위로 취급한다.
            Vector2 aimDir = (Vector2)networkAimPosition.Value - origin;
            bool hasAimDir = aimDir.sqrMagnitude > 0.0001f;
            float halfArcDegrees = stats.attackArcDegrees * 0.5f;

            for (int i = 0; i < hitCount; i++)
            {
                if (!Boss.DeathRealm.CanInteract(gameObject, hitBuffer[i].gameObject))
                {
                    continue; // 261101 - 차원 안/밖이 다르면 못 때린다
                }

                if (hasAimDir)
                {
                    Vector2 toTarget = (Vector2)hitBuffer[i].transform.position - origin;
                    if (toTarget.sqrMagnitude > 0.0001f && Vector2.Angle(aimDir, toTarget) > halfArcDegrees)
                    {
                        continue; // 사거리 안이어도 부채꼴 밖이면 미스
                    }
                }

                if (hitBuffer[i].TryGetComponent(out IDamageable damageable))
                {
                    // 261027 추가 - 결과 화면 처치 수. 이 타격 전엔 살아있었는데 타격 후 쓰러졌으면 내 처치.
                    Enemy enemy = damageable as Enemy;
                    bool wasAlive = enemy != null && !enemy.IsDying;

                    damageable.TakeDamage(stats.attackPower);

                    if (wasAlive && enemy.IsDying && TryGetComponent(out PlayerProgress progress))
                    {
                        progress.ServerAddKill();
                    }
                }
            }
        }

        /// <summary>서버만 호출. ClientsAndHost로 보내서 호스트 자신을 포함해 접속한 모든
        /// 클라이언트에서 즉시(호스트는 동기적으로, 원격 클라이언트는 프레임 끝에) Attack
        /// 애니메이션 트리거가 울린다.</summary>
        [Rpc(SendTo.ClientsAndHost)]
        private void PlayAttackAnimRpc()
        {
            OnAttackTriggered?.Invoke();
        }

        /// <summary>서버(ApplyHit 내부)만 호출. 실제 피해 판정이 들어가는 순간을 모든
        /// 접속 클라이언트(호스트 포함)에 알려서 디버그 시각화(AttackRangeDebugVisual)의
        /// 타이밍을 실제 판정 시점과 맞춘다 - PlayAttackAnimRpc와 같은 전파 방식, 목적만 다름
        /// (모션 트리거 vs 실제 판정 알림).</summary>
        [Rpc(SendTo.ClientsAndHost)]
        private void AttackResolvedVisualRpc()
        {
            OnAttackResolved?.Invoke();
        }

        private void OnDrawGizmosSelected()
        {
            if (stats == null)
            {
                return;
            }

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, stats.attackRange); // 기준값(attackRange) 참고용 - 실제 판정 반경 아님

            // 주황 - 공격 시작 판정과 실제 타격 판정이 공유하는 반경(attackRange * attackAwarenessRangeMultiplier).
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, stats.attackRange * stats.attackAwarenessRangeMultiplier);

            // 공격 부채꼴(attackArcDegrees) 시각화 - Play 중이고 조준 정보가 있으면 실제 조준
            // 방향 기준, 아니면(에디터에서 선택만 한 상태) 오브젝트의 +X축 기준으로 폭만 표시.
            Vector2 origin = transform.position;
            Vector2 aimDir = Application.isPlaying && networkAimPosition.Value != default
                ? (Vector2)networkAimPosition.Value - origin
                : Vector2.right;
            if (aimDir.sqrMagnitude < 0.0001f)
            {
                aimDir = Vector2.right;
            }
            aimDir.Normalize();

            float halfArc = stats.attackArcDegrees * 0.5f;
            Vector2 leftEdge = Quaternion.Euler(0f, 0f, halfArc) * aimDir;
            Vector2 rightEdge = Quaternion.Euler(0f, 0f, -halfArc) * aimDir;

            float arcLineLength = stats.attackRange * stats.attackAwarenessRangeMultiplier; // 실제 타격 판정 반경과 맞춤
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(origin, origin + leftEdge * arcLineLength);
            Gizmos.DrawLine(origin, origin + rightEdge * arcLineLength);
        }
    }
}
