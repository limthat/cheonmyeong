using CheonMyeong.Settings;
using UnityEngine;

namespace CheonMyeong.UI
{
    /// <summary>
    /// 261115 - 마우스 커서를 아트 커서(Cursor-Sheet.png, 붓)로 바꾼다. UICanvas에 붙어 있다.
    /// - Cursor.SetCursor(하드웨어 커서) - 화면 프레임과 따로 그려져 느린 프레임에서도 커서가 밀리지 않는다.
    /// - 텍스처 임포트: Texture Type = Cursor, Read/Write 켬, 압축 없음(SetCursor 요구 조건).
    /// - 261115 - 원본(48px)이 작아서 '보통' = 2배, '크게' = 3배(점 보간). 확대 텍스처는 크기를 바꿀 때만 새로 만들고 이전 것은 지운다.
    /// - hotspot = 붓 끝 픽셀(왼쪽 위 기준).
    /// </summary>
    public class CursorStyle : MonoBehaviour
    {
        [SerializeField] private Texture2D cursor;
        [Tooltip("커서 끝(클릭 지점) 픽셀 좌표, 왼쪽 위 기준.")]
        [SerializeField] private Vector2 hotspot = new Vector2(8f, 7f);
        [Tooltip("하드웨어 커서 크기가 OS에서 제한되어 '크게'가 안 커지면 켠다(소프트웨어 커서, 프레임에 묶임).")]
        [SerializeField] private bool forceSoftware;

        private static readonly int[] Scales = { 2, 3 };

        private Texture2D scaled;
        private int appliedSize = -1;

        private void OnEnable()
        {
            GameSettings.Changed += Refresh;
            appliedSize = -1;
            Refresh();
        }

        private void OnDisable()
        {
            GameSettings.Changed -= Refresh;
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            appliedSize = -1;
        }

        private void OnDestroy()
        {
            ReleaseScaled();
        }

        private void Refresh()
        {
            if (cursor == null)
            {
                return;
            }
            int size = GameSettings.Active.CursorSize;
            if (size == appliedSize)
            {
                return;
            }
            appliedSize = size;
            CursorMode mode = forceSoftware ? CursorMode.ForceSoftware : CursorMode.Auto;
            int factor = Scales[Mathf.Clamp(size, 0, Scales.Length - 1)];
            Texture2D next = factor > 1 ? Upscale(cursor, factor) : null;
            if (next != null)
            {
                Cursor.SetCursor(next, hotspot * factor, mode);
            }
            else
            {
                Cursor.SetCursor(cursor, hotspot, mode);
            }
            // 새 커서를 건 다음에 이전 확대 텍스처를 지운다.
            ReleaseScaled();
            scaled = next;
        }

        private void ReleaseScaled()
        {
            if (scaled != null)
            {
                Destroy(scaled);
                scaled = null;
            }
        }

        /// <summary>점 보간(픽셀 아트 유지) 확대. 원본이 Read/Write 꺼져 있으면 null.</summary>
        private static Texture2D Upscale(Texture2D source, int factor)
        {
            if (!source.isReadable)
            {
                Debug.LogWarning("[CheonMyeong] 커서 텍스처 Read/Write가 꺼져 있어 '크게'를 만들 수 없습니다.");
                return null;
            }
            int w = source.width;
            int h = source.height;
            Color32[] src = source.GetPixels32();
            var dst = new Color32[w * factor * h * factor];
            int dw = w * factor;
            for (int y = 0; y < h * factor; y++)
            {
                int sy = y / factor;
                for (int x = 0; x < dw; x++)
                {
                    dst[y * dw + x] = src[sy * w + x / factor];
                }
            }
            var result = new Texture2D(dw, h * factor, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = source.name + "_x" + factor,
            };
            result.SetPixels32(dst);
            result.Apply(false, false);
            return result;
        }
    }
}
