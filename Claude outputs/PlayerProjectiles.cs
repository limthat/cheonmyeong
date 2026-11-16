using System.Collections.Generic;
using CheonMyeong.Combat;
using CheonMyeong.Data;
using CheonMyeong.Progression;
using Unity.Netcode;
using UnityEngine;

namespace CheonMyeong.Player
{
    /// <summary>
    /// 261114 추가 - 원거리 캐릭터 투사체(한주원 부적 · 이아라 신칼 · 최환조 화살 · 심여레 바람).
    /// - 판정은 서버만: 투사체를 직접 움직이며 겹친 원귀에게 PlayerAutoAttack.ServerDamageTarget(공통 피해 · 치명 · 넉백 · 패시브).
    /// - 그림은 전원: 서버가 쏠 때 FireVisualRpc로 시작 정보만 보내고 각자 같은 규칙으로 날린다(위치 매 틱 동기화 없음).
    ///   서버에서 투사체가 끝나면(관통 소진) EndVisualRpc로 그 그림을 지운다.
    /// - 그림은 왼쪽을 향해 그려진 원본(기획서 11장). 날아가는 방향으로 돌리고, 오른쪽으로 갈 땐 위아래가 뒤집히지 않게 flipY.
    ///   돌아가는 투사체(부적 · 신칼)는 방향 대신 계속 회전한다.
    /// 배열 고정 크기 · 풀링이라 매 프레임 할당 없음(RPC 직렬화 제외).
    /// </summary>
    [RequireComponent(typeof(PlayerAutoAttack))]
    public class PlayerProjectiles : NetworkBehaviour
    {
        private const int MaxShots = 48;
        private const int MaxHitsPerShot = 24;
        private const float SpreadDegrees = 8f;
        private const float RangeTravelMultiplier = 1.15f;
        private const ulong NoTarget = ulong.MaxValue;

        [SerializeField] private LayerMask enemyLayer = ~0;
        [SerializeField] private int sortingOrder = 21;

        /// <summary>한 발의 성질. 기본 공격은 캐릭터 스탯에서 만들고, 전용 신통력 · 각성이 직접 만들어 쏠 수도 있다.</summary>
        public struct ShotParams
        {
            public float Speed;
            public float Range;
            public int Pierce;          // -1 = 무제한
            public float DamageScale;
            public float Knockback;
            public float HomingDegreesPerSecond;
            public bool Returns;        // 끝까지 가면 주인에게 돌아오며 한 번 더 때린다(돌아오는 신칼)
            public float ReturnDamageScale;
            public bool ForceCritOnReturn;
            public float Radius;
            public float Scale;
            public float Spin;
        }

        private struct Shot
        {
            public bool Alive;
            public ushort Id;
            public Vector2 Position;
            public Vector2 Direction;
            public float RangeLeft;
            public ShotParams P;
            public Transform Target;
            public int HitCount;
            public bool Returning;
        }

        private struct Visual
        {
            public bool Alive;
            public ushort Id;
            public Vector2 Position;
            public Vector2 Direction;
            public float RangeLeft;
            public float Speed;
            public float Homing;
            public bool Returns;
            public bool Returning;
            public float Spin;
            public float Angle;
            public Transform Target;
            public SpriteRenderer Renderer;
        }

        public struct ShotVisual : INetworkSerializable
        {
            public ushort Id;
            public Vector2 Origin;
            public Vector2 Direction;
            public float Speed;
            public float Range;
            public float Homing;
            public float Spin;
            public float Scale;
            public bool Returns;
            public ulong TargetId;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref Id);
                serializer.SerializeValue(ref Origin);
                serializer.SerializeValue(ref Direction);
                serializer.SerializeValue(ref Speed);
                serializer.SerializeValue(ref Range);
                serializer.SerializeValue(ref Homing);
                serializer.SerializeValue(ref Spin);
                serializer.SerializeValue(ref Scale);
                serializer.SerializeValue(ref Returns);
                serializer.SerializeValue(ref TargetId);
            }
        }

        private readonly Shot[] shots = new Shot[MaxShots];
        private readonly int[] hitIds = new int[MaxShots * MaxHitsPerShot];
        private readonly Visual[] visuals = new Visual[MaxShots];
        private readonly Collider2D[] buffer = new Collider2D[32];
        private readonly List<HitFeedback> feedback = new List<HitFeedback>(32);
        private readonly List<Collider2D> targetBuffer = new List<Collider2D>(8);
        private ContactFilter2D filter;
        private PlayerAutoAttack attack;
        private Sprite sprite;
        private ushort nextId = 1;

        /// <summary>투사체 수 추가(흩날림 · 신통력). 패시브/신통력 단계가 채운다.</summary>
        public int ExtraProjectiles { get; set; }
        /// <summary>투사체 속도 추가(비율).</summary>
        public float SpeedBonus { get; set; }
        /// <summary>관통 추가(철전 · 벽사부).</summary>
        public int ExtraPierce { get; set; }
        /// <summary>넉백 추가(칸).</summary>
        public float KnockbackBonus { get; set; }
        /// <summary>넉백 배율 추가(비율, 심여레 패시브 · 신내림).</summary>
        public float KnockbackScale { get; set; }

        /// <summary>서버에서 기본 공격 투사체를 쏠 때마다(발사 횟수 세는 전용 신통력 - 휘몰이 등).</summary>
        public event System.Action<Vector2> ServerFired;

        private void Awake()
        {
            attack = GetComponent<PlayerAutoAttack>();
            filter = new ContactFilter2D();
            filter.SetLayerMask(attack != null && attack.EnemyLayer.value != 0 ? attack.EnemyLayer : enemyLayer);
            filter.useTriggers = true;
        }

        /// <summary>PlayerCharacter가 캐릭터를 정할 때 부른다.</summary>
        public void SetSprite(Sprite value)
        {
            sprite = value;
        }

        /// <summary>서버 전용 - 캐릭터 스탯 기준 기본 공격 한 번.</summary>
        public void ServerFire(Vector2 aimDir)
        {
            CharacterStatsSO stats = attack != null ? attack.Stats : null;
            if (!IsServer || stats == null)
            {
                return;
            }
            Vector2 origin = transform.position;
            if (aimDir.sqrMagnitude < 0.0001f)
            {
                aimDir = Vector2.left;
            }
            aimDir.Normalize();

            ShotParams p = DefaultParams(stats);
            int count = Mathf.Max(1, stats.projectileCount + ExtraProjectiles);
            int targets = stats.autoTargets > 0 ? FindNearestTargets(origin, p.Range, stats.autoTargets) : 0;
            for (int i = 0; i < count; i++)
            {
                Transform target = null;
                Vector2 dir;
                if (targets > 0)
                {
                    Collider2D col = targetBuffer[i % targets];
                    target = col.transform;
                    dir = ((Vector2)target.position - origin).normalized;
                }
                else
                {
                    float offset = (i - (count - 1) * 0.5f) * SpreadDegrees;
                    dir = Quaternion.Euler(0f, 0f, offset) * aimDir;
                }
                ServerSpawnShot(origin, dir, p, target);
            }
            ServerFired?.Invoke(aimDir);
        }

        public ShotParams DefaultParams(CharacterStatsSO stats)
        {
            int pierce = stats.projectilePierce < 0 ? -1 : stats.projectilePierce + ExtraPierce;
            return new ShotParams
            {
                Speed = stats.projectileSpeed * (1f + SpeedBonus),
                Range = (stats.attackRange + attack.RangeBonus) * RangeTravelMultiplier,
                Pierce = pierce,
                DamageScale = 1f,
                Knockback = (stats.knockback + KnockbackBonus) * (1f + KnockbackScale),
                HomingDegreesPerSecond = stats.homingDegreesPerSecond,
                Radius = stats.projectileRadius,
                Scale = stats.projectileScale,
                Spin = stats.projectileSpinDegreesPerSecond,
            };
        }

        /// <summary>서버 전용 - 한 발. 전용 신통력 · 각성도 쓴다.</summary>
        public void ServerSpawnShot(Vector2 origin, Vector2 dir, ShotParams p, Transform target)
        {
            if (!IsServer || dir.sqrMagnitude < 0.0001f)
            {
                return;
            }
            int slot = FreeShotSlot();
            if (slot < 0)
            {
                return;
            }
            ushort id = nextId++;
            if (nextId == 0)
            {
                nextId = 1;
            }
            dir.Normalize();
            shots[slot] = new Shot
            {
                Alive = true,
                Id = id,
                Position = origin,
                Direction = dir,
                RangeLeft = p.Range,
                P = p,
                Target = target,
                HitCount = 0,
            };
            ulong targetId = NoTarget;
            if (target != null && target.TryGetComponent(out NetworkObject netObj))
            {
                targetId = netObj.NetworkObjectId;
            }
            FireVisualRpc(new ShotVisual
            {
                Id = id,
                Origin = origin,
                Direction = dir,
                Speed = p.Speed,
                Range = p.Range,
                Homing = p.HomingDegreesPerSecond,
                Spin = p.Spin,
                Scale = p.Scale,
                Returns = p.Returns,
                TargetId = targetId,
            });
        }

        private int FreeShotSlot()
        {
            for (int i = 0; i < MaxShots; i++)
            {
                if (!shots[i].Alive)
                {
                    return i;
                }
            }
            return -1;
        }

        private int FindNearestTargets(Vector2 origin, float range, int count)
        {
            targetBuffer.Clear();
            int n = Physics2D.OverlapCircle(origin, range, filter, buffer);
            for (int i = 0; i < n; i++)
            {
                Collider2D col = buffer[i];
                if (!Boss.DeathRealm.CanInteract(gameObject, col.gameObject) || !col.TryGetComponent(out IDamageable _))
                {
                    continue;
                }
                if (col.TryGetComponent(out Enemies.Enemy enemy) && enemy.IsDying)
                {
                    continue;
                }
                float d = ((Vector2)col.transform.position - origin).sqrMagnitude;
                int insertAt = targetBuffer.Count;
                for (int j = 0; j < targetBuffer.Count; j++)
                {
                    if (d < ((Vector2)targetBuffer[j].transform.position - origin).sqrMagnitude)
                    {
                        insertAt = j;
                        break;
                    }
                }
                if (insertAt < count)
                {
                    targetBuffer.Insert(insertAt, col);
                    if (targetBuffer.Count > count)
                    {
                        targetBuffer.RemoveAt(targetBuffer.Count - 1);
                    }
                }
            }
            return targetBuffer.Count;
        }

        private void Update()
        {
            bool paused = PartyProgress.Instance != null && PartyProgress.Instance.IsPaused;
            if (paused)
            {
                return;
            }
            float dt = Time.deltaTime;
            if (IsServer)
            {
                ServerTick(dt);
            }
            TickVisuals(dt);
        }

        private void ServerTick(float dt)
        {
            for (int i = 0; i < MaxShots; i++)
            {
                if (!shots[i].Alive)
                {
                    continue;
                }
                ref Shot s = ref shots[i];
                Steer(ref s.Direction, s.Position, s.Target, s.P.HomingDegreesPerSecond, dt, s.Returning);
                if (s.Returning)
                {
                    Vector2 home = transform.position;
                    s.Direction = (home - s.Position).normalized;
                    if ((home - s.Position).sqrMagnitude < 0.25f)
                    {
                        EndShot(i);
                        continue;
                    }
                }
                float step = s.P.Speed * dt;
                s.Position += s.Direction * step;
                if (!s.Returning)
                {
                    s.RangeLeft -= step;
                }

                if (HitOverlaps(i, ref s))
                {
                    EndShot(i);
                    continue;
                }
                if (!s.Returning && s.RangeLeft <= 0f)
                {
                    if (s.P.Returns)
                    {
                        s.Returning = true;
                        s.HitCount = 0; // 돌아오는 길에 같은 원귀를 한 번 더 때릴 수 있다
                        s.Target = null;
                    }
                    else
                    {
                        EndShot(i);
                    }
                }
            }
            attack.ServerFlushHitFeedback(feedback);
        }

        /// <summary>겹친 원귀를 때린다. 관통이 다 떨어졌으면 true.</summary>
        private bool HitOverlaps(int index, ref Shot s)
        {
            int n = Physics2D.OverlapCircle(s.Position, s.P.Radius, filter, buffer);
            for (int k = 0; k < n; k++)
            {
                Collider2D col = buffer[k];
                if (!Boss.DeathRealm.CanInteract(gameObject, col.gameObject))
                {
                    continue;
                }
                int key = col.GetInstanceID();
                if (AlreadyHit(index, s.HitCount, key))
                {
                    continue;
                }
                float scale = s.Returning ? s.P.ReturnDamageScale : s.P.DamageScale;
                if (s.Returning && scale <= 0f)
                {
                    continue;
                }
                if (s.Returning && s.P.ForceCritOnReturn)
                {
                    attack.ForceNextCrit = true;
                }
                if (!attack.ServerDamageTarget(col, scale, s.Direction, s.P.Knockback, true, feedback))
                {
                    attack.ForceNextCrit = false;
                    continue;
                }
                attack.ForceNextCrit = false;
                if (s.HitCount < MaxHitsPerShot)
                {
                    hitIds[index * MaxHitsPerShot + s.HitCount] = key;
                    s.HitCount++;
                }
                if (s.Returning)
                {
                    continue; // 돌아오는 길은 관통 제한 없이 지나간다
                }
                if (s.P.Pierce >= 0)
                {
                    if (s.P.Pierce == 0)
                    {
                        if (s.P.Returns)
                        {
                            s.Returning = true;
                            s.HitCount = 0;
                            hitIds[index * MaxHitsPerShot] = key; // 방금 맞은 원귀는 돌아오는 첫 순간 바로 또 맞지 않게
                            s.HitCount = 1;
                            s.Target = null;
                            return false;
                        }
                        return true;
                    }
                    s.P.Pierce--;
                }
            }
            return false;
        }

        private bool AlreadyHit(int index, int count, int key)
        {
            int start = index * MaxHitsPerShot;
            for (int i = 0; i < count; i++)
            {
                if (hitIds[start + i] == key)
                {
                    return true;
                }
            }
            return false;
        }

        private void EndShot(int index)
        {
            ushort id = shots[index].Id;
            shots[index].Alive = false;
            shots[index].Target = null;
            EndVisualRpc(id);
        }

        private static void Steer(ref Vector2 direction, Vector2 position, Transform target, float homing, float dt, bool returning)
        {
            if (returning || homing <= 0f || target == null || !target.gameObject.activeInHierarchy)
            {
                return;
            }
            Vector2 want = (Vector2)target.position - position;
            if (want.sqrMagnitude < 0.0001f)
            {
                return;
            }
            float angle = Vector2.SignedAngle(direction, want);
            float maxTurn = homing * dt;
            float turn = Mathf.Clamp(angle, -maxTurn, maxTurn);
            direction = (Quaternion.Euler(0f, 0f, turn) * direction).normalized;
        }

        // ───────── 연출(전원) ─────────

        [Rpc(SendTo.ClientsAndHost)]
        private void FireVisualRpc(ShotVisual v)
        {
            if (sprite == null)
            {
                return;
            }
            int slot = -1;
            for (int i = 0; i < MaxShots; i++)
            {
                if (!visuals[i].Alive)
                {
                    slot = i;
                    break;
                }
            }
            if (slot < 0)
            {
                return;
            }
            SpriteRenderer sr = visuals[slot].Renderer;
            if (sr == null)
            {
                var go = new GameObject("Projectile");
                go.transform.SetParent(transform.parent, false); // 캐릭터를 따라 움직이지 않게 같은 부모(월드)에 둔다
                sr = go.AddComponent<SpriteRenderer>();
                sr.sortingOrder = sortingOrder;
            }
            Transform target = null;
            if (v.TargetId != NoTarget && NetworkManager != null && NetworkManager.SpawnManager != null
                && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(v.TargetId, out NetworkObject obj))
            {
                target = obj.transform;
            }
            sr.sprite = sprite;
            sr.enabled = true;
            sr.transform.localScale = new Vector3(v.Scale, v.Scale, 1f);
            sr.transform.position = v.Origin;
            visuals[slot] = new Visual
            {
                Alive = true,
                Id = v.Id,
                Position = v.Origin,
                Direction = v.Direction,
                RangeLeft = v.Range,
                Speed = v.Speed,
                Homing = v.Homing,
                Returns = v.Returns,
                Spin = v.Spin,
                Angle = 0f,
                Target = target,
                Renderer = sr,
            };
            ApplyRotation(ref visuals[slot]);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void EndVisualRpc(ushort id)
        {
            for (int i = 0; i < MaxShots; i++)
            {
                if (visuals[i].Alive && visuals[i].Id == id)
                {
                    HideVisual(i);
                    return;
                }
            }
        }

        private void TickVisuals(float dt)
        {
            for (int i = 0; i < MaxShots; i++)
            {
                if (!visuals[i].Alive)
                {
                    continue;
                }
                ref Visual v = ref visuals[i];
                Steer(ref v.Direction, v.Position, v.Target, v.Homing, dt, v.Returning);
                if (v.Returning)
                {
                    Vector2 home = transform.position;
                    if ((home - v.Position).sqrMagnitude < 0.25f)
                    {
                        HideVisual(i);
                        continue;
                    }
                    v.Direction = (home - v.Position).normalized;
                }
                float step = v.Speed * dt;
                v.Position += v.Direction * step;
                if (!v.Returning)
                {
                    v.RangeLeft -= step;
                    if (v.RangeLeft <= 0f)
                    {
                        if (v.Returns)
                        {
                            v.Returning = true;
                        }
                        else
                        {
                            HideVisual(i);
                            continue;
                        }
                    }
                }
                v.Angle += v.Spin * dt;
                v.Renderer.transform.position = v.Position;
                ApplyRotation(ref v);
            }
        }

        private static void ApplyRotation(ref Visual v)
        {
            Transform t = v.Renderer.transform;
            if (v.Spin != 0f)
            {
                t.rotation = Quaternion.Euler(0f, 0f, v.Angle);
                v.Renderer.flipY = false;
                return;
            }
            // 원본 "앞" = 왼쪽(-X). 오른쪽으로 날아가면 위아래가 뒤집히지 않게 flipY.
            float angle = Mathf.Atan2(v.Direction.y, v.Direction.x) * Mathf.Rad2Deg - 180f;
            t.rotation = Quaternion.Euler(0f, 0f, angle);
            v.Renderer.flipY = v.Direction.x > 0f;
        }

        private void HideVisual(int index)
        {
            visuals[index].Alive = false;
            visuals[index].Target = null;
            if (visuals[index].Renderer != null)
            {
                visuals[index].Renderer.enabled = false;
            }
        }

        public override void OnNetworkDespawn()
        {
            for (int i = 0; i < MaxShots; i++)
            {
                shots[i].Alive = false;
                shots[i].Target = null;
                HideVisual(i);
            }
        }

        public override void OnDestroy()
        {
            for (int i = 0; i < MaxShots; i++)
            {
                if (visuals[i].Renderer != null)
                {
                    Destroy(visuals[i].Renderer.gameObject);
                    visuals[i].Renderer = null;
                }
            }
            base.OnDestroy();
        }
    }
}
