using CheonMyeong.Combat;
using CheonMyeong.Data;
using UnityEngine;

namespace CheonMyeong.Player
{
    /// <summary>
    /// 261027 추가 - 하영 베기 이펙트("공격 이펙트를 맞춰서 적용해줘" - 예섭). HaYoung-Sheet 맨 아랫줄
    /// (9프레임, 초승달 모양 참격이 점점 흩어짐)을 공격 판정 순간에 조준 방향으로 재생한다.
    ///
    /// 무엇에 맞췄는지:
    /// - 시작 시점: PlayerAutoAttack.OnAttackResolved = 실제 피해가 들어가는 순간(ApplyHit). 예전 빨간
    ///   사거리 도형이 쓰던 신호와 같아서 "이펙트가 보이는 순간 = 맞는 순간"이다.
    /// - 길이: 판정 순간부터 다음 공격 시작까지 남은 시간(AttackInterval × (1 - HitDelayRatio)).
    ///   공속이 빨라지면 이펙트도 같이 빨라지고, 다음 휘두르기와 겹치지 않는다.
    /// - 방향/위치/크기: 실제 판정에 쓰는 조준 방향과 반경(attackRange × attackAwarenessRangeMultiplier)을
    ///   그대로 써서, 참격 호가 부채꼴 판정 범위 쪽에 판정 반경만 한 크기로 그려진다.
    ///
    /// 네트워크: OnAttackResolved가 서버 → 모든 화면 Rpc라서 각자 로컬로 재생만 하면 된다(동기화 추가 없음).
    /// 원본 이펙트 프레임은 하영 아트와 같은 방향(왼쪽)을 보고 그려져 있어서, 왼쪽(-X)을 "앞"으로 보고
    /// 조준 방향으로 회전시킨다. 오른쪽을 볼 때 위아래가 뒤집혀 보이지 않도록 flipY로 보정.
    /// </summary>
    [RequireComponent(typeof(PlayerAutoAttack))]
    public class PlayerAttackEffect : MonoBehaviour
    {
        [SerializeField] private PlayerAutoAttack autoAttack;
        [SerializeField] private CharacterStatsSO stats;

        [Tooltip("참격 프레임(왼쪽을 보는 원본 그대로). CheonMyeong/Setup/Setup Hayoung Attack Effect가 HaYoung-Sheet\n" +
                 "맨 아랫줄을 연결한다.")]
        [SerializeField] private Sprite[] frames;

        [Tooltip("판정 반경 대비 이펙트 중심이 앞으로 나가는 비율(0 = 몸 중심, 1 = 판정 끝).")]
        [Range(0f, 1f)] [SerializeField] private float forwardOffsetRatio = 0.5f;

        [Tooltip("이펙트 세로 크기를 판정 지름의 몇 배로 그릴지. 1 = 판정 반경×2와 같은 높이.")]
        [Min(0.05f)] [SerializeField] private float sizeToRangeRatio = 0.9f;

        [Tooltip("이펙트가 너무 짧게 지나가지 않도록 하는 최소 재생 시간(초).")]
        [Min(0.01f)] [SerializeField] private float minDuration = 0.15f;

        [SerializeField] private int sortingOrder = 20;

        private SpriteRenderer effectRenderer;
        private Transform effectTransform;
        private float elapsed;
        private float duration;
        private bool playing;
        private Rect visibleBounds;
        private bool visibleBoundsReady;

        private void Awake()
        {
            if (autoAttack == null)
            {
                autoAttack = GetComponent<PlayerAutoAttack>();
            }

            // 하영 루트에는 이미 캐릭터 SpriteRenderer가 있어서 이펙트는 자식 오브젝트에 따로 그린다.
            var child = new GameObject("AttackEffect");
            effectTransform = child.transform;
            effectTransform.SetParent(transform, false);
            effectRenderer = child.AddComponent<SpriteRenderer>();
            effectRenderer.sortingOrder = sortingOrder;
            effectRenderer.enabled = false;
        }

        private void OnEnable()
        {
            if (autoAttack != null)
            {
                autoAttack.OnAttackResolved += HandleAttackResolved;
            }
        }

        private void OnDisable()
        {
            if (autoAttack != null)
            {
                autoAttack.OnAttackResolved -= HandleAttackResolved;
            }
            StopEffect();
        }

        private void Update()
        {
            if (!playing)
            {
                return;
            }

            elapsed += Time.deltaTime;
            if (elapsed >= duration)
            {
                StopEffect();
                return;
            }

            int index = Mathf.Min(frames.Length - 1, (int)(elapsed / duration * frames.Length));
            effectRenderer.sprite = frames[index];
        }

        private void HandleAttackResolved()
        {
            if (frames == null || frames.Length == 0 || frames[0] == null || stats == null || autoAttack == null)
            {
                return;
            }

            // 261104 - 서버가 판정한 방향(연속 공격 · 연속 베기는 처음 방향 그대로). 없으면 현재 조준.
            Vector2 aimDir = autoAttack.LastResolvedDirection;
            if (aimDir.sqrMagnitude < 0.0001f)
            {
                aimDir = (Vector2)autoAttack.AimPosition - (Vector2)transform.position;
            }
            if (aimDir.sqrMagnitude < 0.0001f)
            {
                aimDir = Vector2.left;
            }
            aimDir.Normalize();

            // PlayerAutoAttack.ApplyHit과 같은 반경 - 따로 계산하면 보이는 범위와 맞는 범위가 어긋난다.
            // 261104 - 신통력(근접 사거리 · 넓은 베기)까지 들어간 실제 값.
            float hitRadius = autoAttack.HitRadius;

            bool flip = aimDir.x > 0f;
            effectRenderer.flipY = flip;
            if (!visibleBoundsReady)
            {
                visibleBoundsReady = true;
                visibleBounds = SpriteVisibleBounds.Of(frames);
            }

            if (visibleBounds.width > 0.0001f && visibleBounds.height > 0.0001f)
            {
                // 261102 - 실제 판정 부채꼴에 맞춤(예섭): 앞뒤(가로) = 캐릭터 위치 ~ 판정 반경, 옆(세로) = 부채꼴 현(attackArcDegrees).
                SpriteVisibleBounds.FitSlash(visibleBounds, Vector2.zero, aimDir, hitRadius, autoAttack.ArcDegrees, flip,
                                             out Vector2 center, out float fitAngle, out Vector2 fitScale);
                Vector3 parentScale = transform.lossyScale;
                float parentScaleX = Mathf.Abs(parentScale.x) > 0.0001f ? Mathf.Abs(parentScale.x) : 1f;
                effectTransform.localPosition = center / parentScaleX;
                effectTransform.localRotation = Quaternion.Euler(0f, 0f, fitAngle);
                effectTransform.localScale = new Vector3(fitScale.x / parentScaleX, fitScale.y / parentScaleX, 1f);
            }
            else
            {
                float angle = Mathf.Atan2(aimDir.y, aimDir.x) * Mathf.Rad2Deg - 180f; // 원본 "앞" = 왼쪽(-X)
                effectTransform.localPosition = aimDir * (hitRadius * forwardOffsetRatio);
                effectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
                float spriteHeight = frames[0].bounds.size.y;
                float scale = spriteHeight > 0.0001f ? hitRadius * 2f * sizeToRangeRatio / spriteHeight : 1f;
                effectTransform.localScale = new Vector3(scale, scale, 1f);
            }

            duration = Mathf.Max(minDuration, autoAttack.AttackInterval * (1f - autoAttack.HitDelayRatio));
            elapsed = 0f;
            playing = true;
            effectRenderer.sprite = frames[0];
            effectRenderer.enabled = true;
        }

        private void StopEffect()
        {
            playing = false;
            if (effectRenderer != null)
            {
                effectRenderer.enabled = false;
            }
        }
    }
}
