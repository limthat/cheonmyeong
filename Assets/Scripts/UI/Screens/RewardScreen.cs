using CheonMyeong.Data;
using CheonMyeong.Networking;
using CheonMyeong.Player;
using CheonMyeong.Progression;
using CheonMyeong.Waves;
using UnityEngine;
using UnityEngine.UI;

namespace CheonMyeong.UI.Screens
{
    /// <summary>
    /// REWARD(결과) 화면 - "봉인 성공/실패", 플레이어별 처치 수·획득 엽전, "다시
    /// 하기"(같은 맵 MapStory부터 재도전) / "나가기"(Lobby로). 와이어프레임 캡션
    /// 기준 실패해도 주운 엽전은 보존되는 규칙이지만, 재화 시스템 자체가 아직
    /// 없어서(우선순위 2번) 수치 표시는 placeholder로만 둔다.
    ///
    /// 씬 분리(261013) 반영 - 둘 다 호스트 쪽에서 지금 로드돼 있는 Gameplay 씬을
    /// 먼저 내린다(적 풀/타이머 등을 수동으로 리셋할 필요 없이 씬 자체를 내렸다 올리는
    /// 걸로 깨끗하게 초기화). "다시 하기"는 언로드가 끝나는 순간(OnGameplaySceneUnloaded)
    /// 같은 씬을 다시 로드하고, 그 로드가 끝나면 LoadingScreen이 알아서 MapStory로 넘겨준다.
    /// </summary>
    public class RewardScreen : MonoBehaviour
    {
        [SerializeField] private Text resultTitleText;
        [SerializeField] private Button retryButton;
        [SerializeField] private Button exitButton;

        // 261027 추가 - 결과 화면 값 채우기(기획서 7: 봉인 성공·실패, 최종 원한, 각자 받은 엽전, 클리어 보상).
        // UICanvasBuilder가 만든 텍스트를 이름으로 찾는다(비어 있으면 Awake에서 자동 연결 - 재빌드 불필요).
        [SerializeField] private Text runSummaryText;
        [SerializeField] private Text player1StatsText;
        [SerializeField] private Text player2StatsText;

        private static readonly string[] GrudgeStageNames = { "맑음", "짙음", "한", "폭주" };

        // 값이 바뀐 프레임에만 문자열을 다시 만든다(HUD와 같은 캐싱 관례). 원한/엽전 같은 값은 결과 화면이
        // 열린 직후 몇 프레임 늦게 도착할 수 있어서(서로 다른 NetworkVariable) 열려 있는 동안 계속 비교한다.
        private int cachedSummaryHash = int.MinValue;
        private int cachedP1Hash = int.MinValue;
        private int cachedP2Hash = int.MinValue;

        private bool pendingRetryReload;

        private bool sceneEventsSubscribed;

        private void Awake()
        {
            // 261027 수정 - "다시 하기 누르면 창 안 넘어감"(예섭) 원인: 예전엔 OnEnable에서 구독하고
            // OnDisable에서 해제했는데, 다시 하기를 누르면 곧바로 로딩 화면으로 바뀌면서 이 화면이 꺼지고
            // (OnDisable → 구독 해제) 그 뒤에 도착하는 "언로드 완료" 이벤트를 못 받아서 재로드가 영영
            // 시작되지 않았다(로딩 화면에서 멈춤). 결과 화면이 꺼져 있어도 받아야 하는 이벤트라 이 컴포넌트가
            // 살아있는 동안(Awake~OnDestroy) 계속 구독한다. Awake는 이 화면이 처음 켜질 때(판이 끝날 때)
            // 돌기 때문에 그 시점엔 GameplaySceneLoader가 이미 있다.
            SubscribeSceneEvents();

            if (runSummaryText == null) runSummaryText = FindChildText("RunSummaryText");
            if (player1StatsText == null) player1StatsText = FindChildText("Player1StatsText");
            if (player2StatsText == null) player2StatsText = FindChildText("Player2StatsText");

            if (retryButton != null)
            {
                retryButton.onClick.AddListener(OnRetryPressed);
            }
            if (exitButton != null)
            {
                exitButton.onClick.AddListener(OnExitPressed);
            }
        }

        private void OnEnable()
        {
            SubscribeSceneEvents();

            if (resultTitleText != null && string.IsNullOrEmpty(resultTitleText.text))
            {
                resultTitleText.text = "봉인 성공 (결과 판정 로직 미구현)";
            }
        }

        private void OnDestroy()
        {
            if (sceneEventsSubscribed && GameplaySceneLoader.Instance != null)
            {
                GameplaySceneLoader.Instance.OnGameplaySceneUnloaded -= HandleGameplaySceneUnloaded;
                GameplaySceneLoader.Instance.OnGameplaySceneLoaded -= HandleGameplaySceneLoaded;
            }
            sceneEventsSubscribed = false;
        }

        private void SubscribeSceneEvents()
        {
            if (sceneEventsSubscribed || GameplaySceneLoader.Instance == null)
            {
                return;
            }
            GameplaySceneLoader.Instance.OnGameplaySceneUnloaded += HandleGameplaySceneUnloaded;
            GameplaySceneLoader.Instance.OnGameplaySceneLoaded += HandleGameplaySceneLoaded;
            sceneEventsSubscribed = true;
        }

        /// <summary>
        /// 261027 추가 - 참가자(클라이언트) 쪽: 호스트가 다시 하기를 누르면 참가자는 결과 화면에 머물러
        /// 있다가 새 Gameplay 씬이 로드된 순간 같이 다음 화면(스토리)으로 넘어간다. 호스트는 이미 로딩
        /// 화면(LoadingScreen이 처리)이라 이 화면이 꺼져 있어서 여기선 아무것도 안 한다.
        /// </summary>
        private void HandleGameplaySceneLoaded(string sceneName)
        {
            if (gameObject.activeInHierarchy && UIFlowManager.Instance != null)
            {
                UIFlowManager.Instance.ShowScreen(UIFlowManager.MapStory);
            }
        }

        private void OnRetryPressed()
        {
            pendingRetryReload = true;
            UIFlowManager.Instance.ShowScreen(UIFlowManager.Loading);
            UnloadCurrentGameplayScene();
        }

        private void OnExitPressed()
        {
            pendingRetryReload = false;
            UIFlowManager.Instance.ShowScreen(UIFlowManager.Lobby);
            // 261110 - 세션 · 네트워크까지 정리(안 하면 로비에서 방을 다시 못 만듦). 씬도 SessionManager가 내린다.
            if (SessionManager.Instance != null)
            {
                _ = SessionManager.Instance.LeaveAsync();
                return;
            }
            UnloadCurrentGameplayScene();
        }

        private static void UnloadCurrentGameplayScene()
        {
            string sceneName = CurrentRun.SelectedMap != null ? CurrentRun.SelectedMap.gameplaySceneName : null;
            if (!string.IsNullOrEmpty(sceneName))
            {
                GameplaySceneLoader.Instance?.HostUnloadGameplayScene(sceneName);
            }
        }

        private void HandleGameplaySceneUnloaded(string sceneName)
        {
            if (!pendingRetryReload)
            {
                return; // "나가기"로 내린 경우 - 다시 로드할 필요 없음
            }
            pendingRetryReload = false;

            string mapSceneName = CurrentRun.SelectedMap != null ? CurrentRun.SelectedMap.gameplaySceneName : null;
            if (!string.IsNullOrEmpty(mapSceneName))
            {
                GameplaySceneLoader.Instance?.HostLoadGameplayScene(mapSceneName);
            }
        }

        /// <summary>웨이브 시스템이 생기면 실제 성공/실패 결과로 호출할 자리.</summary>
        public void SetResult(bool success)
        {
            if (resultTitleText != null)
            {
                resultTitleText.text = success ? "봉인 성공" : "봉인 실패";
            }
            cachedSummaryHash = int.MinValue;
            cachedP1Hash = int.MinValue;
            cachedP2Hash = int.MinValue;
        }

        private void Update()
        {
            RefreshSummary();
            RefreshPlayer(player1StatsText, 0, ref cachedP1Hash);
            RefreshPlayer(player2StatsText, 1, ref cachedP2Hash);
        }

        private void RefreshSummary()
        {
            if (runSummaryText == null)
            {
                return;
            }

            WaveManager waves = WaveManager.Instance;
            PartyProgress party = PartyProgress.Instance;
            int wave = waves != null ? waves.CurrentWave : 0;
            int totalWaves = waves != null ? waves.TotalWaves : 0;
            int seconds = waves != null ? waves.FinalRunSeconds : 0;
            int reward = waves != null ? waves.ClearRewardPerPlayer : 0;
            bool cleared = waves != null && waves.Phase == WaveManager.PhaseCleared;
            int grudge = party != null ? party.Grudge : 0;
            int stage = party != null ? party.GrudgeStage : 0;

            int hash = Hash(wave, totalWaves, seconds, reward, cleared ? 1 : 0, grudge, stage);
            if (hash == cachedSummaryHash)
            {
                return;
            }
            cachedSummaryHash = hash;

            string mapName = CurrentRun.SelectedMap != null ? CurrentRun.SelectedMap.displayName : "";
            string stageName = stage >= 0 && stage < GrudgeStageNames.Length ? GrudgeStageNames[stage] : "?";
            string rewardText = cleared ? $"+{reward}(각자)" : "없음";
            string timeLabel = cleared ? "봉인 시간" : "버틴 시간";
            runSummaryText.text = $"{mapName} · {wave}/{totalWaves} 웨이브\n" +
                                  $"최종 원한: {grudge} ({stageName}) / 클리어 보상: {rewardText} / {timeLabel}: {seconds / 60:00}:{seconds % 60:00}";
        }

        private static void RefreshPlayer(Text slot, int index, ref int cachedHash)
        {
            if (slot == null)
            {
                return;
            }

            PlayerProgress progress = null;
            if (index < PlayerHealth.ActivePlayers.Count && PlayerHealth.ActivePlayers[index] != null)
            {
                PlayerHealth.ActivePlayers[index].TryGetComponent(out progress);
            }

            int kills = progress != null ? progress.RunKills : -1;
            int gold = progress != null ? progress.RunGold : -1;
            int hash = Hash(kills, gold);
            if (hash == cachedHash)
            {
                return;
            }
            cachedHash = hash;

            slot.text = progress != null
                ? $"PLAYER{index + 1}\n처치 수 {kills} / 획득 엽전 {gold}"
                : $"PLAYER{index + 1}\n(참가 안 함)";
        }

        // params 배열을 쓰면 매 프레임 배열 할당(GC)이 생겨서 고정 개수로 둔다.
        private static int Hash(int a, int b)
        {
            unchecked
            {
                return (17 * 31 + a) * 31 + b;
            }
        }

        private static int Hash(int a, int b, int c, int d, int e, int f, int g)
        {
            unchecked
            {
                return Hash(Hash(Hash(a, b), Hash(c, d)), Hash(Hash(e, f), g));
            }
        }

        private Text FindChildText(string childName)
        {
            Transform child = FindRecursive(transform, childName);
            return child != null ? child.GetComponent<Text>() : null;
        }

        private static Transform FindRecursive(Transform root, string childName)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == childName)
                {
                    return child;
                }
                Transform found = FindRecursive(child, childName);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }
    }
}
