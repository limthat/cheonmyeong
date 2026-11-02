using System;
using System.Collections;
using UnityEngine;

namespace CheonMyeong.UI.Title
{
    /// <summary>
    /// 261101 추가 - 타이틀 대문(Title-Sheet, 1920x1080 × 9프레임). 평소엔 닫힌 문(0프레임)을 화면에 꽉 차게(cover) 보여주고,
    /// 시작 부적을 누르면 1~8프레임을 재생해 문이 열린 뒤 콜백을 부른다(기획서 11장 "대문이 열리며 시작한다").
    /// 시작/설정/종료 부적은 이 오브젝트의 자식이라 화면 크기에 맞춰 같이 커지고 줄어든다.
    /// 카메라 크기/화면비가 바뀔 때만 다시 맞춘다(매 프레임 계산 안 함).
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class TitleGate : MonoBehaviour
    {
        /// <summary>
        /// 261102 - 문 그림의 열림 프레임에 그려진 부적은 해상도가 낮아(큰 픽셀로 깨져 보임) 그 위에 선명한 부적을 계속 덮어 둔다.
        /// 프레임마다 그림 속 부적의 위치(x, y) · 기울기(z, 도)와 가로 · 세로 배율을 재서 넣는다(빌더가 채움).
        /// </summary>
        [Serializable]
        public class TalismanTrack
        {
            public Transform talisman;
            public Vector3[] poses;
            public Vector2[] scales;
        }

        [SerializeField] private Sprite[] frames;
        [SerializeField] private TalismanTrack[] talismanTracks;
        [Tooltip("문 열림 재생 속도 - 프로젝트 공통 100ms(10fps).")]
        [SerializeField, Min(1f)] private float framesPerSecond = 10f;
        [Tooltip("다 열린 뒤 로비로 넘어가기 전 잠깐 멈추는 시간(초).")]
        [SerializeField, Min(0f)] private float holdAfterOpenSeconds = 0.2f;
        [SerializeField] private Camera targetCamera;

        private SpriteRenderer spriteRenderer;
        private Coroutine opening;
        private float fittedOrthoSize = -1f;
        private float fittedAspect = -1f;

        public bool IsOpening => opening != null;

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }
        }

        private void OnEnable()
        {
            ShowFrame(0);
            FitToCamera(true);
        }

        private void OnDisable()
        {
            if (opening != null)
            {
                StopCoroutine(opening);
                opening = null;
            }
        }

        private void LateUpdate()
        {
            FitToCamera(false);
        }

        /// <summary>문을 연다. 이미 여는 중이면 무시.</summary>
        public void PlayOpen(Action onOpened)
        {
            if (opening != null)
            {
                return;
            }
            opening = StartCoroutine(OpenRoutine(onOpened));
        }

        private IEnumerator OpenRoutine(Action onOpened)
        {
            var frameWait = new WaitForSeconds(1f / framesPerSecond);
            int count = frames != null ? frames.Length : 0;
            for (int i = 1; i < count; i++)
            {
                yield return frameWait;
                ShowFrame(i);
            }
            if (holdAfterOpenSeconds > 0f)
            {
                yield return new WaitForSeconds(holdAfterOpenSeconds);
            }
            opening = null;
            onOpened?.Invoke();
        }

        private void ShowFrame(int index)
        {
            if (spriteRenderer != null && frames != null && index >= 0 && index < frames.Length && frames[index] != null)
            {
                spriteRenderer.sprite = frames[index];
            }
            if (talismanTracks == null)
            {
                return;
            }
            for (int i = 0; i < talismanTracks.Length; i++)
            {
                TalismanTrack track = talismanTracks[i];
                if (track == null || track.talisman == null || track.poses == null || index >= track.poses.Length)
                {
                    continue;
                }
                Vector3 pose = track.poses[index];
                track.talisman.localPosition = new Vector3(pose.x, pose.y, 0f);
                track.talisman.localRotation = Quaternion.Euler(0f, 0f, pose.z);
                Vector2 scale = track.scales != null && index < track.scales.Length ? track.scales[index] : Vector2.one;
                track.talisman.localScale = new Vector3(scale.x, scale.y, 1f);
            }
        }

        /// <summary>화면을 빈틈없이 덮도록(cover) 크기를 맞추고 카메라 가운데에 둔다.</summary>
        private void FitToCamera(bool force)
        {
            if (targetCamera == null || !targetCamera.orthographic || spriteRenderer == null || spriteRenderer.sprite == null)
            {
                return;
            }
            float ortho = targetCamera.orthographicSize;
            float aspect = targetCamera.aspect;
            if (!force && Mathf.Approximately(ortho, fittedOrthoSize) && Mathf.Approximately(aspect, fittedAspect))
            {
                return;
            }
            fittedOrthoSize = ortho;
            fittedAspect = aspect;

            Vector2 size = spriteRenderer.sprite.bounds.size;
            if (size.x <= 0f || size.y <= 0f)
            {
                return;
            }
            float viewHeight = ortho * 2f;
            float viewWidth = viewHeight * aspect;
            float scale = Mathf.Max(viewHeight / size.y, viewWidth / size.x);
            transform.localScale = new Vector3(scale, scale, 1f);
            Vector3 cameraPosition = targetCamera.transform.position;
            transform.position = new Vector3(cameraPosition.x, cameraPosition.y, 0f);
        }
    }
}
