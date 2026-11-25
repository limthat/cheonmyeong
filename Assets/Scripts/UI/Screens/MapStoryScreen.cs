using System;
using System.Collections.Generic;
using System.Text;
using CheonMyeong.Data;
using CheonMyeong.Player;
using CheonMyeong.Progression;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CheonMyeong.UI.Screens
{
    /// <summary>
    /// MAP_STORY(스토리 컷신) 화면 - 261124 [컷신] 천명_인트로_261124(지원) 그대로.
    /// - 배치: 하영은 왼쪽 고정(가로 반전), 오른쪽은 그 장면의 상대(박규혁 → 심여레 → 임훈, 원본 방향). 상대가 바뀔 때 오른쪽만 교체.
    /// - 밝기: 말하는 사람 100% · 조금 크게(630px), 듣는 사람 50%.
    /// - 억쇠 장면: 화면을 남색 검정(약 70%)으로 덮고 가운데에 억쇠(밝기 18%). 말하는 사람 80%, 나머지 32%.
    ///   등장 줄에서 화면 흔들림, 장면이 끝나면 그림자가 걷히고 밝기 복구.
    /// - 대화창: 한지색 칸, 이름표는 말하는 사람 쪽. 억쇠는 ??? · 짙은 적.
    /// - 처음 페이드 인, 끝나면 페이드 아웃 뒤 1웨이브.
    /// 넘기기: Space 짧게 · Enter · 클릭 = 다음 대사(글자가 다 안 나왔으면 먼저 다 보여 줌), Space 길게 = 건너뛰기.
    /// 261118 - 기획서 v0.9.9 12장 "둘 다 눌러야 넘어감": 길게 누르거나 끝까지 본 사람을 서버에 알리고 전원이 모이면 함께 넘어간다
    /// (PartyProgress.Story). 네트워크가 없으면 혼자 바로. 스토리 보관함 다시 보기는 혼자 보고 끝나면 보관함으로.
    /// </summary>
    public class MapStoryScreen : MonoBehaviour
    {
        public enum Speaker
        {
            Narration = 0, // 이름 없음
            Right = 1,     // 오른쪽 캐릭터가 말함
            Left = 2,      // 왼쪽 캐릭터가 말함
            Other = 3,     // speakerName에 적은 인물(억쇠 등)
        }

        [Serializable]
        public class Line
        {
            public Speaker speaker;
            [Tooltip("Other일 때 보일 이름(억쇠는 ???). Left/Right는 비우면 그 캐릭터 이름.")]
            public string speakerName;
            [Tooltip("적이 말하면 켠다 - 이름이 짙은 적색(아군은 군청색).")]
            public bool enemy;
            [Tooltip("대사. Left/Right는 비우면 그 캐릭터의 한마디(CharacterCatalog의 quote).")]
            [TextArea] public string text;
            [Tooltip("이 줄부터 왼쪽에 세울 캐릭터 id(hayoung · imhun · hanjuwon · parkgyuhyeok · leeara · choihwanjo · sinjihyeon · simyeore). 비우면 그대로, '-'면 비움.")]
            public string leftCharacterId;
            [Tooltip("이 줄부터 오른쪽에 세울 캐릭터 id. 비우면 그대로, '-'면 비움.")]
            public string rightCharacterId;
            [Tooltip("억쇠 장면(남색 그림자 + 가운데 억쇠). 켜진 줄이 이어지는 동안 유지된다.")]
            public bool eoksoeScene;
            [Tooltip("이 줄이 시작될 때 화면을 흔든다.")]
            public bool shake;
        }

        private const float HoldSeconds = 1f;
        private const float CharsPerSecond = 40f;
        private const float HighlightSpeed = 6f;  // 밝기 · 크기 전환 속도(초당)
        private const float SpeakerScale = 1.05f; // 말하는 사람 600 → 630px
        private const float DefaultIdleFps = 8f;
        private const float FadeSeconds = 0.6f;
        private const float ShadeSpeed = 3f;      // 억쇠 그림자가 드리우고 걷히는 속도(초당)
        private const float ShakeSeconds = 0.5f;
        private const float ShakeAmplitude = 14f; // 화면 px
        private const float EoksoeFps = 8f;
        private const string SkipLabel = "건너뛰기";
        private const string WaitingLabel = "기다리는 중…";
        private const string ReplayLabel = "돌아가기";

        // 기획서 밝기 값.
        private const float SpeakingBright = 1f;
        private const float ListeningBright = 0.5f;
        private const float ShadeSpeakingBright = 0.8f;
        private const float ShadeListeningBright = 0.32f;
        private const float EoksoeBright = 0.18f;
        private static readonly Color ShadeColor = new Color32(0x0B, 0x0D, 0x22, 0xB3); // 남색 검정 · 불투명도 약 70%

        private static readonly int[] DialogueFontSizes = { 36, 27, 18, 12 };
        private const int SpeakerFontSize = 32;
        private static readonly Color AllyNameColor = new Color32(0x1F, 0x35, 0x7A, 0xFF);  // 군청
        private static readonly Color EnemyNameColor = new Color32(0x88, 0x21, 0x31, 0xFF); // 짙은 적(기획서 #882131)
        private const int HintFontSize = 18;

        [SerializeField] private Text speakerNameText;
        [SerializeField] private Text dialogueText;
        [SerializeField] private Text skipHintText;
        [SerializeField] private Button continueButton;
        [Tooltip("오른쪽 캐릭터 그림. 비우면 자식 'player1'을 찾는다.")]
        [SerializeField] private Image player1Image;
        [Tooltip("왼쪽 캐릭터 그림(가로 반전). 비우면 자식 'player2'를 찾는다.")]
        [SerializeField] private Image player2Image;
        [SerializeField] private CharacterCatalogSO characterCatalog;
        [Tooltip("억쇠 장면에서 가운데에 보일 억쇠 대기 모션(Eoksoe-Sheet IDLE).")]
        [SerializeField] private Sprite[] eoksoeFrames = Array.Empty<Sprite>();
        [Tooltip("억쇠 그림 크기(화면 px, 정사각 256px 칸 기준).")]
        [SerializeField] private float eoksoeSize = 640f;

        [Tooltip("[컷신] 천명_인트로_261124 대본 19줄. 위에서부터 차례대로 보여 준다.")]
        [SerializeField] private List<Line> lines = new List<Line>
        {
            new Line { speaker = Speaker.Left, leftCharacterId = "hayoung", rightCharacterId = "parkgyuhyeok",
                       text = "다 모였네요. 오늘 밤 순서, 다시 확인하겠습니다." },
            new Line { speaker = Speaker.Right, text = "(하품) …또 밤이냐. 해 좀 보고 죽자." },
            new Line { speaker = Speaker.Left, text = "해를 보려면 오늘 밤을 넘겨야죠. 동쪽부터 막습니다." },
            new Line { speaker = Speaker.Right, text = "알았다, 알았어. 앞은 내가 선다. 넌 뒤에서 판이나 짜." },
            new Line { speaker = Speaker.Right, rightCharacterId = "simyeore", text = "하영아. 너 또 아무것도 안 먹고 나왔지." },
            new Line { speaker = Speaker.Right, text = "(주먹밥을 내밀며) 하영아. 먹어. 안 먹으면 못 들어가." },
            new Line { speaker = Speaker.Left, text = "(굳은 채로 받는다) …감사합니다." },
            new Line { speaker = Speaker.Right, text = "그래, 그거면 됐어. 오늘도 같이 돌아오는 거야." },
            new Line { speaker = Speaker.Right, rightCharacterId = "imhun", text = "(장부를 넘기며) 어제 놓친 원귀가 많아. 장터 북쪽이 특히." },
            new Line { speaker = Speaker.Left, text = "북쪽은 제가 맡겠습니다. 임훈 님은—" },
            new Line { speaker = Speaker.Other, speakerName = "???", enemy = true, eoksoeScene = true, shake = true,
                       text = "흐흐… 왔구나, 무당 것들." },
            new Line { speaker = Speaker.Right, eoksoeScene = true, text = "(장부를 덮으며) …저게 원한 덩어리야. 억쇠." },
            new Line { speaker = Speaker.Other, speakerName = "???", enemy = true, eoksoeScene = true,
                       text = "이 장터에 쌓인 원한이 얼마인지 아느냐. 놓친 원귀 하나하나가 내 살이 된다." },
            new Line { speaker = Speaker.Right, rightCharacterId = "simyeore", eoksoeScene = true,
                       text = "(부채를 펴며, 낮게) …지금 우리 애들 앞에서 배부르다고 한 거니?" },
            new Line { speaker = Speaker.Other, speakerName = "???", enemy = true, eoksoeScene = true,
                       text = "많이 놓쳐라. 남길수록 나는 커진다. 해 뜨기 전에 다시 보자." },
            new Line { speaker = Speaker.Left, text = "…이번엔 하나도 놓치지 않습니다." },
            new Line { speaker = Speaker.Right, rightCharacterId = "imhun", text = "그럼 계산은 간단하네. 남기지 말 것." },
            new Line { speaker = Speaker.Right, text = "하영, 어디부터 칠 거야." },
            new Line { speaker = Speaker.Left, text = "북쪽입니다. (사인검을 뽑는다) 계획대로, 시작하죠." },
        };

        /// <summary>스토리 보관함이 다시 보기로 열 때 true로 두고 이 화면을 연다.</summary>
        public static bool ReplayFromStorage { get; set; }

        private class Portrait
        {
            public Image Image;
            public Vector3 BaseScale = Vector3.one;
            public CharacterCatalogSO.CharacterEntry Entry;
            public float Brightness = -1f; // 지금 보이는 밝기(-1 = 아직 안 칠함)
            public float Emphasis;         // 1 = 말하는 중(크게)
            public float AnimTime;
            public int ShownFrame = -1;
        }

        private readonly Portrait[] portraits = { new Portrait(), new Portrait() };
        private readonly StringBuilder hintBuilder = new StringBuilder(64);
        private Text continueLabel;
        private float holdTime;
        private bool keyHolding;
        private bool buttonHolding;
        private bool sentHold;
        private int seenSerial = -1;
        private int shownHolders = -1;
        private int shownPlayers = -1;
        private bool shownWaiting;

        private int lineIndex;
        private bool finished;
        private string fullText = string.Empty;
        private float revealed;
        private int shownChars = -1;
        private Speaker currentSpeaker;
        private bool currentEoksoeScene;

        // 억쇠 장면 · 화면 흔들림 · 페이드(실행 중에 만든다 - 씬 배치는 그대로).
        private Image shadeImage;
        private Image eoksoeImage;
        private Image fadeImage;
        private float shade;          // 0 = 그림자 없음, 1 = 억쇠 장면
        private float eoksoeAnimTime;
        private int eoksoeShownFrame = -1;
        private float shakeLeft;
        private RectTransform rootRect;
        private Vector2 rootBasePosition;
        private float fadeAlpha;
        private bool leaving;
        private float leaveTimer;
        private string leaveTarget;

        private void Awake()
        {
            rootRect = (RectTransform)transform;
            rootBasePosition = rootRect.anchoredPosition;
            if (continueButton != null)
            {
                continueLabel = continueButton.GetComponentInChildren<Text>(true);
                continueButton.onClick.AddListener(HandleContinueClicked);
            }
            if (player1Image == null)
            {
                player1Image = FindImage("player1");
            }
            if (player2Image == null)
            {
                player2Image = FindImage("player2");
            }
            if (dialogueText != null)
            {
                dialogueText.horizontalOverflow = HorizontalWrapMode.Wrap; // 자동 줄바꿈
                dialogueText.verticalOverflow = VerticalWrapMode.Truncate;
                dialogueText.resizeTextForBestFit = false;
                dialogueText.alignment = TextAnchor.UpperLeft;
            }
            if (speakerNameText != null)
            {
                speakerNameText.fontSize = SpeakerFontSize;
                GameFontSet.Apply(speakerNameText, FontRole.Body, true); // 굵게(Galmuri11 Bold)
                speakerNameText.horizontalOverflow = HorizontalWrapMode.Overflow;
                speakerNameText.verticalOverflow = VerticalWrapMode.Overflow;
            }
            if (skipHintText != null)
            {
                skipHintText.fontSize = HintFontSize;
            }
            portraits[0].Image = player1Image;
            portraits[1].Image = player2Image;
            for (int i = 0; i < portraits.Length; i++)
            {
                if (portraits[i].Image != null)
                {
                    portraits[i].BaseScale = portraits[i].Image.rectTransform.localScale;
                    portraits[i].Image.raycastTarget = false;
                }
            }
            BuildSceneLayers();
        }

        /// <summary>
        /// 그리는 순서: 배경(이 화면 Image) → 남색 그림자 → 억쇠 → 두 캐릭터 → 대화창 → 페이드(맨 위).
        /// 캐릭터 그림 바로 앞에 그림자 · 억쇠를 끼워 넣는다.
        /// </summary>
        private void BuildSceneLayers()
        {
            int insertAt = transform.childCount;
            for (int i = 0; i < portraits.Length; i++)
            {
                if (portraits[i].Image != null)
                {
                    insertAt = Mathf.Min(insertAt, portraits[i].Image.transform.GetSiblingIndex());
                }
            }

            shadeImage = NewImage("EoksoeShade", insertAt);
            Stretch(shadeImage.rectTransform);
            shadeImage.color = new Color(ShadeColor.r, ShadeColor.g, ShadeColor.b, 0f);
            shadeImage.enabled = false;

            eoksoeImage = NewImage("Eoksoe", insertAt + 1);
            RectTransform er = eoksoeImage.rectTransform;
            er.anchorMin = er.anchorMax = new Vector2(0.5f, 0.5f);
            er.pivot = new Vector2(0.5f, 0f);
            er.sizeDelta = new Vector2(eoksoeSize, eoksoeSize);
            // 발 높이 = 캐릭터와 같은 줄(대화창 위 가장자리). 억쇠 그림 칸 아래 7px(256 기준)이 비어 있어 그만큼 내린다.
            float feetY = portraits[0].Image != null ? portraits[0].Image.rectTransform.anchoredPosition.y : -186f;
            er.anchoredPosition = new Vector2(0f, feetY - eoksoeSize * (7f / 256f));
            eoksoeImage.preserveAspect = true;
            eoksoeImage.enabled = false;

            fadeImage = NewImage("Fade", transform.childCount);
            Stretch(fadeImage.rectTransform);
            fadeImage.color = new Color(0f, 0f, 0f, 0f);
            fadeImage.enabled = false;
        }

        private Image NewImage(string objectName, int siblingIndex)
        {
            var go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(transform, false);
            go.transform.SetSiblingIndex(Mathf.Clamp(siblingIndex, 0, transform.childCount - 1));
            Image image = go.GetComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private Image FindImage(string childName)
        {
            Transform child = transform.Find(childName);
            return child != null ? child.GetComponent<Image>() : null;
        }

        private void OnDestroy()
        {
            if (continueButton != null)
            {
                continueButton.onClick.RemoveListener(HandleContinueClicked);
            }
        }

        private void OnEnable()
        {
            holdTime = 0f;
            keyHolding = false;
            buttonHolding = false;
            sentHold = false;
            seenSerial = -1;
            finished = false;
            leaving = false;
            shade = 0f;
            shakeLeft = 0f;
            currentEoksoeScene = false;
            if (rootRect != null)
            {
                rootRect.anchoredPosition = rootBasePosition;
            }
            for (int i = 0; i < portraits.Length; i++)
            {
                portraits[i].Entry = null;
                portraits[i].ShownFrame = -1;
                portraits[i].Brightness = -1f;
                if (portraits[i].Image != null)
                {
                    portraits[i].Image.enabled = false;
                }
            }
            ApplyShade();
            fadeAlpha = 1f; // 페이드 인
            ApplyFade();
            ShowLine(0);
            RefreshTexts(true);
        }

        private void OnDisable()
        {
            SendHold(false);
            ReplayFromStorage = false;
            if (rootRect != null)
            {
                rootRect.anchoredPosition = rootBasePosition;
            }
        }

        private void HandleContinueClicked()
        {
            if (ReplayFromStorage)
            {
                BeginLeave(UIFlowManager.StoryStorage);
                return;
            }
            buttonHolding = !buttonHolding; // 버튼은 누를 때마다 건너뛰기 / 취소
        }

        // ───────── 대사 진행 ─────────

        private void ShowLine(int index)
        {
            while (lines != null && index < lines.Count)
            {
                Line line = lines[index];
                ApplyCharacter(0, line.rightCharacterId);
                ApplyCharacter(1, line.leftCharacterId);
                CharacterCatalogSO.CharacterEntry entry = SpeakerEntry(line.speaker);
                string text = !string.IsNullOrEmpty(line.text) ? line.text
                    : entry != null && !string.IsNullOrEmpty(entry.quote) ? entry.quote
                    : string.Empty;
                if (string.IsNullOrEmpty(text))
                {
                    index++; // 대사가 없는 줄은 건너뛴다
                    continue;
                }
                lineIndex = index;
                currentSpeaker = line.speaker;
                currentEoksoeScene = line.eoksoeScene;
                if (line.shake)
                {
                    shakeLeft = ShakeSeconds;
                }
                fullText = text;
                if (speakerNameText != null)
                {
                    string speakerLabel = line.speaker == Speaker.Narration ? string.Empty
                        : !string.IsNullOrEmpty(line.speakerName) ? line.speakerName
                        : entry != null ? entry.displayName
                        : string.Empty;
                    speakerNameText.text = speakerLabel.Length > 0 ? "[" + speakerLabel + "]" : string.Empty;
                    speakerNameText.color = line.enemy ? EnemyNameColor : AllyNameColor;
                    // 이름표는 말하는 사람 쪽(오른쪽 캐릭터면 오른쪽 끝).
                    speakerNameText.alignment = line.speaker == Speaker.Right ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
                }
                FitDialogueSize();
                revealed = 0f;
                shownChars = -1;
                DrawDialogue();
                return;
            }
            lineIndex = lines != null ? lines.Count : 0;
            finished = true;
            currentEoksoeScene = false;
        }

        /// <summary>
        /// 대사 전체가 칸 안에 줄바꿈으로 들어가는 가장 큰 크기를 고른다(한 줄에 한 번, 글자가 찍히는 동안은 크기가 안 바뀜).
        /// UGUI의 BestFit은 크기를 1씩 바꿔 픽셀 글꼴이 뭉개지므로 정해진 크기만 시험한다.
        /// </summary>
        private void FitDialogueSize()
        {
            if (dialogueText == null)
            {
                return;
            }
            Vector2 area = dialogueText.rectTransform.rect.size;
            if (area.x <= 0f || area.y <= 0f)
            {
                return;
            }
            TextGenerator generator = dialogueText.cachedTextGeneratorForLayout;
            int chosen = DialogueFontSizes[DialogueFontSizes.Length - 1];
            for (int i = 0; i < DialogueFontSizes.Length; i++)
            {
                dialogueText.fontSize = DialogueFontSizes[i];
                TextGenerationSettings settings = dialogueText.GetGenerationSettings(new Vector2(area.x, 0f));
                float height = generator.GetPreferredHeight(fullText, settings) / dialogueText.pixelsPerUnit;
                if (height <= area.y)
                {
                    chosen = DialogueFontSizes[i];
                    break;
                }
            }
            dialogueText.fontSize = chosen;
        }

        private CharacterCatalogSO.CharacterEntry SpeakerEntry(Speaker speaker)
        {
            int slot = speaker == Speaker.Right ? 0 : speaker == Speaker.Left ? 1 : -1;
            return slot >= 0 ? portraits[slot].Entry : null;
        }

        private bool LineFullyShown => shownChars >= fullText.Length;

        private void Advance()
        {
            if (finished || leaving)
            {
                return;
            }
            if (!LineFullyShown)
            {
                revealed = fullText.Length;
                DrawDialogue();
                return;
            }
            ShowLine(lineIndex + 1);
            if (finished && ReplayFromStorage)
            {
                BeginLeave(UIFlowManager.StoryStorage);
            }
        }

        private void DrawDialogue()
        {
            int count = Mathf.Clamp(Mathf.FloorToInt(revealed), 0, fullText.Length);
            if (count == shownChars)
            {
                return;
            }
            shownChars = count;
            if (dialogueText != null)
            {
                dialogueText.text = count >= fullText.Length ? fullText : fullText.Substring(0, count);
            }
        }

        // ───────── 캐릭터(밝기 · 크기 · 대기 모션) ─────────

        /// <summary>slot 0 = 오른쪽, 1 = 왼쪽. id가 비면 그대로, "-"면 비운다.</summary>
        private void ApplyCharacter(int slot, string characterId)
        {
            if (string.IsNullOrEmpty(characterId))
            {
                return;
            }
            CharacterCatalogSO catalog = Catalog;
            CharacterCatalogSO.CharacterEntry entry = characterId == "-" || catalog == null ? null : catalog.FindById(characterId.Trim());
            if (entry == null && characterId != "-")
            {
                Debug.LogWarning($"[CheonMyeong] 컷신 캐릭터 id를 못 찾음: {characterId}");
            }
            Portrait p = portraits[slot];
            if (entry == p.Entry)
            {
                return;
            }
            p.Entry = entry;
            p.ShownFrame = -1;
            p.AnimTime = 0f;
            if (p.Image != null)
            {
                p.Image.enabled = entry != null && entry.lobbyIdleFrames != null && entry.lobbyIdleFrames.Length > 0;
            }
        }

        private CharacterCatalogSO Catalog
        {
            get
            {
                if (characterCatalog == null)
                {
                    LobbyScreen lobby = FindFirstObjectByType<LobbyScreen>(FindObjectsInactive.Include);
                    characterCatalog = lobby != null ? lobby.Catalog : null;
                }
                return characterCatalog;
            }
        }

        private void UpdatePortraits(float dt)
        {
            for (int slot = 0; slot < portraits.Length; slot++)
            {
                Portrait p = portraits[slot];
                if (p.Image == null || !p.Image.enabled || p.Entry == null)
                {
                    continue;
                }
                Sprite[] frames = p.Entry.lobbyIdleFrames;
                p.AnimTime += dt;
                float fps = (p.Entry.lobbyIdleFps > 0f ? p.Entry.lobbyIdleFps : DefaultIdleFps) * CheonMyeong.Data.CharacterCatalogSO.IdleSpeedScale;
                int frame = (int)(p.AnimTime * fps) % frames.Length;
                if (frame != p.ShownFrame)
                {
                    p.ShownFrame = frame;
                    p.Image.sprite = frames[frame];
                }

                bool speaking = !finished && (slot == 0 ? currentSpeaker == Speaker.Right : currentSpeaker == Speaker.Left);
                // 억쇠 장면(그림자 정도 shade)에 따라 말하는 사람 100 → 80%, 듣는 사람 50 → 32%.
                float speakingBright = Mathf.Lerp(SpeakingBright, ShadeSpeakingBright, shade);
                float listeningBright = Mathf.Lerp(ListeningBright, ShadeListeningBright, shade);
                float targetBright = speaking ? speakingBright : listeningBright;
                float targetEmphasis = speaking ? 1f : 0f;
                bool first = p.Brightness < 0f;
                float bright = first ? targetBright : Mathf.MoveTowards(p.Brightness, targetBright, HighlightSpeed * dt);
                float emphasis = first ? targetEmphasis : Mathf.MoveTowards(p.Emphasis, targetEmphasis, HighlightSpeed * dt);
                if (!first && Mathf.Approximately(bright, p.Brightness) && Mathf.Approximately(emphasis, p.Emphasis))
                {
                    continue;
                }
                p.Brightness = bright;
                p.Emphasis = emphasis;
                p.Image.color = new Color(bright, bright, bright, 1f);
                float scale = Mathf.Lerp(1f, SpeakerScale, emphasis);
                p.Image.rectTransform.localScale = new Vector3(p.BaseScale.x * scale, p.BaseScale.y * scale, p.BaseScale.z);
            }
        }

        // ───────── 억쇠 장면 · 흔들림 · 페이드 ─────────

        private void UpdateShade(float dt)
        {
            float target = currentEoksoeScene ? 1f : 0f;
            if (!Mathf.Approximately(shade, target))
            {
                shade = Mathf.MoveTowards(shade, target, ShadeSpeed * dt);
                ApplyShade();
            }
            if (eoksoeImage != null && eoksoeImage.enabled && eoksoeFrames != null && eoksoeFrames.Length > 0)
            {
                eoksoeAnimTime += dt;
                int frame = (int)(eoksoeAnimTime * EoksoeFps) % eoksoeFrames.Length;
                if (frame != eoksoeShownFrame)
                {
                    eoksoeShownFrame = frame;
                    eoksoeImage.sprite = eoksoeFrames[frame];
                }
            }
        }

        private void ApplyShade()
        {
            bool visible = shade > 0.001f;
            if (shadeImage != null)
            {
                shadeImage.enabled = visible;
                shadeImage.color = new Color(ShadeColor.r, ShadeColor.g, ShadeColor.b, ShadeColor.a * shade);
            }
            if (eoksoeImage != null)
            {
                bool hasFrames = eoksoeFrames != null && eoksoeFrames.Length > 0;
                eoksoeImage.enabled = visible && hasFrames;
                if (eoksoeImage.enabled && eoksoeImage.sprite == null)
                {
                    eoksoeImage.sprite = eoksoeFrames[0];
                }
                eoksoeImage.color = new Color(EoksoeBright, EoksoeBright, EoksoeBright, shade);
            }
        }

        private void UpdateShake(float dt)
        {
            if (rootRect == null)
            {
                return;
            }
            if (shakeLeft > 0f)
            {
                shakeLeft -= dt;
                float strength = ShakeAmplitude * Mathf.Clamp01(shakeLeft / ShakeSeconds);
                rootRect.anchoredPosition = rootBasePosition + UnityEngine.Random.insideUnitCircle * strength;
                if (shakeLeft <= 0f)
                {
                    rootRect.anchoredPosition = rootBasePosition;
                }
            }
        }

        private void UpdateFade(float dt)
        {
            float target = leaving ? 1f : 0f;
            if (!Mathf.Approximately(fadeAlpha, target))
            {
                fadeAlpha = Mathf.MoveTowards(fadeAlpha, target, dt / FadeSeconds);
                ApplyFade();
            }
            if (leaving)
            {
                leaveTimer -= dt;
                if (leaveTimer <= 0f)
                {
                    leaving = false;
                    UIFlowManager.Instance.ShowScreen(leaveTarget);
                }
            }
        }

        private void ApplyFade()
        {
            if (fadeImage == null)
            {
                return;
            }
            fadeImage.enabled = fadeAlpha > 0.001f;
            fadeImage.color = new Color(0f, 0f, 0f, fadeAlpha);
        }

        /// <summary>페이드 아웃이 끝나면 화면을 넘긴다(1웨이브 · 보관함).</summary>
        private void BeginLeave(string target)
        {
            if (leaving)
            {
                return;
            }
            leaving = true;
            leaveTarget = target;
            leaveTimer = FadeSeconds;
        }

        // ───────── 입력 · 건너뛰기 ─────────

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            UpdatePortraits(dt);
            UpdateShade(dt);
            UpdateShake(dt);
            UpdateFade(dt);
            if (!gameObject.activeInHierarchy || leaving)
            {
                return;
            }
            if (!finished && !LineFullyShown)
            {
                revealed += CharsPerSecond * dt;
                DrawDialogue();
            }

            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            bool spacePressed = keyboard != null && keyboard.spaceKey.isPressed;
            if (spacePressed)
            {
                holdTime += dt;
                if (holdTime >= HoldSeconds && !ReplayFromStorage)
                {
                    keyHolding = true;
                }
            }
            // Space는 짧게 눌렀다 떼면 다음 대사, 길게 누르면 건너뛰기라 '뗄 때' 판단한다.
            bool next = (keyboard != null && keyboard.spaceKey.wasReleasedThisFrame && holdTime < HoldSeconds)
                        || (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))
                        || (mouse != null && mouse.leftButton.wasPressedThisFrame && !PointerOverContinueButton(mouse));
            if (!spacePressed)
            {
                holdTime = 0f;
                keyHolding = false;
            }
            if (next)
            {
                Advance();
                if (leaving)
                {
                    return; // 다시 보기가 끝나 보관함으로 넘어가는 중
                }
            }

            if (ReplayFromStorage)
            {
                return;
            }

            bool wantSkip = keyHolding || buttonHolding || finished;
            PartyProgress party = PartyProgress.Instance;
            if (party == null || !party.IsSpawned)
            {
                // 네트워크 없이(혼자 · 에디터 테스트) 열렸으면 바로 넘어간다.
                if (wantSkip)
                {
                    BeginLeave(UIFlowManager.InGame);
                }
                return;
            }

            if (seenSerial < 0)
            {
                seenSerial = party.StorySkipSerial;
            }
            else if (party.StorySkipSerial != seenSerial)
            {
                BeginLeave(UIFlowManager.InGame);
                return;
            }

            SendHold(wantSkip);
            RefreshTexts(false);
        }

        private bool PointerOverContinueButton(Mouse mouse)
        {
            if (continueButton == null || !continueButton.gameObject.activeInHierarchy)
            {
                return false;
            }
            Canvas canvas = continueButton.GetComponentInParent<Canvas>();
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            return RectTransformUtility.RectangleContainsScreenPoint((RectTransform)continueButton.transform, mouse.position.ReadValue(), cam);
        }

        private void SendHold(bool holding)
        {
            if (holding == sentHold)
            {
                return;
            }
            sentHold = holding;
            PartyProgress party = PartyProgress.Instance;
            if (party != null && party.IsSpawned)
            {
                party.SetStorySkipHold(holding);
            }
        }

        private void RefreshTexts(bool force)
        {
            if (ReplayFromStorage)
            {
                if (force)
                {
                    if (skipHintText != null)
                    {
                        skipHintText.text = "다시 보기 · Space · 클릭으로 넘기기";
                    }
                    if (continueLabel != null)
                    {
                        continueLabel.text = ReplayLabel;
                    }
                }
                return;
            }
            PartyProgress party = PartyProgress.Instance;
            int holders = party != null && party.IsSpawned ? party.StorySkipHolders : (sentHold ? 1 : 0);
            int players = Mathf.Max(1, PlayerHealth.ActivePlayers.Count);
            if (force || holders != shownHolders || players != shownPlayers)
            {
                shownHolders = holders;
                shownPlayers = players;
                if (skipHintText != null)
                {
                    // 기획서: 우측 하단 "Space 길게 눌러 넘기기 ●○" - 넘길 준비가 된 사람 수만큼 ●.
                    hintBuilder.Clear();
                    hintBuilder.Append("Space 길게 눌러 넘기기 ");
                    for (int i = 0; i < players; i++)
                    {
                        hintBuilder.Append(i < holders ? '●' : '○');
                    }
                    skipHintText.text = hintBuilder.ToString();
                }
            }
            if (force || sentHold != shownWaiting)
            {
                shownWaiting = sentHold;
                if (continueLabel != null)
                {
                    continueLabel.text = sentHold ? WaitingLabel : SkipLabel;
                }
            }
        }
    }
}
