using CheonMyeong.Data;
using CheonMyeong.Input;
using UnityEngine;
using UnityEngine.UI;

namespace CheonMyeong.UI
{
    /// <summary>
    /// 261027 추가 - 로비 마당(와이어프레임 LOBBY: "캐릭터가 걸어다니는 공간")에서 선택된 캐릭터를 WASD/방향키로
    /// 움직인다. 로비는 UI(Canvas) 화면이라 네트워크/물리 없이 RectTransform만 옮긴다.
    ///
    /// - 크기/속도: canvasUnitsPerWorldUnit(월드 1유닛 = 캔버스 몇 단위)로 스프라이트 크기와 CharacterStatsSO.moveSpeed를
    ///   같은 비율로 바꾼다. 0이면 인게임 카메라(orthographic) 기준으로 자동 계산(캔버스 높이 / (orthographicSize × 2)).
    ///   261101 - 건물 로비에서는 건물 그림과 크기를 맞추려고 빌더가 값을 직접 넣는다.
    /// - 대각선 보정, 키 배치: PlayerMovement/PlayerInputReader와 같다.
    /// - 방향: 원본 아트가 왼쪽을 보고 있어서 오른쪽으로 움직일 때 좌우 반전(PlayerAnimator와 같은 규칙).
    ///
    /// 261101 - 건물 로비(기획서 v0.9.3 "로비: 캐릭터가 걸어 다니고 건물이 곧 메뉴"):
    /// - 위치 기준점(pivot)은 발밑(아래 가운데). walkArea(마당 바닥) 안에서만 움직인다.
    /// - obstacles(건물)의 바닥 영역(LobbyBuilding.BlocksPoint)으로는 들어가지 못한다 - 막히면 X/Y 축을 따로 시도해서
    ///   건물 옆을 미끄러지듯 지나간다.
    ///
    /// 구조: 이 오브젝트(위치/크기) 아래에 캐릭터 그림(spriteImage)을 자식으로 둔다 - 좌우 반전은 그림 자식에만 적용한다.
    /// </summary>
    public class LobbyAvatar : MonoBehaviour
    {
        [SerializeField] private CharacterCatalogSO characterCatalog;

        [Tooltip("캐릭터 그림. 비워두면 이 오브젝트의 Image를 쓴다.")]
        [SerializeField] private Image spriteImage;

        [Tooltip("캐릭터가 걸을 수 있는 바닥 범위. 이 아바타와 같은 부모 아래에 둔다.")]
        [SerializeField] private RectTransform walkArea;

        [Tooltip("들어갈 수 없는 건물들(바닥 영역으로 막힘).")]
        [SerializeField] private LobbyBuilding[] obstacles;

        [Tooltip("로비가 열릴 때 서 있을 위치(부모 기준 로컬 좌표, 발밑).")]
        [SerializeField] private Vector2 startPosition;

        [Tooltip("월드 1유닛당 캔버스 단위. 0이면 인게임 카메라 기준으로 자동 계산.")]
        [SerializeField] private float canvasUnitsPerWorldUnit;

        [Tooltip("자동 계산이 안 될 때 쓰는 값(1080 / (5 × 2) = 108).")]
        [SerializeField] private float fallbackCanvasUnitsPerWorldUnit = 108f;

        [Tooltip("캐릭터 스탯이 연결 안 됐을 때의 이동 속도(유닛/초) - 기획서 하영 기준 300(=5).")]
        [SerializeField] private float fallbackMoveSpeed = 5f;

        [Header("261109 - 박석 마당 경계 (배경 그림 기준, 화면 비율 0~1 · 위에서부터)")]
        [Tooltip("켜면 발밑이 양옆 회랑(기둥 줄) 쪽으로 넘어가지 않는다 - 배경(background-Sheet)의 기둥보다 캐릭터가 앞에 그려지는 문제 방지.")]
        [SerializeField] private bool clampToPlaza = true;
        [SerializeField] private Vector2 plazaLeftTop = new Vector2(0.181f, 0.521f);
        [SerializeField] private Vector2 plazaLeftBottom = new Vector2(0f, 0.833f);
        [SerializeField] private Vector2 plazaRightTop = new Vector2(0.841f, 0.528f);
        [SerializeField] private Vector2 plazaRightBottom = new Vector2(0.977f, 0.833f);

        private RectTransform rectTransform;
        private Image image;
        private RectTransform imageTransform;
        private CharacterCatalogSO.CharacterEntry entry;
        private float unitsScale;
        private float moveSpeed;
        private bool wasMoving;
        private bool facingRight;
        private float animTime;
        private int lastFrameIndex = -1;

        /// <summary>발밑 위치(부모 기준 로컬 좌표). LobbyInteraction이 건물과의 거리/앞뒤 정렬에 쓴다.</summary>
        public Vector2 FootPosition => rectTransform.localPosition;

        private void Awake()
        {
            rectTransform = (RectTransform)transform;
            image = spriteImage != null ? spriteImage : GetComponent<Image>();
            if (image == null)
            {
                Debug.LogError("[CheonMyeong] LobbyAvatar에 캐릭터 Image가 없습니다 - Build Single Screen/Lobby를 다시 실행하세요.");
                enabled = false;
                return;
            }
            imageTransform = image.rectTransform;
            image.raycastTarget = false;
            image.preserveAspect = true;
        }

        private void OnEnable()
        {
            CurrentRun.EnsureCharacterSelected(characterCatalog);
            entry = CurrentRun.SelectedCharacter;
            unitsScale = canvasUnitsPerWorldUnit > 0f ? canvasUnitsPerWorldUnit : ResolveCanvasUnitsPerWorldUnit();
            moveSpeed = entry != null && entry.stats != null ? entry.stats.moveSpeed : fallbackMoveSpeed;

            rectTransform.anchorMin = rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0f);
            ApplySize();
            rectTransform.localPosition = ClampToWalkArea(startPosition);

            wasMoving = false;
            animTime = 0f;
            lastFrameIndex = -1;
            ApplyFacing();
            UpdateFrame(false);
        }

        private void Update()
        {
            bool blocked = UIFlowManager.Instance != null && UIFlowManager.Instance.HasOverlay;
            Vector2 move = blocked ? Vector2.zero : PlayerInputReader.ReadKeyboardMove();
            if (move.sqrMagnitude > 1f)
            {
                move.Normalize();
            }

            bool isMoving = move.sqrMagnitude > 0.01f;
            if (isMoving != wasMoving)
            {
                wasMoving = isMoving;
                animTime = 0f;
                lastFrameIndex = -1;
            }

            if (isMoving)
            {
                Vector2 current = rectTransform.localPosition;
                Vector2 delta = move * (moveSpeed * unitsScale * Time.deltaTime);
                rectTransform.localPosition = ResolveMove(current, delta);

                if (Mathf.Abs(move.x) > 0.01f && (move.x > 0f) != facingRight)
                {
                    facingRight = move.x > 0f;
                    ApplyFacing();
                }
            }

            animTime += Time.deltaTime;
            UpdateFrame(isMoving);
        }

        private Vector2 ResolveMove(Vector2 current, Vector2 delta)
        {
            Vector2 full = ClampToWalkArea(current + delta);
            if (!IsBlocked(full))
            {
                return full;
            }
            Vector2 xOnly = ClampToWalkArea(current + new Vector2(delta.x, 0f));
            if (!IsBlocked(xOnly))
            {
                return xOnly;
            }
            Vector2 yOnly = ClampToWalkArea(current + new Vector2(0f, delta.y));
            return IsBlocked(yOnly) ? current : yOnly;
        }

        private bool IsBlocked(Vector2 foot)
        {
            if (obstacles == null)
            {
                return false;
            }
            for (int i = 0; i < obstacles.Length; i++)
            {
                if (obstacles[i] != null && obstacles[i].BlocksPoint(foot))
                {
                    return true;
                }
            }
            return false;
        }

        private Vector2 ClampToWalkArea(Vector2 position)
        {
            if (walkArea == null)
            {
                return position;
            }

            Rect local = walkArea.rect;
            Vector2 offset = walkArea.localPosition;
            float halfWidth = rectTransform.sizeDelta.x * 0.25f;
            var clamped = new Vector2(
                Mathf.Clamp(position.x, offset.x + local.xMin + halfWidth, offset.x + local.xMax - halfWidth),
                Mathf.Clamp(position.y, offset.y + local.yMin, offset.y + local.yMax));
            return clampToPlaza ? ClampToPlaza(clamped, halfWidth) : clamped;
        }

        /// <summary>261109 - 배경의 박석 마당(사다리꼴) 안으로 발밑을 넣는다. 양옆 사선 경계 = 회랑 끝.</summary>
        private Vector2 ClampToPlaza(Vector2 foot, float halfWidth)
        {
            if (!(rectTransform.parent is RectTransform parent))
            {
                return foot;
            }
            Rect area = parent.rect;
            if (area.width <= 0f || area.height <= 0f)
            {
                return foot;
            }
            float v = (area.yMax - foot.y) / area.height;
            float left = area.xMin + area.width * EdgeU(plazaLeftTop, plazaLeftBottom, v) + halfWidth;
            float right = area.xMin + area.width * EdgeU(plazaRightTop, plazaRightBottom, v) - halfWidth;
            if (left > right)
            {
                foot.x = (left + right) * 0.5f;
                return foot;
            }
            foot.x = Mathf.Clamp(foot.x, left, right);
            return foot;
        }

        private static float EdgeU(Vector2 top, Vector2 bottom, float v)
        {
            float span = bottom.y - top.y;
            float t = Mathf.Abs(span) > 0.0001f ? (v - top.y) / span : 0f;
            return Mathf.LerpUnclamped(top.x, bottom.x, t);
        }

        private float ResolveCanvasUnitsPerWorldUnit()
        {
            Camera cam = Camera.main;
            Canvas canvas = GetComponentInParent<Canvas>();
            if (cam == null || !cam.orthographic || cam.orthographicSize <= 0f || canvas == null)
            {
                return fallbackCanvasUnitsPerWorldUnit;
            }

            var canvasRect = (RectTransform)canvas.rootCanvas.transform;
            float height = canvasRect.rect.height;
            return height > 0f ? height / (cam.orthographicSize * 2f) : fallbackCanvasUnitsPerWorldUnit;
        }

        private void ApplySize()
        {
            Sprite first = FirstSprite();
            image.enabled = first != null;
            if (first == null)
            {
                return;
            }

            Vector2 worldSize = first.rect.size / first.pixelsPerUnit;
            rectTransform.sizeDelta = worldSize * unitsScale;
        }

        private void ApplyFacing()
        {
            Vector3 scale = imageTransform.localScale;
            scale.x = facingRight ? -Mathf.Abs(scale.x) : Mathf.Abs(scale.x);
            imageTransform.localScale = scale;
        }

        private void UpdateFrame(bool isMoving)
        {
            if (entry == null)
            {
                return;
            }

            Sprite[] frames = isMoving && HasFrames(entry.lobbyWalkFrames) ? entry.lobbyWalkFrames : entry.lobbyIdleFrames;
            float fps = isMoving && HasFrames(entry.lobbyWalkFrames) ? entry.lobbyWalkFps : entry.lobbyIdleFps;
            if (!HasFrames(frames))
            {
                return;
            }

            int index = (int)(animTime * fps) % frames.Length;
            if (index != lastFrameIndex)
            {
                lastFrameIndex = index;
                image.sprite = frames[index];
            }
        }

        private Sprite FirstSprite()
        {
            if (entry == null)
            {
                return null;
            }
            if (HasFrames(entry.lobbyIdleFrames))
            {
                return entry.lobbyIdleFrames[0];
            }
            return HasFrames(entry.lobbyWalkFrames) ? entry.lobbyWalkFrames[0] : null;
        }

        private static bool HasFrames(Sprite[] frames)
        {
            return frames != null && frames.Length > 0 && frames[0] != null;
        }
    }
}
