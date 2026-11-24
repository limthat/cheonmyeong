using System;
using System.Collections.Generic;
using CheonMyeong.Data;
using CheonMyeong.Player;
using CheonMyeong.Progression;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CheonMyeong.UI.Screens
{
    /// <summary>
    /// MAP_STORY(스토리 컷신) 화면.
    /// 261124 - 비주얼 노벨 방식(지원 아트 씬): 배경 일러스트 + 양쪽 스토리 캐릭터(player1 = 오른쪽, player2 = 왼쪽) + 흰 대사칸.
    /// 양쪽에 누구를 세울지는 대사 줄마다 정한다(예섭: 참가자 수 · 고른 캐릭터와 상관없이 늘 둘 다).
    /// 말하는 사람은 원래 색 · 살짝 크게, 듣는 사람은 그림자(어두운 색). 대사는 한 글자씩 나오고
    /// Space(짧게) · Enter · 클릭으로 넘긴다(글자가 다 안 나왔으면 먼저 다 보여 준다). 대사 목록은 Inspector의 lines에서 고친다.
    /// 261118 - 기획서 v0.9.9 12장 "Space 길게 눌러 건너뛰기 (둘 다 눌러야 넘어감)":
    /// Space를 HoldSeconds 동안 누르고 있거나 건너뛰기 버튼을 누르면 서버에 알리고, 접속한 사람 전원이 누르고 있으면
    /// 모두 함께 인게임으로 넘어간다(PartyProgress.Story). 마지막 대사까지 다 넘긴 사람도 '넘어갈 준비'로 같이 센다.
    /// 네트워크가 없으면 혼자 바로 넘어간다. 스토리 보관함에서 다시 보기로 열었을 때는 혼자 보고, 끝나면 보관함으로 돌아간다.
    /// </summary>
    public class MapStoryScreen : MonoBehaviour
    {
        public enum Speaker
        {
            Narration = 0, // 이름 없음, 양쪽 다 그림자
            Right = 1,     // 오른쪽 캐릭터가 말함
            Left = 2,      // 왼쪽 캐릭터가 말함
            Other = 3,     // speakerName에 적은 인물(억쇠 등), 양쪽 다 그림자
        }

        [Serializable]
        public class Line
        {
            public Speaker speaker;
            [Tooltip("Other일 때 보일 이름(억쇠 등). Left/Right는 비우면 그 캐릭터 이름.")]
            public string speakerName;
            [Tooltip("적이 말하면 켠다 - 이름이 짙은 적색(아군은 군청색).")]
            public bool enemy;
            [Tooltip("대사. Left/Right는 비우면 그 캐릭터의 한마디(CharacterCatalog의 quote).")]
            [TextArea] public string text;
            [Tooltip("이 줄부터 왼쪽에 세울 캐릭터 id(hayoung · imhun · hanjuwon · parkgyuhyeok · leeara · choihwanjo · sinjihyeon · simyeore). 비우면 그대로, '-'면 비움.")]
            public string leftCharacterId;
            [Tooltip("이 줄부터 오른쪽에 세울 캐릭터 id. 비우면 그대로, '-'면 비움.")]
            public string rightCharacterId;
        }

        private const float HoldSeconds = 1f;
        private const float CharsPerSecond = 40f;
        private const float HighlightSpeed = 8f;  // 강조 · 그림자 전환 속도(초당)
        private const float SpeakerScale = 1.05f; // 말하는 사람을 이만큼 크게
        private const float DefaultIdleFps = 8f;
        private const string SkipLabel = "건너뛰기 (Space 길게)";
        private const string WaitingLabel = "기다리는 중…";
        private const string ReplayLabel = "돌아가기";
        private static readonly Color ShadowColor = new Color(0.28f, 0.28f, 0.36f, 1f);
        // 261124 - 예섭: 대사 글자 크기를 줄 수에 맞춰 자동으로(큰 것부터 칸에 들어가는 크기).
        // 2차 - "75%로 줄여줘": 48 · 36 · 24 · 12 → 36 · 27 · 18 · 12, 이름 36 → 27, 안내 24 → 18.
        private static readonly int[] DialogueFontSizes = { 36, 27, 18, 12 };
        private const int SpeakerFontSize = 32; // 261124 3차 - 이름 +20%(27 → 32)
        private static readonly Color AllyNameColor = new Color32(0x1F, 0x35, 0x7A, 0xFF);  // 군청
        private static readonly Color EnemyNameColor = new Color32(0x88, 0x21, 0x31, 0xFF); // 짙은 적(기획서 #882131)
        private const int HintFontSize = 18;

        [SerializeField] private Text speakerNameText;
        [SerializeField] private Text dialogueText;
        [SerializeField] private Text skipHintText;
        [SerializeField] private Button continueButton;
        [Tooltip("오른쪽 캐릭터 그림. 비우면 자식 'player1'을 찾는다.")]
        [SerializeField] private Image player1Image;
        [Tooltip("왼쪽 캐릭터 그림. 비우면 자식 'player2'를 찾는다.")]
        [SerializeField] private Image player2Image;
        [SerializeField] private CharacterCatalogSO characterCatalog;
        [Tooltip("제1화 억쇠 등장 컷([스토리] 7장). 위에서부터 차례대로 보여 준다.")]
        [SerializeField] private List<Line> lines = new List<Line>
        {
            new Line { speaker = Speaker.Narration, leftCharacterId = "imhun", rightCharacterId = "hayoung",
                       text = "징 소리가 잦아든 폐장터. 당산나무 아래로 원귀들의 푸른 빛이 빨려 들어간다." },
            new Line { speaker = Speaker.Other, speakerName = "억쇠", enemy = true, text = "한 맺힌 것들이 이렇게나 많은데… 오늘 밤도 배가 부르겠구나." },
            new Line { speaker = Speaker.Right },
            new Line { speaker = Speaker.Left },
        };

        /// <summary>스토리 보관함이 다시 보기로 열 때 true로 두고 이 화면을 연다.</summary>
        public static bool ReplayFromStorage { get; set; }

        private class Portrait
        {
            public Image Image;
            public Vector3 BaseScale = Vector3.one;
            public CharacterCatalogSO.CharacterEntry Entry;
            public float Highlight; // 1 = 말하는 중(원래 색), 0 = 그림자
            public bool HighlightApplied;
            public float AnimTime;
            public int ShownFrame = -1;
        }

        private readonly Portrait[] portraits = { new Portrait(), new Portrait() };
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

        private void Awake()
        {
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
            for (int i = 0; i < portraits.Length; i++)
            {
                portraits[i].Entry = null;
                portraits[i].ShownFrame = -1;
                portraits[i].HighlightApplied = false;
                if (portraits[i].Image != null)
                {
                    portraits[i].Image.enabled = false;
                }
            }
            ShowLine(0);
            RefreshTexts(true);
        }

        private void OnDisable()
        {
            SendHold(false);
            ReplayFromStorage = false;
        }

        private void HandleContinueClicked()
        {
            if (ReplayFromStorage)
            {
                UIFlowManager.Instance.ShowScreen(UIFlowManager.StoryStorage);
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
                fullText = text;
                if (speakerNameText != null)
                {
                    string speakerLabel = line.speaker == Speaker.Narration ? string.Empty
                        : !string.IsNullOrEmpty(line.speakerName) ? line.speakerName
                        : entry != null ? entry.displayName
                        : string.Empty;
                    speakerNameText.text = speakerLabel.Length > 0 ? "[" + speakerLabel + "]" : string.Empty;
                    speakerNameText.color = line.enemy ? EnemyNameColor : AllyNameColor;
                }
                FitDialogueSize();
                revealed = 0f;
                shownChars = -1;
                DrawDialogue();
                return;
            }
            lineIndex = lines != null ? lines.Count : 0;
            finished = true;
        }

        /// <summary>
        /// 대사 전체가 칸 안에 줄바꿈으로 들어가는 가장 큰 크기를 고른다(한 줄에 한 번, 글자가 찍히는 동안은 크기가 안 바뀜).
        /// UGUI의 BestFit은 크기를 1씩 바꿔 픽셀 글꼴이 뭉개지므로 정해진 크기(12의 배수)만 시험한다.
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

        /// <summary>다음으로. 글자가 다 안 나왔으면 먼저 다 보여 준다. 다시 보기가 끝나 화면을 닫았으면 true.</summary>
        private bool Advance()
        {
            if (finished)
            {
                return false;
            }
            if (!LineFullyShown)
            {
                revealed = fullText.Length;
                DrawDialogue();
                return false;
            }
            ShowLine(lineIndex + 1);
            if (finished && ReplayFromStorage)
            {
                UIFlowManager.Instance.ShowScreen(UIFlowManager.StoryStorage);
                return true;
            }
            return false;
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

        // ───────── 캐릭터(강조 · 그림자 · 대기 모션) ─────────

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
                float fps = p.Entry.lobbyIdleFps > 0f ? p.Entry.lobbyIdleFps : DefaultIdleFps;
                int frame = (int)(p.AnimTime * fps) % frames.Length;
                if (frame != p.ShownFrame)
                {
                    p.ShownFrame = frame;
                    p.Image.sprite = frames[frame];
                }

                bool speaking = !finished && (slot == 0 ? currentSpeaker == Speaker.Right : currentSpeaker == Speaker.Left);
                float target = speaking ? 1f : 0f;
                if (!p.HighlightApplied)
                {
                    p.Highlight = target; // 처음 열 때는 바로 그 상태로
                }
                else if (Mathf.Approximately(p.Highlight, target))
                {
                    continue;
                }
                else
                {
                    p.Highlight = Mathf.MoveTowards(p.Highlight, target, HighlightSpeed * dt);
                }
                p.HighlightApplied = true;
                p.Image.color = Color.Lerp(ShadowColor, Color.white, p.Highlight);
                float scale = Mathf.Lerp(1f, SpeakerScale, p.Highlight);
                p.Image.rectTransform.localScale = new Vector3(p.BaseScale.x * scale, p.BaseScale.y * scale, p.BaseScale.z);
            }
        }

        // ───────── 입력 · 건너뛰기 ─────────

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            UpdatePortraits(dt);
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
            if (next && Advance())
            {
                return; // 다시 보기가 끝나 보관함으로 넘어감
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
                    UIFlowManager.Instance.ShowScreen(UIFlowManager.InGame);
                }
                return;
            }

            if (seenSerial < 0)
            {
                seenSerial = party.StorySkipSerial;
            }
            else if (party.StorySkipSerial != seenSerial)
            {
                UIFlowManager.Instance.ShowScreen(UIFlowManager.InGame);
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
                    skipHintText.text = players > 1
                        ? "Space · 클릭: 다음 · Space 길게: 건너뛰기 " + holders + "/" + players + " · 둘 다 끝나야 넘어감"
                        : "Space · 클릭: 다음 · Space 길게: 건너뛰기";
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
