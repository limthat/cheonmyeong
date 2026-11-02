using UnityEngine;

namespace CheonMyeong.Combat
{
    /// <summary>
    /// 261102 추가 - 이펙트를 실제 판정 범위에 맞춰 그리기 위한 "보이는 부분" 경계(스프라이트 피벗 기준, 유닛).
    /// 스프라이트 메시(Tight)의 꼭짓점은 투명 부분을 뺀 그림 윤곽이라, 텍스처를 읽지 않고도(Read/Write 불필요) 실제 그림 크기를 안다.
    /// 여러 프레임이면 전부 합친 범위(가장 크게 퍼진 순간 기준).
    /// </summary>
    public static class SpriteVisibleBounds
    {
        public static Rect Of(Sprite[] frames)
        {
            bool any = false;
            Vector2 min = Vector2.zero;
            Vector2 max = Vector2.zero;
            if (frames == null)
            {
                return Rect.zero;
            }
            for (int f = 0; f < frames.Length; f++)
            {
                Sprite sprite = frames[f];
                if (sprite == null)
                {
                    continue;
                }
                Vector2[] vertices = sprite.vertices;
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector2 v = vertices[i];
                    if (!any)
                    {
                        min = v;
                        max = v;
                        any = true;
                    }
                    else
                    {
                        min = Vector2.Min(min, v);
                        max = Vector2.Max(max, v);
                    }
                }
            }
            return any ? Rect.MinMaxRect(min.x, min.y, max.x, max.y) : Rect.zero;
        }

        /// <summary>
        /// "앞 = 왼쪽(-X)"으로 그려진 참격을 부채꼴 판정(원점 · 방향 · 반경 · 각도)에 맞춘다.
        /// 세로(옆) 축: 그림의 세로 폭 = 부채꼴 현 길이(2R·sin(각/2), 180도 이상이면 2R).
        /// 가로(앞뒤) 축: 그림의 가로 폭 = 반경 R - 그림 앞 끝이 사거리 끝, 뒤 끝이 공격하는 사람 위치(부채꼴 꼭짓점).
        /// 두 축을 따로 늘리므로 scale은 (가로, 세로).
        /// flipY는 방향이 오른쪽일 때 위아래가 뒤집혀 보이지 않게 하는 보정(그림 기준 세로 중심도 같이 뒤집힌다).
        /// </summary>
        public static void FitSlash(Rect visible, Vector2 origin, Vector2 direction, float radius, float arcDegrees, bool flipY,
                                    out Vector2 center, out float angleDegrees, out Vector2 scale)
        {
            angleDegrees = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 180f;
            float chord = arcDegrees >= 180f ? radius * 2f : radius * 2f * Mathf.Sin(arcDegrees * 0.5f * Mathf.Deg2Rad);
            float scaleY = visible.height > 0.0001f ? chord / visible.height : 1f;
            float scaleX = visible.width > 0.0001f ? radius / visible.width : scaleY;
            scale = new Vector2(scaleX, scaleY);
            float forwardExtent = -visible.xMin * scaleX;
            float lateralCenter = (flipY ? -visible.center.y : visible.center.y) * scaleY;
            Vector2 lateralAxis = Quaternion.Euler(0f, 0f, angleDegrees) * Vector3.up;
            center = origin + direction * (radius - forwardExtent) - lateralAxis * lateralCenter;
        }

        /// <summary>바닥 폭발 같은 원형 범위 그림: 그림 가로 폭 = 지름, 가로 가운데 = 중심, 그림 바닥은 중심보다 조금 아래(탑뷰 원근).</summary>
        public static void FitGroundBurst(Rect visible, Vector2 center, float radius, out Vector2 position, out float scale)
        {
            scale = visible.width > 0.0001f ? radius * 2f / visible.width : 1f;
            position = new Vector2(center.x - visible.center.x * scale, center.y - radius * 0.35f - visible.yMin * scale);
        }
    }
}
