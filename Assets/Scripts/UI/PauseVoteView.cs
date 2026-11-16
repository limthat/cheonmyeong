using CheonMyeong.Progression;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace CheonMyeong.UI
{
    /// <summary>
    /// 261110 추가 - 일시정지 투표 알림(인게임 화면 위쪽 가운데). 와이어프레임 UI는 아트가 잡는 중이라 실행 중에만 만든다.
    /// - 내가 요청: "일시정지 요청 중… 동료 응답 대기 (N초)"
    /// - 동료가 요청: "동료가 일시정지를 요청했어요 (N초)" + 수락 / 거절 (ESC = 수락)
    /// - 거절 · 시간 초과: 2초 동안 "일시정지가 거절됐어요"
    /// 상태는 PartyProgress(서버 복제값)를 매 프레임 보고, 글자는 바뀔 때만 다시 쓴다.
    /// </summary>
    public class PauseVoteView : MonoBehaviour
    {
        private const float ResultSeconds = 2f;
        private static readonly Color PanelColor = new Color(0.08f, 0.07f, 0.1f, 0.9f);
        private static readonly Color AcceptColor = new Color(0.25f, 0.45f, 0.2f, 1f);
        private static readonly Color RejectColor = new Color(0.5f, 0.15f, 0.15f, 1f);

        private GameObject panel;
        private Text message;
        private GameObject buttons;
        private float resultTimer;
        private string shownText;

        public static PauseVoteView Create(Transform hudRoot)
        {
            var go = new GameObject("PauseVote", typeof(RectTransform));
            go.transform.SetParent(hudRoot, false);
            var view = go.AddComponent<PauseVoteView>();
            view.Build();
            return view;
        }

        private void OnEnable()
        {
            PartyProgress.PauseVoteFinished += HandleVoteFinished;
        }

        private void OnDisable()
        {
            PartyProgress.PauseVoteFinished -= HandleVoteFinished;
        }

        private void HandleVoteFinished(bool passed)
        {
            resultTimer = passed ? 0f : ResultSeconds;
        }

        private void Build()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var root = (RectTransform)transform;
            root.anchorMin = new Vector2(0.5f, 1f);
            root.anchorMax = new Vector2(0.5f, 1f);
            root.pivot = new Vector2(0.5f, 1f);
            root.anchoredPosition = new Vector2(0f, -200f);
            root.sizeDelta = new Vector2(520f, 110f);

            panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(transform, false);
            Stretch((RectTransform)panel.transform, Vector2.zero, Vector2.one);
            panel.GetComponent<Image>().color = PanelColor;

            var textGo = new GameObject("Message", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(panel.transform, false);
            Stretch((RectTransform)textGo.transform, new Vector2(0f, 0.45f), Vector2.one);
            message = textGo.GetComponent<Text>();
            message.font = font;
            message.fontSize = 20;
            message.alignment = TextAnchor.MiddleCenter;
            message.color = Color.white;
            message.raycastTarget = false;

            buttons = new GameObject("Buttons", typeof(RectTransform));
            buttons.transform.SetParent(panel.transform, false);
            Stretch((RectTransform)buttons.transform, new Vector2(0.15f, 0.08f), new Vector2(0.85f, 0.42f));
            CreateButton(buttons.transform, "수락 (ESC)", AcceptColor, new Vector2(0f, 0f), new Vector2(0.48f, 1f), font, () => Answer(true));
            CreateButton(buttons.transform, "거절", RejectColor, new Vector2(0.52f, 0f), new Vector2(1f, 1f), font, () => Answer(false));

            panel.SetActive(false);
        }

        private static void Answer(bool accept)
        {
            PartyProgress party = PartyProgress.Instance;
            if (party != null && party.IsSpawned && party.IsPauseVoteActive)
            {
                party.AnswerPauseVoteRpc(accept);
            }
        }

        private void Update()
        {
            PartyProgress party = PartyProgress.Instance;
            NetworkManager manager = NetworkManager.Singleton;
            string text = null;
            bool showButtons = false;

            if (party != null && party.IsSpawned && party.IsPauseVoteActive && manager != null)
            {
                int seconds = Mathf.CeilToInt(party.PauseVoteSecondsLeft);
                if (party.PauseVoteRequester == manager.LocalClientId)
                {
                    text = "일시정지 요청 중… 동료 응답 대기 (" + seconds + "초)";
                }
                else
                {
                    text = "동료가 일시정지를 요청했어요 (" + seconds + "초)";
                    showButtons = true;
                }
            }
            else if (resultTimer > 0f)
            {
                resultTimer -= Time.unscaledDeltaTime;
                text = "일시정지가 거절됐어요";
            }

            bool show = text != null;
            if (panel.activeSelf != show)
            {
                panel.SetActive(show);
                transform.SetAsLastSibling();
            }
            if (!show)
            {
                shownText = null;
                return;
            }
            if (buttons.activeSelf != showButtons)
            {
                buttons.SetActive(showButtons);
            }
            if (text != shownText)
            {
                shownText = text;
                message.text = text;
            }
        }

        private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void CreateButton(Transform parent, string label, Color color, Vector2 min, Vector2 max, Font font, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            Stretch((RectTransform)go.transform, min, max);
            go.GetComponent<Image>().color = color;
            go.GetComponent<Button>().onClick.AddListener(onClick);

            var textGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            Stretch((RectTransform)textGo.transform, Vector2.zero, Vector2.one);
            var text = textGo.GetComponent<Text>();
            text.font = font;
            text.fontSize = 18;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.text = label;
        }
    }
}
