using System;
using System.Collections.Generic;
using CheonMyeong.Input;
using CheonMyeong.Settings;
using UnityEngine;
using UnityEngine.UI;

namespace CheonMyeong.UI.Screens
{
    /// <summary>
    /// 261115 재작성 - 설정 창(화면 · 소리 · 조작 · 기타 탭). 로비 / ESC 메뉴 / 타이틀 어디서 열어도 같은 화면을 쓴다.
    /// - 열 때 GameSettings.Saved를 복사한 작업본을 고친다. 고치는 즉시 미리 보여 주고(GameSettings.Preview),
    ///   '저장'을 눌러야 PlayerPrefs에 남는다. 저장하지 않고 닫으면(뒤로 · ESC · 일시정지 해제 등) OnDisable에서 되돌린다.
    /// - '기본값'은 작업본만 기본값으로 바꾼다(저장 눌러야 확정).
    /// - 씬 오브젝트를 이름으로 찾는다(아트가 모양을 바꿔도 이름만 같으면 그대로 동작):
    ///   Tab_* / Page_* / Row_*(Opt_0..n · Slider · Value · Prev · Next) / BackButton · DefaultsButton · SaveButton · QuitButton · StatusText.
    ///   고르는 버튼(Opt_i, Tab_*)은 자식 Label(글자) · Underline(고른 칸 밑줄)을 쓴다.
    /// - 소리는 전체 음량만 실제 반영(AudioListener). 나머지는 값만 저장 - 소리 넣을 때 GameSettings.Active에서 읽으면 된다.
    /// - 261116 조작 키 바꾸기: Bind_&lt;GameAction&gt;/Button을 누르면 다음 키 하나를 받아 바로 저장한다(KeyBindings). ESC = 취소.
    ///   KeyReset 버튼 · '기본값'은 키도 기본으로 되돌린다. 방향키 · ESC · 마우스 · F1~F12는 고정.
    /// </summary>
    public class SettingsScreen : MonoBehaviour
    {
        // 261121 - 기획서 v1.2 버튼 규칙: 글씨는 늘 먹(#080710), 고른 칸은 흰 테두리 그림(Btn 2 = 호버 · 선택용) + 황(#E7BA41) 밑줄.
        private static readonly Color ChoiceOn = Color.white;
        private static readonly Color ChoiceOff = Color.white;
        private static readonly Color LabelOn = new Color32(0x08, 0x07, 0x10, 0xFF);
        private static readonly Color LabelOff = new Color32(0x08, 0x07, 0x10, 0xFF);
        private static readonly Color SelectedLine = new Color32(0xE7, 0xBA, 0x41, 0xFF);
        private static readonly Color SliderFill = new Color32(0x2D, 0x51, 0x8B, 0xFF); // 청 - 영혼 바와 같은 색

        private static readonly string[] TabNames = { "Display", "Sound", "Control", "Etc" };

        /// <summary>버튼 여러 개 중 하나를 고르는 줄. 고른 칸 = 흰 바탕 + 파란 밑줄, 나머지 = 크림색.</summary>
        private sealed class Choice
        {
            public Button[] Buttons;
            public Image[] Images;
            public Text[] Labels;
            public GameObject[] Underlines;
            public Sprite[] NormalSprites;
            public Sprite[] SelectedSprites;

            public void Show(int selected, bool interactable = true)
            {
                for (int i = 0; i < Buttons.Length; i++)
                {
                    if (Buttons[i] == null)
                    {
                        continue;
                    }
                    bool on = i == selected;
                    Buttons[i].interactable = interactable;
                    if (Images[i] != null)
                    {
                        Images[i].color = on ? ChoiceOn : ChoiceOff;
                        Sprite sprite = on ? SelectedSprites[i] : NormalSprites[i];
                        if (sprite != null && Images[i].sprite != sprite)
                        {
                            Images[i].sprite = sprite;
                        }
                    }
                    if (Labels[i] != null)
                    {
                        Labels[i].color = on ? LabelOn : LabelOff;
                    }
                    if (Underlines[i] != null && Underlines[i].activeSelf != on)
                    {
                        Underlines[i].SetActive(on);
                    }
                }
            }
        }

        private sealed class VolumeRow
        {
            public Slider Slider;
            public Text Value;
        }

        private Choice tabs;
        private GameObject[] pages;
        private Choice screenMode, vsync, frameCap, shake, ally, muteBg, cursorSize, confine, damageNumbers, hitEffects;
        private VolumeRow master, bgm, sfx, voice;
        private Text resolutionText;
        private Text statusText;
        private Button saveButton;

        private static readonly GameAction[] BindActions =
        {
            GameAction.MoveUp, GameAction.MoveDown, GameAction.MoveLeft, GameAction.MoveRight, GameAction.Interact, GameAction.Skill,
        };
        private readonly Button[] bindButtons = new Button[BindActions.Length];
        private readonly Text[] bindLabels = new Text[BindActions.Length];
        private int capturing = -1;

        private SettingsData working;
        private int tabIndex;
        private bool bound;
        private bool refreshing;

        private void Awake()
        {
            Bind();
        }

        private void OnEnable()
        {
            if (!bound)
            {
                return;
            }
            GameSettings.SyncScreen();
            working = GameSettings.Saved;
            SelectTab(tabIndex);
            Refresh();
            RefreshKeys();
            SetStatus(string.Empty);
            KeyBindings.Changed += RefreshKeys;
        }

        private void OnDisable()
        {
            KeyBindings.Changed -= RefreshKeys;
            StopCapture(false);
            // 저장하지 않고 닫힘 → 미리보기로 바꿔 둔 값을 저장본으로 되돌린다.
            if (bound && !working.Equals(GameSettings.Saved))
            {
                GameSettings.Revert();
                working = GameSettings.Saved;
            }
        }

        private void Bind()
        {
            bound = true;
            Transform root = transform;

            tabs = MakeChoice(FindButtons(root, "Tab_", TabNames));
            for (int i = 0; i < tabs.Buttons.Length; i++)
            {
                int index = i;
                tabs.Buttons[i]?.onClick.AddListener(() => SelectTab(index));
            }
            pages = new GameObject[TabNames.Length];
            for (int i = 0; i < TabNames.Length; i++)
            {
                Transform page = FindDeep(root, "Page_" + TabNames[i]);
                pages[i] = page != null ? page.gameObject : null;
            }

            // 화면
            screenMode = BindChoice(root, "Row_ScreenMode", i => working.ScreenMode = i);
            vsync = BindChoice(root, "Row_VSync", i => working.VSync = i == 0);
            frameCap = BindChoice(root, "Row_FrameCap", i => working.FrameCapIndex = i);
            shake = BindChoice(root, "Row_Shake", i => working.Shake = i);
            BindButton(root, "Row_Resolution/Prev", () => StepResolution(-1));
            BindButton(root, "Row_Resolution/Next", () => StepResolution(1));
            resolutionText = FindComponent<Text>(root, "Row_Resolution/Value");

            // 소리
            master = BindVolume(root, "Row_Master", v => working.Master = v);
            bgm = BindVolume(root, "Row_Bgm", v => working.Bgm = v);
            sfx = BindVolume(root, "Row_Sfx", v => working.Sfx = v);
            voice = BindVolume(root, "Row_Voice", v => working.Voice = v);
            ally = BindChoice(root, "Row_Ally", i => working.AllyVolume = i);
            muteBg = BindChoice(root, "Row_MuteBg", i => working.MuteInBackground = i == 0);

            // 조작
            cursorSize = BindChoice(root, "Row_CursorSize", i => working.CursorSize = i);
            confine = BindChoice(root, "Row_Confine", i => working.ConfineCursor = i == 0);

            // 기타
            damageNumbers = BindChoice(root, "Row_DamageNumbers", i => working.DamageNumbers = i == 0);
            hitEffects = BindChoice(root, "Row_HitEffects", i => working.HitEffects = i == 0);
            Text versionText = FindComponent<Text>(root, "Row_Version/Value");
            if (versionText != null)
            {
                versionText.text = "v" + Application.version;
            }

            // 261116 - 조작 키
            for (int i = 0; i < BindActions.Length; i++)
            {
                int index = i;
                Button button = FindComponent<Button>(root, "Bind_" + BindActions[i] + "/Button");
                bindButtons[i] = button;
                if (button != null)
                {
                    bindLabels[i] = button.GetComponentInChildren<Text>(true);
                    button.onClick.AddListener(() => StartCapture(index));
                }
            }
            BindButton(root, "KeyReset", () =>
            {
                StopCapture(false);
                KeyBindings.ResetToDefaults();
                SetStatus("조작 키를 기본으로 되돌렸어요.");
            });

            BindButton(root, "BackButton", OnBackPressed);
            BindButton(root, "QuitButton", QuitGame);
            BindButton(root, "DefaultsButton", OnDefaults);
            saveButton = BindButton(root, "SaveButton", OnSave);
            statusText = FindComponent<Text>(root, "StatusText");
        }

        private void SelectTab(int index)
        {
            tabIndex = Mathf.Clamp(index, 0, TabNames.Length - 1);
            tabs.Show(tabIndex);
            for (int i = 0; i < pages.Length; i++)
            {
                if (pages[i] != null && pages[i].activeSelf != (i == tabIndex))
                {
                    pages[i].SetActive(i == tabIndex);
                }
            }
        }

        /// <summary>작업본 값을 화면에 그린다.</summary>
        private void Refresh()
        {
            refreshing = true;
            screenMode?.Show(working.ScreenMode);
            vsync?.Show(working.VSync ? 0 : 1);
            // 수직 동기화가 켜져 있으면 최대 프레임은 적용되지 않으므로(Unity 문서) 잠근다.
            frameCap?.Show(working.FrameCapIndex, !working.VSync);
            shake?.Show(working.Shake);
            if (resolutionText != null)
            {
                resolutionText.text = working.Width + " x " + working.Height;
            }
            SetVolume(master, working.Master);
            SetVolume(bgm, working.Bgm);
            SetVolume(sfx, working.Sfx);
            SetVolume(voice, working.Voice);
            ally?.Show(working.AllyVolume);
            muteBg?.Show(working.MuteInBackground ? 0 : 1);
            cursorSize?.Show(working.CursorSize);
            confine?.Show(working.ConfineCursor ? 0 : 1);
            damageNumbers?.Show(working.DamageNumbers ? 0 : 1);
            hitEffects?.Show(working.HitEffects ? 0 : 1);
            if (saveButton != null)
            {
                saveButton.interactable = !working.Equals(GameSettings.Saved);
            }
            refreshing = false;
        }

        private void OnChanged()
        {
            if (refreshing)
            {
                return;
            }
            GameSettings.Preview(working);
            Refresh();
            SetStatus(working.Equals(GameSettings.Saved) ? string.Empty : "저장하지 않은 변경 사항이 있어요.");
        }

        private void StepResolution(int step)
        {
            IReadOnlyList<Vector2Int> list = GameSettings.Resolutions;
            if (list.Count == 0)
            {
                return;
            }
            int current = -1;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].x == working.Width && list[i].y == working.Height)
                {
                    current = i;
                    break;
                }
            }
            int next = current < 0 ? list.Count - 1 : Mathf.Clamp(current + step, 0, list.Count - 1);
            working.Width = list[next].x;
            working.Height = list[next].y;
            OnChanged();
        }

        private void OnDefaults()
        {
            StopCapture(false);
            KeyBindings.ResetToDefaults();
            working = GameSettings.Defaults();
            OnChanged();
            SetStatus("기본값으로 바꿨어요. 저장을 눌러야 남아요.");
        }

        private void OnSave()
        {
            GameSettings.Save(working);
            Refresh();
            SetStatus("저장했어요.");
        }

        private void OnBackPressed()
        {
            if (UIFlowManager.Instance.IsTopOverlay(UIFlowManager.Settings))
            {
                UIFlowManager.Instance.CloseOverlay();
            }
            else
            {
                UIFlowManager.Instance.ShowScreen(UIFlowManager.Lobby);
            }
        }

        /// <summary>
        /// 261110 - 게임을 완전히 끈다. 세션은 SessionManager.OnApplicationQuit에서 LeaveAsync로 정리된다.
        /// 에디터에서는 Application.Quit이 무시되므로 플레이 모드를 끈다.
        /// </summary>
        private static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---------------- 조작 키

        private void StartCapture(int index)
        {
            StopCapture(false);
            capturing = index;
            KeyBindings.BeginCapture();
            RefreshKeys();
            SetStatus("바꿀 키를 누르세요. (ESC = 취소)");
        }

        private void StopCapture(bool escapeUsed)
        {
            if (capturing < 0)
            {
                return;
            }
            capturing = -1;
            KeyBindings.EndCapture(escapeUsed);
            RefreshKeys();
        }

        private void Update()
        {
            if (capturing < 0)
            {
                return;
            }
            UnityEngine.InputSystem.Key key = KeyBindings.PollPressedKey();
            if (key == UnityEngine.InputSystem.Key.None)
            {
                return;
            }
            if (key == UnityEngine.InputSystem.Key.Escape)
            {
                StopCapture(true);
                SetStatus("키 바꾸기를 취소했어요.");
                return;
            }
            if (KeyBindings.IsReserved(key))
            {
                SetStatus(KeyBindings.DisplayName(key) + " 키는 쓸 수 없어요. 다른 키를 누르세요.");
                return;
            }
            GameAction action = BindActions[capturing];
            KeyBindings.Set(action, key);
            StopCapture(false);
            SetStatus("키를 바꿨어요. (바로 저장)");
        }

        private void RefreshKeys()
        {
            for (int i = 0; i < BindActions.Length; i++)
            {
                if (bindLabels[i] != null)
                {
                    bindLabels[i].text = i == capturing ? "…" : KeyBindings.DisplayName(KeyBindings.Get(BindActions[i]));
                }
            }
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }
        }

        private static void SetVolume(VolumeRow row, float value)
        {
            if (row == null)
            {
                return;
            }
            if (row.Slider != null)
            {
                row.Slider.SetValueWithoutNotify(value);
            }
            if (row.Value != null)
            {
                row.Value.text = Mathf.RoundToInt(value * 100f) + "%";
            }
        }

        // ---------------- 바인딩

        private Choice BindChoice(Transform root, string rowName, Action<int> setter)
        {
            Transform row = FindDeep(root, rowName);
            if (row == null)
            {
                return null;
            }
            Choice choice = MakeChoice(FindButtons(row, "Opt_", null));
            for (int i = 0; i < choice.Buttons.Length; i++)
            {
                int index = i;
                choice.Buttons[i].onClick.AddListener(() =>
                {
                    setter(index);
                    OnChanged();
                });
            }
            return choice;
        }

        private VolumeRow BindVolume(Transform root, string rowName, Action<float> setter)
        {
            Transform row = FindDeep(root, rowName);
            if (row == null)
            {
                return null;
            }
            var result = new VolumeRow
            {
                Slider = FindComponent<Slider>(row, "Slider"),
                Value = FindComponent<Text>(row, "Value"),
            };
            if (result.Slider != null)
            {
                ApplySliderSkin(result.Slider);
                result.Slider.minValue = 0f;
                result.Slider.maxValue = 1f;
                result.Slider.onValueChanged.AddListener(v =>
                {
                    setter(v);
                    OnChanged();
                });
            }
            return result;
        }

        /// <summary>
        /// 261121 - 기획서 v1.2 "설정 슬라이더(재사용)": 막대 · 채움은 Bar의 xp 바, 손잡이는 황금 엽전. 막대(Track)에 xp 바 그림(9분할),
        /// 채움은 그 안쪽 칸만 청으로, 금테(Gold)는 끈다. 씬 배치는 그대로 두고 그림 · 색만 바꾼다.
        /// </summary>
        private static void ApplySliderSkin(Slider slider)
        {
            CheonMyeong.UI.ButtonSkinSet skin = CheonMyeong.UI.ButtonSkinSet.Load();
            if (skin == null || skin.sliderTrack == null)
            {
                return;
            }
            Transform track = FindDeep(slider.transform, "Track");
            if (track != null && track.TryGetComponent(out Image trackImage))
            {
                const float artScale = 1.5f; // 그림 1px = 1.5 UI px (HUD 영혼 바는 2배)
                RectTransform trackRect = trackImage.rectTransform;
                trackImage.sprite = skin.sliderTrack;
                trackImage.type = Image.Type.Sliced;
                trackImage.color = Color.white;
                trackImage.raycastTarget = false;
                trackRect.SetAsFirstSibling(); // 채움 · 손잡이 뒤에 그려지게
                trackImage.pixelsPerUnitMultiplier = 1f / (artScale * Mathf.Max(0.0001f, trackImage.pixelsPerUnit));
                // xp 바 칸(128×64)은 위아래가 투명이라, 막대가 보이게 높이를 그림 칸 높이에 맞춰 늘린다(가운데 기준).
                float wanted = skin.sliderTrack.rect.height * artScale;
                float grow = (wanted - trackRect.rect.height) * 0.5f;
                trackRect.offsetMin -= new Vector2(0f, grow);
                trackRect.offsetMax += new Vector2(0f, grow);
                // 채움 영역 = 막대 그림의 안쪽 칸(9분할 테두리 안).
                if (slider.fillRect != null && slider.fillRect.parent is RectTransform fillArea)
                {
                    Vector4 border = skin.sliderTrack.border * artScale;
                    fillArea.anchorMin = trackRect.anchorMin;
                    fillArea.anchorMax = trackRect.anchorMax;
                    fillArea.pivot = trackRect.pivot;
                    fillArea.offsetMin = trackRect.offsetMin + new Vector2(border.x, border.y);
                    fillArea.offsetMax = trackRect.offsetMax - new Vector2(border.z, border.w);
                }
            }
            if (slider.fillRect != null && slider.fillRect.TryGetComponent(out Image fillImage))
            {
                fillImage.sprite = null;
                fillImage.type = Image.Type.Simple;
                fillImage.color = SliderFill;
            }
            Transform gold = FindDeep(slider.transform, "Gold");
            if (gold != null)
            {
                gold.gameObject.SetActive(false);
            }
            if (slider.TryGetComponent(out Image background))
            {
                background.enabled = false; // 막대 그림이 테두리까지 그리므로 뒤 검은 판은 안 쓴다
            }
            if (slider.handleRect != null && skin.sliderKnob != null && slider.handleRect.TryGetComponent(out Image knob))
            {
                knob.sprite = skin.sliderKnob;
                knob.preserveAspect = true;
            }
        }

        private static Button BindButton(Transform root, string path, UnityEngine.Events.UnityAction action)
        {
            Button button = FindComponent<Button>(root, path);
            if (button != null)
            {
                button.onClick.AddListener(action);
            }
            return button;
        }

        /// <summary>names가 있으면 prefix+이름(빠진 칸은 null), 없으면 prefix+0,1,2.. 를 끊길 때까지.</summary>
        private static List<Button> FindButtons(Transform root, string prefix, string[] names)
        {
            var buttons = new List<Button>(8);
            int count = names != null ? names.Length : 16;
            for (int i = 0; i < count; i++)
            {
                Transform child = FindDeep(root, prefix + (names != null ? names[i] : i.ToString()));
                Button button = null;
                if (child == null || !child.TryGetComponent(out button))
                {
                    if (names == null)
                    {
                        break;
                    }
                    Debug.LogWarning("[CheonMyeong] 설정 창에서 " + prefix + names[i] + " 버튼을 못 찾았습니다.");
                }
                buttons.Add(button);
            }
            return buttons;
        }

        private static Choice MakeChoice(List<Button> buttons)
        {
            var choice = new Choice
            {
                Buttons = buttons.ToArray(),
                Images = new Image[buttons.Count],
                Labels = new Text[buttons.Count],
                Underlines = new GameObject[buttons.Count],
                NormalSprites = new Sprite[buttons.Count],
                SelectedSprites = new Sprite[buttons.Count],
            };
            CheonMyeong.UI.ButtonSkinSet skin = CheonMyeong.UI.ButtonSkinSet.Load();
            for (int i = 0; i < buttons.Count; i++)
            {
                if (buttons[i] == null)
                {
                    continue;
                }
                Transform t = buttons[i].transform;
                choice.Images[i] = buttons[i].targetGraphic as Image;
                choice.Labels[i] = FindComponent<Text>(t, "Label");
                Transform underline = FindDeep(t, "Underline");
                choice.Underlines[i] = underline != null ? underline.gameObject : null;
                if (underline != null && underline.TryGetComponent(out Graphic line))
                {
                    line.color = SelectedLine;
                }
                int kind = skin != null && choice.Images[i] != null ? skin.KindOf(choice.Images[i].sprite) : -1;
                if (kind >= 0)
                {
                    choice.NormalSprites[i] = skin.Normal(kind);
                    choice.SelectedSprites[i] = skin.Hover(kind);
                }
            }
            return choice;
        }

        private static T FindComponent<T>(Transform root, string path) where T : Component
        {
            Transform current = root;
            foreach (string part in path.Split('/'))
            {
                current = FindDeep(current, part);
                if (current == null)
                {
                    return null;
                }
            }
            return current.TryGetComponent(out T component) ? component : null;
        }

        private static Transform FindDeep(Transform root, string childName)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == childName)
                {
                    return child;
                }
                Transform deeper = FindDeep(child, childName);
                if (deeper != null)
                {
                    return deeper;
                }
            }
            return null;
        }
    }
}
