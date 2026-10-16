using CheonMyeong.Data;
using UnityEngine;

namespace CheonMyeong.Player
{
    /// <summary>
    /// 하영이 무기를 휘두를 때 실제 피해 판정 범위(부채꼴)를 반투명 도형으로 잠깐
    /// 보여주는 확인용 연출(261013 추가, 예섭 요청 - "대미지 들어가는 이펙트 더미 도형으로
    /// 그 범위 정확히 맞춰서 보여줘, 알파값 낮춰서 반투명하게").
    ///
    /// PlayerAutoAttack.Attack()이 실제로 쓰는 반경(stats.attackRange *
    /// stats.attackAwarenessRangeMultiplier)과 각도(stats.attackArcDegrees)를 그대로
    /// 재사용해서 그린다 - 숫자를 새로 짓는 게 아니라 이미 코드가 판정에 쓰고 있는 값을
    /// 눈에 보이게 시각화만 한다. PlayerAutoAttack.OnDrawGizmosSelected가 이미 똑같은
    /// 반경/각도를 Scene 뷰 전용으로 그리고 있는데, 그건 "오브젝트를 선택했을 때만 +
    /// 에디터에서만" 보여서 실제 플레이 화면(Game 뷰/빌드)에서는 안 보인다 - 그래서
    /// Gizmos가 아니라 실제 렌더링되는 MeshRenderer로 별도 구현했다.
    ///
    /// 절차적 메시(부채꼴 Triangle Fan)를 공격할 때마다 다시 그린다 - 조준 방향이 매번
    /// 달라지므로 고정 메시를 하나 만들어두고 회전만 시키는 대신 매번 꼭짓점을 새로
    /// 계산한다(공격 빈도가 초당 몇 회 수준이라 메시 재계산 비용은 무시 가능).
    ///
    /// 네트워크 동기화를 따로 추가하지 않은 이유 - PlayerAutoAttack.OnAttackResolved(261014
    /// 추가)는 이미 서버(ApplyHit 내부)→ClientsAndHost Rpc(AttackResolvedVisualRpc)로 모든
    /// 화면에서 동시에 울리는 신호라(PlayerAutoAttack.cs 주석 참고), 이 컴포넌트가 각자
    /// 로컬에서 그 이벤트만 구독하면 호스트/참가자 화면에 전부 똑같이(각자 자기 화면
    /// 기준으로) 보인다. 공격 모션/이펙트 자체가 아직 미정이라 "확인용" 성격이 강한 디버그
    /// 연출이므로, 기획 확정 전까지 계속 켜둬도 되고 나중에 이 컴포넌트를 통째로 빼도
    /// PlayerAutoAttack 쪽 코드는 전혀 안 건드려도 된다(OnAttackResolved 구독만 끊어지는 구조).
    /// </summary>
    [RequireComponent(typeof(PlayerAutoAttack))]
    public class AttackRangeDebugVisual : MonoBehaviour
    {
        [SerializeField] private CharacterStatsSO stats;
        [SerializeField] private PlayerAutoAttack autoAttack;

        [Tooltip("부채꼴을 몇 개의 삼각형으로 쪼갤지 - 높을수록 호(弧)가 매끈해지지만 비용도 늘어난다.\n" +
                 "공격 판정 자체의 정밀도와는 무관(순수 시각적 매끈함 설정).")]
        [SerializeField] private int arcSegments = 20;

        [Tooltip("한 번 휘두를 때 이 도형이 화면에 떠 있는 시간(초) - 공격 모션/이펙트 자체가 아직\n" +
                 "미정이라 모션 길이에 맞출 기준이 없다. 범위를 잠깐 확인하는 용도라 임의의 짧은\n" +
                 "고정값으로 둔다(느낌 보고 조정 가능).")]
        [SerializeField] private float visibleDuration = 0.15f;

        [Tooltip("도형 색 - 알파(투명도)를 낮게 둬서 반투명하게(예섭 요청).")]
        [SerializeField] private Color fillColor = new Color(1f, 0.15f, 0.15f, 0.25f);

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private Mesh arcMesh;
        private float hideTimer;

        private void Awake()
        {
            if (autoAttack == null)
            {
                autoAttack = GetComponent<PlayerAutoAttack>();
            }

            arcMesh = new Mesh { name = "AttackRangeDebugMesh" };

            // 확인된 사실 - Unity는 같은 GameObject에 Renderer 계열 컴포넌트를 두 개 이상
            // 둘 수 없다(SpriteRenderer + MeshRenderer 동시 불가, Rigidbody/Collider와는
            // 다른 제약). 하영 루트 GameObject에는 이미 캐릭터 스프라이트용 SpriteRenderer가
            // 있으므로, MeshFilter/MeshRenderer는 별도의 자식 오브젝트를 만들어 거기에 붙인다
            // (로컬 좌표만 공유하면 되므로 위치/회전은 부모 그대로 둬도 충분 - 부채꼴 자체를
            // 매번 로컬 (0,0) 기준으로 다시 계산해서 그린다).
            GameObject visualChild = new GameObject("AttackRangeDebugVisual_Mesh");
            visualChild.transform.SetParent(transform, false);

            meshFilter = visualChild.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = arcMesh;

            meshRenderer = visualChild.AddComponent<MeshRenderer>();
            // Sprites/Default - 라이팅 없이 알파 블렌딩을 지원하는 내장 셰이더라
            // 반투명 디버그 도형용으로 별도 셰이더/머티리얼 에셋을 새로 만들 필요가 없다.
            meshRenderer.sharedMaterial = new Material(Shader.Find("Sprites/Default"))
            {
                color = fillColor
            };
            // 캐릭터/적 SpriteRenderer와 같은 기본(Default) 소팅 레이어를 쓰되, order를
            // 높여서 그 위에 그려지게 한다 - 이 프로젝트엔 아직 별도 소팅 레이어 체계가
            // 없어서(코드 전체 검색 확인) 새 레이어를 만들지 않고 order만 조정.
            meshRenderer.sortingOrder = 50;
            meshRenderer.enabled = false;
        }

        private void OnEnable()
        {
            if (autoAttack != null)
            {
                // 261014 수정 - 예섭 지적("빨간 원뿔 생기는 타이밍이 실제 대미지 타이밍이랑
                // 안 맞는다")으로 구독 대상을 OnAttackTriggered(모션 트리거 시점, 예전 즉발
                // 판정 시점 그대로)에서 OnAttackResolved(PlayerAutoAttack.ApplyHit() 실행
                // 시점 = pendingHitDelay 적용 후 실제 피해가 들어가는 순간)로 변경. 이제
                // 이 디버그 도형이 뜨는 순간과 실제 TakeDamage가 호출되는 순간이 정확히 일치한다.
                autoAttack.OnAttackResolved += HandleAttackResolved;
            }
        }

        private void OnDisable()
        {
            if (autoAttack != null)
            {
                autoAttack.OnAttackResolved -= HandleAttackResolved;
            }
        }

        private void OnDestroy()
        {
            if (arcMesh != null)
            {
                Destroy(arcMesh);
            }
        }

        private void Update()
        {
            if (meshRenderer == null || !meshRenderer.enabled)
            {
                return;
            }

            hideTimer -= Time.deltaTime;
            if (hideTimer <= 0f)
            {
                meshRenderer.enabled = false;
            }
        }

        private void HandleAttackResolved()
        {
            if (stats == null || autoAttack == null)
            {
                return;
            }

            Vector2 aimDir = (Vector2)autoAttack.AimPosition - (Vector2)transform.position;
            if (aimDir.sqrMagnitude < 0.0001f)
            {
                aimDir = Vector2.right;
            }
            aimDir.Normalize();

            // PlayerAutoAttack.Attack()이 실제 타격 판정에 쓰는 것과 완전히 같은 두 값 -
            // 하나라도 다르면 "눈에 보이는 범위"와 "실제로 맞는 범위"가 어긋나버리므로
            // 절대 따로 계산하지 않고 그대로 가져온다.
            float radius = stats.attackRange * stats.attackAwarenessRangeMultiplier;
            float halfArcDegrees = stats.attackArcDegrees * 0.5f;

            BuildArcMesh(aimDir, radius, halfArcDegrees);

            hideTimer = visibleDuration;
            meshRenderer.enabled = true;
        }

        /// <summary>
        /// 부채꼴을 삼각형 팬(Triangle Fan)으로 만든다 - 중심(로컬 원점) 1개 + 호를
        /// arcSegments개 선분으로 쪼갠 꼭짓점들. 이 오브젝트 자체가 플레이어를 따라다니므로
        /// 로컬 좌표 기준 (0,0)이 곧 공격 원점이다(PlayerAutoAttack.Attack()의 origin과 동일).
        /// </summary>
        private void BuildArcMesh(Vector2 aimDir, float radius, float halfArcDegrees)
        {
            int ringVertexCount = arcSegments + 1; // 호 위의 점들(양 끝 포함)
            Vector3[] vertices = new Vector3[ringVertexCount + 1]; // + 중심점 1개
            int[] triangles = new int[arcSegments * 3];

            vertices[0] = Vector3.zero;

            float startAngle = -halfArcDegrees;
            float angleStep = (halfArcDegrees * 2f) / arcSegments;

            for (int i = 0; i <= arcSegments; i++)
            {
                float angle = startAngle + angleStep * i;
                Vector2 dir = Quaternion.Euler(0f, 0f, angle) * aimDir;
                vertices[i + 1] = dir * radius;
            }

            for (int i = 0; i < arcSegments; i++)
            {
                int triIndex = i * 3;
                triangles[triIndex] = 0;
                triangles[triIndex + 1] = i + 1;
                triangles[triIndex + 2] = i + 2;
            }

            arcMesh.Clear();
            arcMesh.vertices = vertices;
            arcMesh.triangles = triangles;
            arcMesh.RecalculateBounds();
        }
    }
}
