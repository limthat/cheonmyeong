using CheonMyeong.Data;
using CheonMyeong.Maps;
using CheonMyeong.Player;
using CheonMyeong.Progression;
using CheonMyeong.Waves;
using Unity.Netcode;
using UnityEngine;

namespace CheonMyeong.Enemies
{
    /// <summary>
    /// 261119 추가 - 중형 원귀 3종(기획서 v1.1 8-4 ~ 8-6)의 스킬 · 그림. Enemy와 같은 프리팹에 붙는다.
    /// - 아귀(Charge): 3칸 안이면 0.8초 멈춰 바닥에 붉은 직선 예고 → 3칸 돌진(피해 18 + 1칸 밀침), 벽에 부딪히면 1초 기절.
    /// - 물귀신(Submerge): 웅덩이에 잠긴 동안 원거리에 안 맞고 인식도 안 됨 · 공격 안 함. 2.5칸 안이면 솟아오르고, 멀어지면 다시 잠김.
    /// - 역귀(Throw): 6칸 안이면 멈춰 호리병을 던짐(0.8초 동작) → 맞으면 독 1 × 5틱(중첩 없이 시간만 갱신).
    /// 판정 · 상태는 서버, 그림(프레임 · 예고선 · 호리병)은 각 화면이 상태값을 보고 그린다. Animator 없이 프레임을 직접 넘긴다(10fps).
    /// 풀링으로 재사용되므로 ServerReset에서 전부 되돌린다. 매 프레임 할당 없음.
    /// </summary>
    [RequireComponent(typeof(Enemy))]
    public class EliteEnemy : NetworkBehaviour
    {
        public enum State : byte
        {
            Walk,
            ChargeWindup,
            Charging,
            Submerged,
            Rising,
            Surfaced,
            Sinking,
            Throwing,
        }

        private const float FrameSeconds = 0.1f;
        private const string MapLayerName = "Map";
        private const float JarHitRadius = 0.45f;
        private const int MaxJars = 2;

        [Header("그림 - 'CheonMyeong/Setup/Create Elite Enemies' 메뉴가 채운다")]
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private Sprite[] walkFrames;
        [SerializeField] private Sprite[] attackFrames;
        [SerializeField] private Sprite[] dieFrames;
        [SerializeField] private Sprite[] swimFrames;
        [SerializeField] private Sprite[] riseFrames;
        [SerializeField] private Sprite jarSprite;

        private readonly NetworkVariable<State> state = new NetworkVariable<State>(
            State.Walk, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<Vector2> chargeDir = new NetworkVariable<Vector2>(
            Vector2.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<byte> attackSerial = new NetworkVariable<byte>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private Enemy enemy;
        private Rigidbody2D rb;
        private EnemyStatsSO stats;

        // 서버
        private float stateTimer;
        private float skillCooldown;
        private Vector2 chargeStart;
        private bool chargeHit;
        private int mapMask;

        private struct Jar
        {
            public bool Alive;
            public Vector2 Position;
            public Vector2 Velocity;
            public float Left;
        }

        private readonly Jar[] jars = new Jar[MaxJars];

        // 화면
        private State shownState = (State)255;
        private float animTime;
        private byte shownAttackSerial;
        private float attackOverlayLeft;
        private bool shownDying;
        private SpriteRenderer telegraph;
        private readonly SpriteRenderer[] jarViews = new SpriteRenderer[MaxJars];
        private readonly Jar[] jarFx = new Jar[MaxJars];
        private static Sprite whitePixel;

        /// <summary>잠겨 있어서 원거리 공격 · 자동 조준 대상이 아님(물귀신).</summary>
        public bool HiddenFromRanged
        {
            get
            {
                State s = state.Value;
                return s == State.Submerged || s == State.Sinking || (s == State.Rising && !RiseFinished);
            }
        }

        /// <summary>접촉 피해를 줄 수 있는 상태인지(물귀신은 나와 있을 때만).</summary>
        public bool CanContact => stats == null || stats.skill != EliteSkill.Submerge || state.Value == State.Surfaced;

        private bool RiseFinished => riseFrames == null || riseFrames.Length == 0 || stateTimer <= 0f;

        private void Awake()
        {
            enemy = GetComponent<Enemy>();
            rb = GetComponent<Rigidbody2D>();
            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponent<SpriteRenderer>();
            }
            mapMask = LayerMask.GetMask(MapLayerName);
        }

        public override void OnNetworkSpawn()
        {
            stats = enemy.Stats;
            shownState = (State)255;
            shownAttackSerial = attackSerial.Value;
            shownDying = false;
            attackOverlayLeft = 0f;
            for (int i = 0; i < MaxJars; i++)
            {
                jarFx[i].Alive = false;
            }
        }

        public override void OnNetworkDespawn()
        {
            HideFx();
        }

        public override void OnDestroy()
        {
            if (telegraph != null)
            {
                Destroy(telegraph.gameObject);
            }
            for (int i = 0; i < MaxJars; i++)
            {
                if (jarViews[i] != null)
                {
                    Destroy(jarViews[i].gameObject);
                }
            }
            base.OnDestroy();
        }

        /// <summary>서버 전용 - 풀에서 꺼낼 때(Enemy.ServerInitialize).</summary>
        public void ServerReset()
        {
            stats = enemy != null ? enemy.Stats : stats;
            for (int i = 0; i < MaxJars; i++)
            {
                jars[i].Alive = false;
            }
            stateTimer = 0f;
            chargeHit = false;
            if (stats == null)
            {
                return;
            }
            switch (stats.skill)
            {
                case EliteSkill.Charge:
                    skillCooldown = stats.chargeFirstCooldown;
                    break;
                case EliteSkill.Throw:
                    skillCooldown = 0f;
                    break;
                default:
                    skillCooldown = 0f;
                    break;
            }
            state.Value = stats.skill == EliteSkill.Submerge ? State.Submerged : State.Walk;
            chargeDir.Value = Vector2.zero;
        }

        /// <summary>서버 전용 - 접촉 피해를 줬을 때(공격 그림 한 번).</summary>
        public void ServerOnContactHit()
        {
            unchecked
            {
                attackSerial.Value++;
            }
        }

        /// <summary>
        /// 서버 전용 - Enemy.FixedUpdate가 넉백 · 기절 처리 뒤 부른다. 이동을 직접 정했으면 true(평소 추적을 건너뜀).
        /// </summary>
        public bool ServerFixedStep(Transform target, float dt)
        {
            TickJars(dt);
            if (stats == null)
            {
                return false;
            }
            if (skillCooldown > 0f)
            {
                skillCooldown -= dt;
            }
            if (stateTimer > 0f)
            {
                stateTimer -= dt;
            }
            switch (stats.skill)
            {
                case EliteSkill.Charge: return StepCharge(target);
                case EliteSkill.Submerge: StepSubmerge(target); return false;
                case EliteSkill.Throw: return StepThrow(target);
            }
            return false;
        }

        // ───────── 아귀 ─────────

        private bool StepCharge(Transform target)
        {
            switch (state.Value)
            {
                case State.ChargeWindup:
                    rb.linearVelocity = Vector2.zero;
                    if (stateTimer <= 0f)
                    {
                        state.Value = State.Charging;
                        stateTimer = Mathf.Max(0.05f, stats.chargeSeconds);
                        chargeStart = rb.position;
                        chargeHit = false;
                    }
                    return true;
                case State.Charging:
                {
                    Vector2 dir = chargeDir.Value;
                    float speed = stats.chargeDistance / Mathf.Max(0.05f, stats.chargeSeconds);
                    float step = speed * Time.fixedDeltaTime;
                    if (HitsWall(rb.position, dir, step + 0.4f))
                    {
                        EndCharge();
                        enemy.ServerStun(stats.chargeWallStun); // 벽에 부딪히면 기절
                        return true;
                    }
                    rb.linearVelocity = dir * speed;
                    if (!chargeHit)
                    {
                        ChargeHitPlayers(dir);
                    }
                    if (stateTimer <= 0f || (rb.position - chargeStart).sqrMagnitude >= stats.chargeDistance * stats.chargeDistance)
                    {
                        EndCharge();
                    }
                    return true;
                }
            }
            if (target != null && skillCooldown <= 0f && !Boss.DeathRealm.IsActive)
            {
                Vector2 to = (Vector2)target.position - rb.position;
                if (to.sqrMagnitude <= stats.chargeDetectRange * stats.chargeDetectRange && to.sqrMagnitude > 0.0001f)
                {
                    chargeDir.Value = to.normalized;
                    state.Value = State.ChargeWindup;
                    stateTimer = stats.chargeWindup;
                    rb.linearVelocity = Vector2.zero;
                    return true;
                }
            }
            return false;
        }

        private void EndCharge()
        {
            rb.linearVelocity = Vector2.zero;
            state.Value = State.Walk;
            skillCooldown = stats.chargeCooldown;
        }

        private void ChargeHitPlayers(Vector2 dir)
        {
            float damage = stats.chargeDamage * DamageMultiplier;
            for (int i = 0; i < PlayerHealth.ActivePlayers.Count; i++)
            {
                PlayerHealth player = PlayerHealth.ActivePlayers[i];
                if (player == null || player.IsDead || !Boss.DeathRealm.CanInteract(gameObject, player.gameObject))
                {
                    continue;
                }
                if (((Vector2)player.transform.position - rb.position).sqrMagnitude > 0.8f * 0.8f)
                {
                    continue;
                }
                player.TakeDamage(damage);
                if (stats.chargeKnockback > 0f && player.TryGetComponent(out PlayerMovement movement))
                {
                    // PlayerMovement 넉백은 남은 비율의 제곱으로 줄어든다 → 이동 거리 = 속도 × 시간 / 3.
                    const float seconds = 0.2f;
                    movement.ServerKnockback(dir * (stats.chargeKnockback * 3f / seconds), seconds);
                }
                chargeHit = true;
                ServerOnContactHit();
            }
        }

        private bool HitsWall(Vector2 from, Vector2 dir, float distance)
        {
            if (mapMask != 0 && Physics2D.Raycast(from, dir, distance, mapMask).collider != null)
            {
                return true;
            }
            MapBounds bounds = MapBounds.Instance;
            return bounds != null && !bounds.PlayableArea.Contains(from + dir * distance);
        }

        // ───────── 물귀신 ─────────

        private void StepSubmerge(Transform target)
        {
            float distance = target != null ? Vector2.Distance(target.position, rb.position) : float.MaxValue;
            int riseFrameCount = riseFrames != null ? riseFrames.Length : 0;
            float riseSeconds = riseFrameCount * FrameSeconds;
            switch (state.Value)
            {
                case State.Submerged:
                    if (distance <= stats.surfaceRange)
                    {
                        state.Value = State.Rising;
                        stateTimer = riseSeconds;
                    }
                    break;
                case State.Rising:
                    if (stateTimer <= 0f)
                    {
                        state.Value = State.Surfaced;
                    }
                    break;
                case State.Surfaced:
                    if (distance > Mathf.Max(stats.surfaceRange, stats.submergeRange))
                    {
                        state.Value = State.Sinking;
                        stateTimer = riseSeconds;
                    }
                    break;
                case State.Sinking:
                    if (stateTimer <= 0f)
                    {
                        state.Value = State.Submerged;
                    }
                    break;
                default:
                    state.Value = State.Submerged;
                    break;
            }
        }

        // ───────── 역귀 ─────────

        private bool StepThrow(Transform target)
        {
            if (state.Value == State.Throwing)
            {
                rb.linearVelocity = Vector2.zero;
                if (stateTimer <= 0f)
                {
                    if (target != null)
                    {
                        SpawnJar((Vector2)target.position - rb.position);
                    }
                    state.Value = State.Walk;
                    skillCooldown = stats.throwCooldown;
                }
                return true;
            }
            if (target == null)
            {
                return false;
            }
            Vector2 to = (Vector2)target.position - rb.position;
            if (to.sqrMagnitude > stats.throwRange * stats.throwRange)
            {
                return false; // 6칸 밖이면 다가온다
            }
            rb.linearVelocity = Vector2.MoveTowards(rb.linearVelocity, Vector2.zero, 20f * Time.fixedDeltaTime);
            if (skillCooldown <= 0f)
            {
                state.Value = State.Throwing;
                stateTimer = stats.throwWindup;
                chargeDir.Value = to.normalized;
            }
            return true;
        }

        private void SpawnJar(Vector2 toTarget)
        {
            if (toTarget.sqrMagnitude < 0.0001f)
            {
                return;
            }
            for (int i = 0; i < MaxJars; i++)
            {
                if (jars[i].Alive)
                {
                    continue;
                }
                Vector2 velocity = toTarget.normalized * stats.throwSpeed;
                float travel = stats.throwRange + 2f;
                jars[i] = new Jar { Alive = true, Position = rb.position, Velocity = velocity, Left = travel / Mathf.Max(0.1f, stats.throwSpeed) };
                JarFxRpc((byte)i, rb.position, velocity, jars[i].Left);
                return;
            }
        }

        private void TickJars(float dt)
        {
            for (int i = 0; i < MaxJars; i++)
            {
                if (!jars[i].Alive)
                {
                    continue;
                }
                ref Jar jar = ref jars[i];
                jar.Position += jar.Velocity * dt;
                jar.Left -= dt;
                if (jar.Left <= 0f)
                {
                    jar.Alive = false;
                    continue;
                }
                for (int p = 0; p < PlayerHealth.ActivePlayers.Count; p++)
                {
                    PlayerHealth player = PlayerHealth.ActivePlayers[p];
                    if (player == null || player.IsDead || !Boss.DeathRealm.CanInteract(gameObject, player.gameObject))
                    {
                        continue;
                    }
                    if (((Vector2)player.transform.position - jar.Position).sqrMagnitude <= JarHitRadius * JarHitRadius)
                    {
                        player.ServerApplyPoison(stats.poisonDamage * DamageMultiplier, stats.poisonTicks);
                        jar.Alive = false;
                        JarEndRpc((byte)i);
                        break;
                    }
                }
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void JarFxRpc(byte index, Vector2 position, Vector2 velocity, float seconds)
        {
            if (index >= MaxJars)
            {
                return;
            }
            jarFx[index] = new Jar { Alive = true, Position = position, Velocity = velocity, Left = seconds };
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void JarEndRpc(byte index)
        {
            if (index < MaxJars)
            {
                jarFx[index].Alive = false;
            }
        }

        private static float DamageMultiplier => WaveManager.Instance != null ? WaveManager.Instance.CurrentDamageMultiplier : 1f;

        // ───────── 그림(모든 화면) ─────────

        private void Update()
        {
            if (!IsSpawned || spriteRenderer == null)
            {
                return;
            }
            bool paused = PartyProgress.Instance != null && PartyProgress.Instance.IsPaused;
            float dt = paused ? 0f : Time.deltaTime;
            UpdateJarViews(dt);

            if (enemy.IsDying)
            {
                if (!shownDying)
                {
                    shownDying = true;
                    animTime = 0f;
                    HideTelegraph();
                }
                animTime += dt;
                SetFrame(dieFrames, animTime, false);
                return;
            }
            shownDying = false;

            State s = state.Value;
            if (s != shownState)
            {
                shownState = s;
                animTime = 0f;
            }
            animTime += dt;

            if (attackSerial.Value != shownAttackSerial)
            {
                shownAttackSerial = attackSerial.Value;
                attackOverlayLeft = attackFrames != null ? attackFrames.Length * FrameSeconds : 0f;
            }

            UpdateTelegraph(s);
            FaceMovement(s);

            switch (s)
            {
                case State.ChargeWindup:
                case State.Charging:
                    SetFrame(walkFrames, animTime * 2f, true); // 돌진: WALK 2배속
                    return;
                case State.Submerged:
                    SetFrame(swimFrames, animTime, true);
                    return;
                case State.Rising:
                    SetFrame(riseFrames, animTime, false);
                    return;
                case State.Sinking:
                    SetFrameReversed(riseFrames, animTime);
                    return;
                case State.Throwing:
                    SetFrame(attackFrames, animTime * AttackSpeedFor(stats != null ? stats.throwWindup : 0.8f), false);
                    return;
            }
            if (attackOverlayLeft > 0f && attackFrames != null && attackFrames.Length > 0)
            {
                float total = attackFrames.Length * FrameSeconds;
                SetFrame(attackFrames, total - attackOverlayLeft, false);
                attackOverlayLeft -= dt;
                return;
            }
            SetFrame(walkFrames, animTime, true);
        }

        /// <summary>던지기 동작이 예고 시간 안에 끝나게 재생 속도를 맞춘다.</summary>
        private float AttackSpeedFor(float windup)
        {
            int n = attackFrames != null ? attackFrames.Length : 0;
            return n > 0 && windup > 0.01f ? Mathf.Max(1f, n * FrameSeconds / windup) : 1f;
        }

        private void SetFrame(Sprite[] frames, float time, bool loop)
        {
            if (frames == null || frames.Length == 0)
            {
                return;
            }
            int index = Mathf.FloorToInt(time / FrameSeconds);
            index = loop ? index % frames.Length : Mathf.Min(index, frames.Length - 1);
            if (frames[index] != null && spriteRenderer.sprite != frames[index])
            {
                spriteRenderer.sprite = frames[index];
            }
        }

        private void SetFrameReversed(Sprite[] frames, float time)
        {
            if (frames == null || frames.Length == 0)
            {
                return;
            }
            int index = frames.Length - 1 - Mathf.Min(Mathf.FloorToInt(time / FrameSeconds), frames.Length - 1);
            if (frames[index] != null)
            {
                spriteRenderer.sprite = frames[index];
            }
        }

        private Vector2 lastPosition;

        private void FaceMovement(State s)
        {
            Vector2 pos = transform.position;
            float dx = s == State.ChargeWindup || s == State.Charging || s == State.Throwing ? chargeDir.Value.x : pos.x - lastPosition.x;
            lastPosition = pos;
            if (Mathf.Abs(dx) > 0.001f)
            {
                spriteRenderer.flipX = dx > 0f; // 그림이 왼쪽을 보고 있어서 오른쪽으로 갈 때 뒤집는다
            }
        }

        private void UpdateTelegraph(State s)
        {
            if (s != State.ChargeWindup || stats == null)
            {
                HideTelegraph();
                return;
            }
            if (telegraph == null)
            {
                var go = new GameObject("ChargeTelegraph");
                go.transform.SetParent(transform.parent, false);
                telegraph = go.AddComponent<SpriteRenderer>();
                telegraph.sprite = WhitePixel;
                telegraph.sortingOrder = spriteRenderer.sortingOrder - 1;
                if (spriteRenderer.sharedMaterial != null)
                {
                    telegraph.sharedMaterial = spriteRenderer.sharedMaterial;
                }
            }
            Vector2 dir = chargeDir.Value;
            float length = stats.chargeDistance;
            telegraph.enabled = true;
            telegraph.transform.position = (Vector2)transform.position + dir * (length * 0.5f);
            telegraph.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
            telegraph.transform.localScale = new Vector3(length, 1f, 1f); // 폭 1칸 · 길이 3칸
            float pulse = 0.25f + 0.15f * Mathf.Sin(animTime * 20f);
            telegraph.color = new Color(0.9f, 0.12f, 0.1f, pulse);
        }

        private void HideTelegraph()
        {
            if (telegraph != null && telegraph.enabled)
            {
                telegraph.enabled = false;
            }
        }

        private void UpdateJarViews(float dt)
        {
            for (int i = 0; i < MaxJars; i++)
            {
                ref Jar jar = ref jarFx[i];
                if (jar.Alive)
                {
                    jar.Position += jar.Velocity * dt;
                    jar.Left -= dt;
                    if (jar.Left <= 0f)
                    {
                        jar.Alive = false;
                    }
                }
                SpriteRenderer view = jarViews[i];
                if (!jar.Alive)
                {
                    if (view != null && view.enabled)
                    {
                        view.enabled = false;
                    }
                    continue;
                }
                if (view == null)
                {
                    if (jarSprite == null)
                    {
                        continue;
                    }
                    var go = new GameObject("Horibyeong");
                    go.transform.SetParent(transform.parent, false);
                    view = go.AddComponent<SpriteRenderer>();
                    view.sprite = jarSprite;
                    view.sortingOrder = spriteRenderer.sortingOrder + 1;
                    if (spriteRenderer.sharedMaterial != null)
                    {
                        view.sharedMaterial = spriteRenderer.sharedMaterial;
                    }
                    jarViews[i] = view;
                }
                view.enabled = true;
                view.transform.position = jar.Position;
                view.transform.Rotate(0f, 0f, -720f * dt); // 빙글빙글(회전은 코드)
            }
        }

        private void HideFx()
        {
            HideTelegraph();
            for (int i = 0; i < MaxJars; i++)
            {
                jarFx[i].Alive = false;
                if (jarViews[i] != null)
                {
                    jarViews[i].enabled = false;
                }
            }
        }

        private static Sprite WhitePixel
        {
            get
            {
                if (whitePixel == null)
                {
                    var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.DontSave };
                    tex.SetPixel(0, 0, Color.white);
                    tex.Apply(false, true);
                    whitePixel = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
                    whitePixel.hideFlags = HideFlags.DontSave;
                }
                return whitePixel;
            }
        }
    }
}
