using CheonMyeong.Settings;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CheonMyeong.UI
{
    /// <summary>
    /// 261116 재작성 - 아트 커서(Cursor-Sheet.png, 붓)를 화면 맨 위 UI 그림으로 그린다. UICanvas에 붙어 있다.
    /// - 예전 방식(Cursor.SetCursor)은 윈도우가 하드웨어 커서를 시스템 크기로 맞춰 버리고, 소프트웨어 커서도 크기 변경이 반영되지 않았다.
    ///   그래서 OS 커서는 숨기고(Cursor.visible = false) 전용 오버레이 캔버스의 RawImage가 마우스를 따라가게 했다.
    /// - 크기(설정 &gt; 조작 &gt; 커서 크기): 작게 1배 · 보통 2배 · 크게 3배(원본 48px, 1920×1080 기준 화면 크기에 맞춰 늘고 줄어듦).
    /// - 그림 끝(hotspot, 왼쪽 위 기준 픽셀)이 실제 클릭 지점이 되도록 피벗을 맞춘다.
    /// - 캔버스를 따로 둬서 커서가 움직여도 다른 UI가 다시 그려지지 않는다. 매 프레임 할당 없음.
    /// - 창 밖으로 나가거나 포커스를 잃으면 OS 커서를 돌려준다.
    /// </summary>
    public class CursorStyle : MonoBehaviour
    {
        [SerializeField] private Texture2D cursor;
        [Tooltip("커서 끝(클릭 지점) 픽셀 좌표, 왼쪽 위 기준.")]
        [SerializeField] private Vector2 hotspot = new Vector2(8f, 7f);
        [Tooltip("작게 · 보통 · 크게 배율(원본 픽셀 기준).")]
        [SerializeField] private float[] scales = { 1f, 2f, 3f };

        private const int SortingOrder = 32767;

        private Canvas canvas;
        private RectTransform cursorRect;
        private RawImage cursorImage;
        private int appliedSize = -1;
        private bool focused = true;

        private void OnEnable()
        {
            EnsureView();
            GameSettings.Changed += Refresh;
            appliedSize = -1;
            Refresh();
        }

        private void OnDisable()
        {
            GameSettings.Changed -= Refresh;
            if (canvas != null)
            {
                canvas.gameObject.SetActive(false);
            }
            Cursor.visible = true;
        }

        private void OnDestroy()
        {
            if (canvas != null)
            {
                Destroy(canvas.gameObject);
                canvas = null;
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            focused = hasFocus;
        }

        private void EnsureView()
        {
            if (canvas != null)
            {
                canvas.gameObject.SetActive(true);
                return;
            }
            var go = new GameObject("[Cursor]", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform.root.parent, false);
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            canvas.pixelPerfect = false;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            var imageGo = new GameObject("Brush", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            imageGo.transform.SetParent(go.transform, false);
            cursorRect = (RectTransform)imageGo.transform;
            cursorRect.anchorMin = Vector2.zero;
            cursorRect.anchorMax = Vector2.zero;
            cursorImage = imageGo.GetComponent<RawImage>();
            cursorImage.raycastTarget = false;
            cursorImage.texture = cursor;
            if (cursor != null)
            {
                cursorRect.pivot = new Vector2(hotspot.x / cursor.width, 1f - hotspot.y / cursor.height);
            }
        }

        private void Refresh()
        {
            if (cursor == null || cursorRect == null)
            {
                return;
            }
            int size = Mathf.Clamp(GameSettings.Active.CursorSize, 0, scales.Length - 1);
            if (size == appliedSize)
            {
                return;
            }
            appliedSize = size;
            float scale = scales[size];
            cursorRect.sizeDelta = new Vector2(cursor.width * scale, cursor.height * scale);
        }

        private void LateUpdate()
        {
            if (cursorRect == null)
            {
                return;
            }
            Mouse mouse = Mouse.current;
            Vector2 screen = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
            bool inside = mouse != null && focused
                          && screen.x >= 0f && screen.y >= 0f && screen.x <= Screen.width && screen.y <= Screen.height;
            if (cursorImage.enabled != inside)
            {
                cursorImage.enabled = inside;
            }
            if (Cursor.visible == inside)
            {
                Cursor.visible = !inside;
            }
            if (!inside)
            {
                return;
            }
            float factor = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
            cursorRect.anchoredPosition = screen / factor;
        }
    }
}
