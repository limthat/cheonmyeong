using CheonMyeong.Data;
using Unity.Netcode;
using UnityEngine;

namespace CheonMyeong.Player
{
    /// <summary>
    /// 캐릭터 애니메이션 · 바라보는 방향. 전원(서버 + 클라이언트)이 복제된 값(이동 입력, 조준 위치, 사망)을 보고 각자 그린다.
    ///
    /// 261114 수정 - 8명 캐릭터: Animator 컨트롤러(하영 전용 클립) 대신 캐릭터 카탈로그의 프레임을 코드로 넘긴다.
    /// 기획서 6장 TAG 규칙 그대로:
    /// - IDLE / WALK: 100ms씩 반복.
    /// - ATK: 공격 1번마다 1번 재생, 클립 길이를 공격 간격에 맞춘다(프레임 수 ÷ 공격 간격).
    /// - DIE: 쓰러지는 모션 없이 바로 지친 자세 2프레임 × 300ms 반복.
    /// 그림은 전부 왼쪽을 보고 그려져 있어서, 조준이 오른쪽이면 flipX. 캐릭터 프레임이 없으면 예전 Animator 방식 그대로.
    /// 매 프레임 할당 없음.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class PlayerAnimator : NetworkBehaviour
    {
        private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
        private static readonly int AttackHash = Animator.StringToHash("Attack");
        private static readonly int AttackSpeedHash = Animator.StringToHash("AttackSpeed");
        private static readonly int IsDeadHash = Animator.StringToHash("IsDead");
        private const string IdleStateName = "Idle";

        private const float LoopFrameSeconds = 0.1f;
        private const float IdleFrameSeconds = LoopFrameSeconds / CharacterCatalogSO.IdleSpeedScale;
        private const float DieFrameSeconds = 0.3f;

        [SerializeField] private Animator animator;
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private PlayerMovement movement;
        [SerializeField] private PlayerAutoAttack attack;
        [SerializeField] private PlayerHealth health;

        [Tooltip("Animator 방식일 때만 - Attack 상태 재생 속도 계산용(attackClip.length ÷ 공격 간격).")]
        [SerializeField] private AnimationClip attackClip;

        private bool lastIsMoving;
        private bool lastFlipX;
        private bool subscribed;
        private float lastAttackSpeed = 1f;
        private bool lastIsDead;

        // 프레임 방식
        private Sprite[] idleFrames;
        private Sprite[] walkFrames;
        private Sprite[] atkFrames;
        private Sprite[] dieFrames;
        private bool frameMode;
        private float loopTime;
        private float attackTime = -1f;
        private float attackLength = 1f;
        private int lastLoopState = -1;

        /// <summary>바라보는 방향이 오른쪽인지(원본은 왼쪽). 이펙트 · 투사체 연출이 같이 쓴다.</summary>
        public bool FacingRight => lastFlipX;

        /// <summary>PlayerCharacter가 캐릭터를 정할 때 부른다. 프레임이 하나도 없으면 Animator 방식으로 남는다.</summary>
        public void SetCharacter(CharacterCatalogSO.CharacterEntry entry)
        {
            idleFrames = entry != null ? entry.lobbyIdleFrames : null;
            walkFrames = entry != null ? entry.lobbyWalkFrames : null;
            atkFrames = entry != null ? entry.atkFrames : null;
            dieFrames = entry != null ? entry.dieFrames : null;
            frameMode = HasFrames(idleFrames);
            if (animator != null)
            {
                animator.enabled = !frameMode;
            }
            attackTime = -1f;
            loopTime = 0f;
            lastLoopState = -1;
            if (frameMode && spriteRenderer != null)
            {
                spriteRenderer.sprite = idleFrames[0];
            }
        }

        private static bool HasFrames(Sprite[] frames) => frames != null && frames.Length > 0 && frames[0] != null;

        public override void OnNetworkSpawn()
        {
            if (animator != null && animator.enabled)
            {
                animator.Play(IdleStateName, 0, 0f);
                lastIsMoving = false;
            }
            if (attack != null && !subscribed)
            {
                attack.OnAttackTriggered += HandleAttackTriggered;
                subscribed = true;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (subscribed && attack != null)
            {
                attack.OnAttackTriggered -= HandleAttackTriggered;
                subscribed = false;
            }
        }

        private void Update()
        {
            UpdateFacing();
            if (frameMode)
            {
                UpdateFrames();
                return;
            }
            if (animator == null)
            {
                return;
            }
            UpdateDeadState();
            if (movement == null)
            {
                return;
            }
            bool isMoving = IsMoving();
            if (isMoving != lastIsMoving)
            {
                animator.SetBool(IsMovingHash, isMoving);
                lastIsMoving = isMoving;
            }
            UpdateAttackSpeed();
        }

        private bool IsMoving() => movement != null && movement.MoveInput.sqrMagnitude > 0.01f && !movement.IsSuppressed;

        private void UpdateFrames()
        {
            if (spriteRenderer == null)
            {
                return;
            }
            float dt = Time.deltaTime;
            bool dead = health != null && health.IsDead;
            if (dead)
            {
                attackTime = -1f;
                Sprite[] frames = HasFrames(dieFrames) ? dieFrames : idleFrames;
                SetLoopState(2);
                loopTime += dt;
                spriteRenderer.sprite = frames[(int)(loopTime / DieFrameSeconds) % frames.Length];
                return;
            }

            if (attackTime >= 0f && HasFrames(atkFrames))
            {
                attackTime += dt;
                if (attackTime < attackLength)
                {
                    int index = Mathf.Min(atkFrames.Length - 1, (int)(attackTime / attackLength * atkFrames.Length));
                    spriteRenderer.sprite = atkFrames[index];
                    return;
                }
                attackTime = -1f;
                lastLoopState = -1;
            }

            bool moving = IsMoving() && HasFrames(walkFrames);
            Sprite[] loop = moving ? walkFrames : idleFrames;
            SetLoopState(moving ? 1 : 0);
            loopTime += dt;
            spriteRenderer.sprite = loop[(int)(loopTime / (moving ? LoopFrameSeconds : IdleFrameSeconds)) % loop.Length];
        }

        private void SetLoopState(int state)
        {
            if (state != lastLoopState)
            {
                lastLoopState = state;
                loopTime = 0f;
            }
        }

        private void UpdateDeadState()
        {
            if (health == null)
            {
                return;
            }
            bool isDead = health.IsDead;
            if (isDead != lastIsDead)
            {
                animator.SetBool(IsDeadHash, isDead);
                lastIsDead = isDead;
            }
        }

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
                return;
            }
            bool flipX = toAim.x > 0f; // 원본이 왼쪽을 본다
            if (flipX != lastFlipX)
            {
                spriteRenderer.flipX = flipX;
                lastFlipX = flipX;
            }
        }

        private void HandleAttackTriggered()
        {
            if (health != null && health.IsDead)
            {
                return;
            }
            if (frameMode)
            {
                attackLength = attack != null ? Mathf.Max(0.05f, attack.AttackInterval) : 1f;
                attackTime = 0f;
                return;
            }
            if (animator != null)
            {
                animator.SetTrigger(AttackHash);
            }
        }
    }
}
