using UnityEngine;
using UnityEngine.UI;

namespace CheonMyeong.UI
{
    /// <summary>
    /// 261114 추가 - 9분할(Sliced) 틀 그림의 "안쪽 빈 칸"에 자식(채움 바 등)을 정확히 맞춘다.
    /// holePixels = 스프라이트 원본 픽셀 기준 안쪽 칸까지의 여백(왼쪽, 아래, 오른쪽, 위).
    /// 여백이 9분할 테두리 안에 있으면 1:1, 가운데(늘어나는) 구역에 걸리면 늘어난 비율대로 바꿔서 맞춘다.
    /// 보스 바처럼 안쪽 칸이 테두리보다 안쪽에 있는 그림(Bar-Sheet_10: 왼쪽 얼굴)에 쓴다. 비우면(0) 테두리를 그대로 쓴다.
    /// </summary>
    public class SlicedHole : MonoBehaviour
    {
        [Tooltip("스프라이트 원본 픽셀 기준 안쪽 칸 여백 (x=왼쪽, y=아래, z=오른쪽, w=위). 0이면 9분할 테두리를 쓴다.")]
        public Vector4 holePixels;

        [Tooltip("시작할 때 이 칸에 맞출 자식(비워도 됨 - 코드가 Fit을 직접 부를 수 있다).")]
        public RectTransform fitTarget;

        private void Start()
        {
            if (fitTarget != null && TryGetComponent(out Image frame))
            {
                Fit(frame, fitTarget, holePixels);
            }
        }

        /// <summary>target(frame의 자식)을 frame 그림의 안쪽 칸에 맞춘다.</summary>
        public static void Fit(Image frame, RectTransform target, Vector4 holePixels)
        {
            target.anchorMin = Vector2.zero;
            target.anchorMax = Vector2.one;
            Sprite sprite = frame != null ? frame.sprite : null;
            if (sprite == null)
            {
                target.offsetMin = Vector2.zero;
                target.offsetMax = Vector2.zero;
                return;
            }
            Vector4 border = sprite.border;
            Vector4 hole = holePixels.sqrMagnitude > 0f ? holePixels : border;
            float unitsPerPixel = 1f / Mathf.Max(0.0001f, frame.pixelsPerUnit * frame.pixelsPerUnitMultiplier);
            Rect local = frame.rectTransform.rect;
            float left = Map(hole.x, sprite.rect.width, border.x, border.z, local.width, unitsPerPixel);
            float right = local.width - Map(sprite.rect.width - hole.z, sprite.rect.width, border.x, border.z, local.width, unitsPerPixel);
            float bottom = Map(hole.y, sprite.rect.height, border.y, border.w, local.height, unitsPerPixel);
            float top = local.height - Map(sprite.rect.height - hole.w, sprite.rect.height, border.y, border.w, local.height, unitsPerPixel);
            target.offsetMin = new Vector2(left, bottom);
            target.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>원본 픽셀 좌표 → 9분할로 늘어난 로컬 좌표.</summary>
        private static float Map(float pixel, float spriteSize, float borderMin, float borderMax, float localSize, float unitsPerPixel)
        {
            if (pixel <= borderMin)
            {
                return pixel * unitsPerPixel;
            }
            if (pixel >= spriteSize - borderMax)
            {
                return localSize - (spriteSize - pixel) * unitsPerPixel;
            }
            float centerPixels = Mathf.Max(0.0001f, spriteSize - borderMin - borderMax);
            float centerLocal = localSize - (borderMin + borderMax) * unitsPerPixel;
            return borderMin * unitsPerPixel + (pixel - borderMin) / centerPixels * centerLocal;
        }
    }
}
