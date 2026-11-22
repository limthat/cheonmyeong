using CheonMyeong.Data;
using CheonMyeong.Networking;
using CheonMyeong.Player;
using CheonMyeong.Progression;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace CheonMyeong.UI.Screens
{
    /// <summary>
    /// ESC(일시 메뉴) - "계속하기"/"설정"/"나가기(로비로)". 와이어프레임 캡션
    /// "온라인이라 게임은 멈추지 않음"을 그대로 따른다 - 그래서 이 스크립트는
    /// Time.timeScale을 절대 건드리지 않는다(UI만 덮어 보여주는 순수 오버레이).
    ///
    /// "나가기"(261110 수정)는 세션을 나가고 NetworkManager를 멈춘 뒤 Gameplay 씬을 내린다(SessionManager.LeaveAsync).
    /// </summary>
    public class EscMenuScreen : MonoBehaviour
    {
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button exitButton;
        [Tooltip("261122 - 오른쪽 '내 능력치' 칸 글자(지원 피드백: ESC로 스탯 보기). 비어 있으면 표시 안 함.")]
        [SerializeField] private Text statsText;

        private const float StatsRefreshSeconds = 0.25f;
        private float statsRefreshLeft;
        private readonly System.Text.StringBuilder statsBuilder = new System.Text.StringBuilder(256);

        private void OnEnable()
        {
            statsRefreshLeft = 0f;
            if (statsText != null)
            {
                GameFontSet.Apply(statsText, FontRole.Body, false);
            }
        }

        private void Update()
        {
            if (statsText == null)
            {
                return;
            }
            // 게임은 멈추지 않으니(온라인) 열려 있는 동안 0.25초마다 다시 쓴다 - 실제 시간 기준.
            statsRefreshLeft -= Time.unscaledDeltaTime;
            if (statsRefreshLeft > 0f)
            {
                return;
            }
            statsRefreshLeft = StatsRefreshSeconds;
            RefreshStats();
        }

        /// <summary>261122 - 내 캐릭터의 지금 능력치(영력 · 신통력 반영). 상황형 고유 패시브(조건부 공격력 등)는 빠진다.</summary>
        private void RefreshStats()
        {
            PlayerProgress local = PlayerProgress.Local;
            if (local == null)
            {
                statsText.text = "인게임에서 볼 수 있다.";
                return;
            }
            local.TryGetComponent(out PlayerHealth health);
            local.TryGetComponent(out PlayerAutoAttack attack);
            local.TryGetComponent(out PlayerMovement movement);
            local.TryGetComponent(out CharacterKit kit);
            AugmentModifiers mods = local.Modifiers;

            StringBuilder b = statsBuilder;
            b.Clear();
            if (health != null)
            {
                Row(b, "체력").Append(health.CurrentHealth.ToString("0")).Append(" / ").Append(health.MaxHealth.ToString("0"));
                float sacrificed = health.BarMaxHealth - health.MaxHealth;
                if (sacrificed > 0.5f)
                {
                    b.Append(" <color=#6b5a50>(-").Append(sacrificed.ToString("0")).Append(")</color>");
                }
                b.Append('\n');
            }
            if (attack != null)
            {
                Row(b, "공격력").Append(attack.DisplayAttackPower.ToString("0.#")).Append('\n');
                Row(b, "공격 속도").Append(attack.DisplayAttacksPerSecond.ToString("0.##")).Append("회/초\n");
                Row(b, "사거리").Append(attack.HitRadius.ToString("0.#")).Append("칸\n");
                Row(b, "치명타").Append((attack.DisplayCritChance * 100f).ToString("0")).Append("% · ×")
                    .Append(attack.DisplayCritMultiplier.ToString("0.##")).Append('\n');
            }
            if (movement != null)
            {
                Row(b, "이동 속도").Append((movement.DisplayMoveSpeed * 60f).ToString("0")).Append('\n'); // 기획서 단위(유닛/초 × 60)
            }
            if (kit != null)
            {
                Row(b, "호신부").Append(kit.ShieldCharges).Append(" / ").Append(kit.ShieldCapacity).Append("회\n");
            }
            if (mods.RegenPercent > 0f)
            {
                Row(b, "재생").Append((mods.RegenPercent * 100f).ToString("0.##")).Append("%/초\n");
            }
            if (mods.Lifesteal > 0f)
            {
                Row(b, "넋 흡수").Append((mods.Lifesteal * 100f).ToString("0.#")).Append("%\n");
            }
            if (health != null)
            {
                Row(b, "획득 범위").Append(health.MagnetRadius.ToString("0.#")).Append("칸");
            }
            statsText.text = b.ToString();
        }

        private static StringBuilder Row(StringBuilder b, string label)
        {
            return b.Append("<color=#9e1f1f><b>").Append(label).Append("</b></color>  ");
        }

        private void Awake()
        {
            if (resumeButton != null)
            {
                resumeButton.onClick.AddListener(OnResumePressed);
            }
            if (settingsButton != null)
            {
                settingsButton.onClick.AddListener(() => UIFlowManager.Instance.OpenOverlay(UIFlowManager.Settings));
            }
            if (exitButton != null)
            {
                exitButton.onClick.AddListener(OnExitPressed);
            }
        }

        /// <summary>261110 - 일시정지(투표) 중이면 서버에 풀어달라고 한다. 닫힘은 PartyProgress가 상태를 보고 처리.</summary>
        private static void OnResumePressed()
        {
            PartyProgress party = PartyProgress.Instance;
            if (party != null && party.IsSpawned && party.IsMenuPaused)
            {
                party.RequestMenuResumeRpc();
                return;
            }
            UIFlowManager.Instance.CloseOverlay(UIFlowManager.EscMenu);
        }

        private static void OnExitPressed()
        {
            PartyProgress party = PartyProgress.Instance;
            if (party != null && party.IsSpawned && party.IsMenuPaused)
            {
                party.RequestMenuResumeRpc();
            }

            // 261101 - 중간에 나가도 그때까지 주운 엽전은 받는다(예섭 확정, 기획서 "연결 끊김" 규칙과 같게).
            PlayerProgress.SettleLocalPlayerRunGold();

            // 261110 - 세션 · 네트워크까지 정리한다(안 하면 로비로 와서 방을 다시 못 만듦). Gameplay 씬도 여기서 내린다.
            // 호스트가 나가면 동료는 끊김 처리(SessionManager.HandleClientStopped)로 로비에 온다.
            LeaveSession();
            UIFlowManager.Instance.ShowScreen(UIFlowManager.Lobby);
        }

        private static void LeaveSession()
        {
            if (SessionManager.Instance != null)
            {
                _ = SessionManager.Instance.LeaveAsync();
                return;
            }
            // SessionManager가 없는 테스트 씬 - 예전처럼 호스트가 Gameplay 씬만 내린다.
            string sceneName = CurrentRun.SelectedMap != null ? CurrentRun.SelectedMap.gameplaySceneName : null;
            if (!string.IsNullOrEmpty(sceneName))
            {
                GameplaySceneLoader.Instance?.HostUnloadGameplayScene(sceneName);
            }
        }
    }
}
