using CheonMyeong.Player;
using CheonMyeong.Progression;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CheonMyeong.UI.Screens
{
    /// <summary>
    /// MAP_STORY(스토리 컷신) 화면 - 상단 일러스트 자리 + 하단 대사창.
    /// 261118 - 기획서 v0.9.9 12장 "Space 길게 눌러 건너뛰기 (둘 다 눌러야 넘어감)":
    /// Space를 HoldSeconds 동안 누르고 있으면(또는 버튼을 누르면) 서버에 알리고, 접속한 사람 전원이 누르고 있으면
    /// 모두 함께 인게임으로 넘어간다(PartyProgress.Story). 손을 떼면 취소. 네트워크가 없으면 혼자 바로 넘어간다.
    /// 스토리 보관함에서 다시 보기로 열었을 때는 혼자 보는 것이라 Space(짧게) · 버튼으로 바로 보관함으로 돌아간다.
    /// </summary>
    public class MapStoryScreen : MonoBehaviour
    {
        private const float HoldSeconds = 1f;
        private const string ContinueLabel = "건너뛰기 (Space)";
        private const string WaitingLabel = "기다리는 중…";
        private const string ReplayLabel = "돌아가기 (Space)";

        [SerializeField] private Text speakerNameText;
        [SerializeField] private Text dialogueText;
        [SerializeField] private Text skipHintText;
        [SerializeField] private Button continueButton;

        /// <summary>스토리 보관함이 다시 보기로 열 때 true로 두고 이 화면을 연다.</summary>
        public static bool ReplayFromStorage { get; set; }

        private Text continueLabel;
        private float holdTime;
        private bool keyHolding;
        private bool buttonHolding;
        private bool sentHold;
        private int seenSerial = -1;
        private int shownHolders = -1;
        private int shownPlayers = -1;
        private bool shownWaiting;

        private void Awake()
        {
            if (continueButton != null)
            {
                continueLabel = continueButton.GetComponentInChildren<Text>(true);
                continueButton.onClick.AddListener(HandleContinueClicked);
            }
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
            // 제1화 와이어프레임 예시 대사 - 실제 스토리 데이터 시스템이 들어오면 교체.
            if (speakerNameText != null)
            {
                speakerNameText.text = "억쇠";
            }
            if (dialogueText != null)
            {
                dialogueText.text = "한 맺힌 것들이 이렇게나 많은데… 오늘 밤도 배가 부르겠구나.";
            }
            holdTime = 0f;
            keyHolding = false;
            buttonHolding = false;
            sentHold = false;
            seenSerial = -1;
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

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;

            if (ReplayFromStorage)
            {
                if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
                {
                    UIFlowManager.Instance.ShowScreen(UIFlowManager.StoryStorage);
                }
                return;
            }

            if (keyboard != null && keyboard.spaceKey.isPressed)
            {
                holdTime += Time.unscaledDeltaTime;
                if (holdTime >= HoldSeconds)
                {
                    keyHolding = true;
                }
            }
            else
            {
                holdTime = 0f;
                keyHolding = false;
            }

            bool wantSkip = keyHolding || buttonHolding;
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
                        skipHintText.text = "다시 보기 · Space로 돌아가기";
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
                        ? "Space 길게 눌러 건너뛰기 " + holders + "/" + players + " · 둘 다 눌러야 넘어감"
                        : "Space 길게 눌러 건너뛰기";
                }
            }
            if (force || sentHold != shownWaiting)
            {
                shownWaiting = sentHold;
                if (continueLabel != null)
                {
                    continueLabel.text = sentHold ? WaitingLabel : ContinueLabel;
                }
            }
        }
    }
}
