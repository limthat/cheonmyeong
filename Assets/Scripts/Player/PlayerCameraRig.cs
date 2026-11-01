using UnityEngine;

namespace CheonMyeong.Player
{
    /// <summary>
    /// 로컬 전용(네트워크 아님) 카메라 추적기 - Main Camera에 붙인다.
    /// 평상시엔 "내 캐릭터"를 따라가다가, 내 캐릭터가 죽으면 PlayerHealth가
    /// SetFollowTarget()을 호출해 살아있는 동료 쪽으로 전환한다 - 기획 확정(261013):
    /// "한쪽 사망 시, 남은 시간 동안 다른 쪽 플레이어에게 카메라 고정되고 관전".
    ///
    /// NetworkVariable이 전혀 없는 이유: 카메라는 각 클라이언트 화면에서만 의미
    /// 있는 순수 로컬 연출이라 서버가 알 필요도, 복제할 필요도 없다(PlayerHealth
    /// 쪽에서 IsOwner인 클라이언트만 이 컴포넌트를 호출하도록 가드한다).
    ///
    /// 싱글턴으로 둔 이유: 씬에 카메라는 하나뿐인데, 플레이어는 네트워크로 런타임에
    /// 스폰되므로 Inspector로 사전 연결이 불가능하다 - PlayerHealth가 매번
    /// FindObjectOfType으로 찾는 비용/번거로움 없이 Instance로 바로 접근하게 한다.
    ///
    /// 261013 수정 - "카메라 따라오는게 너무 느림" 피드백에 대응해 예측(lookahead) 추적
    /// 추가: 타겟의 Rigidbody2D.linearVelocity를 읽어서 "지금 위치"가 아니라 "조금 뒤에
    /// 있을 위치"를 보간 목표로 잡는다 - 이러면 followSpeed를 과하게 올려서 생기는 떨림/
    /// 오버슈트 없이도 체감 지연이 줄어든다. 타겟이 호스트 자신의 Rigidbody2D라면
    /// PlayerMovement가 네트워크 지연 없이 즉시 움직이므로(FixedUpdate, IsServer 게이트),
    /// 이 lookahead는 순수하게 "다음 프레임에 어디 있을지"를 더 일찍 반영하는 역할만
    /// 한다 - 클라이언트측 입력 예측/보간(PlayerMovement 쪽 미정 항목)과는 별개.
    /// Rigidbody2D가 없는 타겟(예: 전용 관전 더미)이면 lookahead 없이 기존 방식 그대로
    /// 작동한다 - null 체크로 안전하게 폴백.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerCameraRig : MonoBehaviour
    {
        public static PlayerCameraRig Instance { get; private set; }

        [Tooltip("초당 추적 속도(지수 감쇠 계수) - 값이 클수록 타겟 위치로 빨리 붙는다. " +
                 "0 이하로 두면 보간 없이 즉시 스냅.")]
        [SerializeField] private float followSpeed = 8f;

        [Tooltip("261013 추가 - 타겟의 현재 속도(Rigidbody2D.linearVelocity)에 이 시간(초)을 " +
                 "곱한 만큼 앞서서 추적한다. 0이면 예측 없이 순수 현재 위치만 따라간다. " +
                 "값은 미정, 느낌 보고 조정(체감상 너무 크면 코너링에서 캐릭터가 화면 중앙에서 " +
                 "벗어나 보임).")]
        [SerializeField] private float lookAheadTime = 0.12f;

        [Tooltip("카메라 Z 위치 - 탑뷰 2D라 X/Y만 타겟을 따라가고 Z(원근/정렬용)는 고정 유지한다.")]
        [SerializeField] private float fixedZ = -10f;

        [Tooltip("261101 추가 - 인게임 카메라 크기(orthographicSize). 맵 타일이 1유닛(64px, PPU 64)으로 커진 만큼 예전 5를\n" +
                 "5 ÷ 0.64 = 7.8125로 키워 예전과 같은 칸 수(세로 약 15.6칸)가 보이게 했다. 따라갈 대상이 정해질 때(인게임 시작)\n" +
                 "적용한다 - 타이틀 화면은 같은 카메라로 원래 크기 그대로 보인다. 0 이하면 바꾸지 않는다.")]
        [SerializeField] private float gameplayOrthographicSize = 7.8125f;

        private Camera rigCamera;
        private Transform followTarget;
        private Rigidbody2D followTargetBody;

        private void Awake()
        {
            // 씬 재로드 등으로 중복 생성될 경우 기존 인스턴스를 유지하고 새로 들어온
            // 쪽은 비활성화 - 여러 개가 동시에 카메라를 움직이면 떨림/충돌이 생긴다.
            if (Instance != null && Instance != this)
            {
                enabled = false;
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// 카메라가 따라갈 대상을 바꾼다. target이 null이면 아무 것도 하지 않고
        /// 마지막 타겟/위치에 그대로 머문다(예: 전원 사망처럼 아직 기획에 없는
        /// 예외 상황을 억지로 처리하지 않고 안전하게 무시하기 위함).
        /// </summary>
        public void SetFollowTarget(Transform target)
        {
            if (target == null)
            {
                return;
            }
            followTarget = target;
            ApplyGameplayZoom();
            // 타겟이 바뀔 때 한 번만 TryGetComponent - 매 프레임 컴포넌트 조회를 피한다.
            // 못 찾으면 null로 둬서 아래에서 lookahead 없이 안전하게 폴백.
            followTargetBody = target.TryGetComponent(out Rigidbody2D rb) ? rb : null;
        }

        private void LateUpdate()
        {
            if (followTarget == null)
            {
                return;
            }

            Vector3 rawTargetPos = new Vector3(followTarget.position.x, followTarget.position.y, fixedZ);
            Vector3 targetPos = rawTargetPos;
            if (followTargetBody != null && lookAheadTime > 0f)
            {
                Vector2 predictedOffset = followTargetBody.linearVelocity * lookAheadTime;
                targetPos.x += predictedOffset.x;
                targetPos.y += predictedOffset.y;
            }

            targetPos = ClampToMap(targetPos);

            if (followSpeed <= 0f)
            {
                transform.position = targetPos;
                return;
            }

            // 프레임레이트에 무관한 지수 감쇠 보간 - Lerp(a, b, t)에 고정 t를 매 프레임
            // 그대로 쓰면 프레임레이트가 달라질 때 체감 속도가 달라지는 흔한 실수를 피한다.
            float t = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);
            transform.position = ClampToMap(Vector3.Lerp(transform.position, targetPos, t));
        }

        /// <summary>
        /// 261101 추가 - "카메라가 맵 넘어서까지 보인다"(예섭). 화면 가장자리(orthographicSize × 화면비)가 MapBounds.CameraArea
        /// (타일맵 전체 범위) 밖으로 나가지 않게 카메라 중심을 제한한다. 맵이 화면보다 작은 축은 맵 가운데에 고정한다.
        /// 로비 등 MapBounds가 없는 씬에서는 아무것도 안 한다.
        /// </summary>
        private Vector3 ClampToMap(Vector3 position)
        {
            Maps.MapBounds bounds = Maps.MapBounds.Instance;
            if (bounds == null)
            {
                return position;
            }

            Rect area = bounds.CameraArea;
            if (area.width <= 0f || area.height <= 0f)
            {
                return position;
            }

            if (rigCamera == null && !TryGetComponent(out rigCamera))
            {
                rigCamera = Camera.main;
            }
            if (rigCamera == null || !rigCamera.orthographic)
            {
                return position;
            }

            float halfHeight = rigCamera.orthographicSize;
            float halfWidth = halfHeight * rigCamera.aspect;
            position.x = area.width <= halfWidth * 2f ? area.center.x : Mathf.Clamp(position.x, area.xMin + halfWidth, area.xMax - halfWidth);
            position.y = area.height <= halfHeight * 2f ? area.center.y : Mathf.Clamp(position.y, area.yMin + halfHeight, area.yMax - halfHeight);
            return position;
        }

        private void ApplyGameplayZoom()
        {
            if (gameplayOrthographicSize <= 0f)
            {
                return;
            }
            if (rigCamera == null && !TryGetComponent(out rigCamera))
            {
                rigCamera = Camera.main;
            }
            if (rigCamera != null && rigCamera.orthographic)
            {
                rigCamera.orthographicSize = gameplayOrthographicSize;
            }
        }
    }
}
