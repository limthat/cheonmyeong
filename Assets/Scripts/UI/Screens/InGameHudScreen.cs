using CheonMyeong.Player;
using CheonMyeong.Progression;
using CheonMyeong.Waves;
using UnityEngine;
using UnityEngine.UI;

namespace CheonMyeong.UI.Screens
{
    /// <summary>
    /// INGAME HUD - P1/P2 체력, 웨이브 타이머, 레벨, 원한 게이지, 엽전, 증강 슬롯.
    ///
    /// 실제로 지금 구현 가능한 것: P1/P2 체력 표시(PlayerHealth.ActivePlayers가 이미
    /// 있어서 그대로 읽으면 됨 - 사망 중이면 "쓰러짐 · 다음 웨이브에 부활" 문구로
    /// PlayerHealth.cs의 확정된 부활 메커닉을 그대로 반영). 체력을 숫자 텍스트만이
    /// 아니라 와이어프레임 PDF(WireFrame.pdf) 원본의 "체력 바" 모습대로 Image.Filled
    /// 바로도 같이 그린다(UICanvasBuilder가 p1HealthFillImage/p2HealthFillImage를
    /// 연결해줌 - 에디터에서 비워두면 null 체크로 안전하게 생략).
    ///
    /// 웨이브/레벨/원한/엽전은 그 시스템 자체가 아직 없어서(웨이브 매니저, 경험치,
    /// 재화 전부 미구현/미정) 지금 당장은 보여줄 실제 값이 없다 - 그렇다고 문자열을
    /// 아무 데서나 직접 틀어막지 않고, 실제 시스템이 붙을 자리를 SetWaveStatus/
    /// SetLevel/SetGrudge/SetGold 공개 메서드로 미리 정의해둔다(261013, 예섭 지적
    /// 반영 - "수치는 보통 변수랑 연동하지 하드코딩 텍스트로 안 둔다"). 나중에 웨이브
    /// 매니저나 재화 매니저가 생기면 이 HUD 내부를 직접 건드릴 필요 없이
    /// Instance.SetXxx(...)만 호출하면 된다(PlayerCameraRig/GameplaySceneLoader와
    /// 같은 씬 전역 싱글턴 관례).
    ///
    /// 261027 수정 - "아직 수치가 없을때도 미구현 말고 0 같은 수치로 표현해줘"(예섭 요청).
    /// OnEnable 기본값을 "(시스템 미구현)" 같은 안내 문자열 대신, 실제 SetXxx가 쓰는 것과
    /// 똑같은 포맷의 0 값으로 바꿨다(레벨은 0부터 - 기획서 "0→30레벨" 기준과 일치). 어떤
    /// 수치를 지어낸 게 아니라 "아직 아무도 안 쌓인 시작값"이라는 점은 그대로이고, 캐시
    /// (cachedLevel 등)는 여전히 UnsetCacheValue로 두므로 실제 시스템이 처음 SetXxx를
    /// 호출하는 순간엔(우연히 0이 들어와도) 다시 그려진다.
    /// </summary>
    public class InGameHudScreen : MonoBehaviour
    {
        public static InGameHudScreen Instance { get; private set; }

        [SerializeField] private Text p1HealthText;
        [SerializeField] private Text p2HealthText;
        [SerializeField] private Image p1HealthFillImage;
        [SerializeField] private Image p2HealthFillImage;
        [SerializeField] private Text waveTimerText;
        [SerializeField] private Text levelText;
        [SerializeField] private Image expFillImage;
        [SerializeField] private Text grudgeText;
        [SerializeField] private Text goldText;

        /// <summary>
        /// 261027 추가 - 원한 4단계 게이지 구간 Image들, 반드시 [맑음, 짙음, 한, 폭주] 순서
        /// (UICanvasBuilder.BuildInGameHudScreen의 Segment_Clear/Deep/Han/Rampage 순서와
        /// 일치해야 한다 - PartyProgress.GrudgeStage의 0~3 인덱스가 이 배열 인덱스를 그대로
        /// 가리킨다). 지금 단계만 원래 색 그대로 두고, 나머지는 어둡게 눌러서(알파 낮춤)
        /// "지금 어디인지" 한눈에 보이게 한다.
        /// </summary>
        [SerializeField] private Image[] grudgeSegmentImages;

        private static readonly string[] GrudgeStageNames = { "맑음", "짙음", "한", "폭주" };
        private const float GrudgeInactiveAlpha = 0.35f;
        private Color[] grudgeSegmentFullColors;

        // 체력은 전투 중 거의 매 프레임 바뀌지만, 안 바뀌는 프레임까지 매번 문자열 보간을
        // 새로 만들면 불필요한 GC 할당이 쌓인다(실무에서 HUD 최적화할 때 흔히 쓰는 방식) -
        // 슬롯별로 마지막에 실제로 그린 값을 기억해뒀다가 값이 달라진 프레임에만 다시 만든다.
        // 아래 웨이브/레벨/원한/엽전 캐시도 미래에 매 프레임 호출될 수 있어 같은 방식을 쓴다.
        private readonly HealthSlotCache p1Cache = new HealthSlotCache();
        private readonly HealthSlotCache p2Cache = new HealthSlotCache();

        private const int UnsetCacheValue = int.MinValue;
        private int cachedWaveCurrent = UnsetCacheValue;
        private int cachedWaveTotal = UnsetCacheValue;
        private int cachedWaveRemainingSeconds = UnsetCacheValue;
        private int cachedLevel = UnsetCacheValue;
        private int cachedGrudge = UnsetCacheValue;
        private int cachedGold = UnsetCacheValue;

        // 경험치 바는 정수가 아니라 0~1 비율 캐시 - UnsetCacheValue(int.MinValue) 같은
        // "확실히 다른 값" 역할로 음수를 써서 최초 1회는 반드시 다시 그리게 한다.
        private const float UnsetExpRatio = -1f;
        private float cachedExpRatio = UnsetExpRatio;

        private class HealthSlotCache
        {
            public bool HasPlayer;
            public bool IsDead;
            public float Health = float.MinValue;
            public float MaxHealth = float.MinValue;
        }

        private void Awake()
        {
            // 씬 재로드 등으로 중복 생성될 경우 기존 인스턴스를 유지 - PlayerCameraRig와 같은 관례.
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[CheonMyeong] InGameHudScreen이 씬에 두 개 이상 있습니다 - 하나만 남기고 제거하세요.");
            }
            Instance = this;

            AutoBindMissingReferences();

            // 261027 추가 - UICanvasBuilder가 칠해둔 4단계 원래 색을 그대로 기억해둔다
            // (Segment_Clear/Deep/Han/Rampage마다 색이 다 다르므로, 하드코딩하지 않고
            // 지금 Image.color를 "원래 색"으로 캡처해서 나중에 그 색 그대로 복원한다).
            if (grudgeSegmentImages != null)
            {
                grudgeSegmentFullColors = new Color[grudgeSegmentImages.Length];
                for (int i = 0; i < grudgeSegmentImages.Length; i++)
                {
                    if (grudgeSegmentImages[i] != null)
                    {
                        grudgeSegmentFullColors[i] = grudgeSegmentImages[i].color;
                    }
                }
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnEnable()
        {
            // 아직 아무 시스템도 SetXxx를 호출한 적 없을 때의 기본 표시 - 캐시를 미설정
            // 상태로 되돌려서, 나중에 실제 값으로 첫 SetXxx가 들어오면(우연히 이전 캐시와
            // 값이 같아도) 반드시 다시 그리도록 한다.
            cachedWaveCurrent = UnsetCacheValue;
            cachedWaveTotal = UnsetCacheValue;
            cachedWaveRemainingSeconds = UnsetCacheValue;
            cachedLevel = UnsetCacheValue;
            cachedGrudge = UnsetCacheValue;
            cachedGold = UnsetCacheValue;
            cachedExpRatio = UnsetExpRatio;

            if (waveTimerText != null)
            {
                // SetWaveStatus와 같은 포맷("WAVE 현재/전체 mm:ss") - 아직 웨이브 매니저가
                // 없으니 0/0 00:00으로 시작.
                waveTimerText.text = "WAVE 0/0 00:00";
            }
            if (levelText != null)
            {
                // SetLevel과 같은 포맷 - 레벨은 0부터(기획서 "0→30레벨" 기준).
                levelText.text = "Lv.0 · 영혼 공유";
            }
            if (expFillImage != null)
            {
                // PartyProgress가 아직 SetExpProgress를 호출하기 전엔 빈 바로 둔다 -
                // 가득 찬(1.0) 기본값으로 두면 "이미 레벨업 직전"처럼 오해를 줄 수 있어서.
                expFillImage.fillAmount = 0f;
            }
            if (grudgeText != null)
            {
                // SetGrudge(0, 0)과 같은 결과 - 시작은 원한 0, 맑음.
                grudgeText.text = $"원한 0 · {GrudgeStageNames[0]}";
            }
            if (goldText != null)
            {
                // SetGold와 같은 포맷.
                goldText.text = "엽전 0";
            }
            RefreshGrudgeSegments(0);

            PullCurrentState();
        }

        /// <summary>
        /// 261027 추가 - "HUD 또 연동 끊킨 듯"(예섭) 원인 대응. 저장된 SampleScene을 열어보니 이 컴포넌트의
        /// 참조 필드(p1HealthText ~ grudgeSegmentImages)가 전부 비어 있었다(fileID: 0) - 그래서 OnEnable/
        /// Update/SetXxx가 전부 null 체크에 걸려 아무것도 안 그리고 UICanvasBuilder의 기본 문구
        /// ("WAVE --/--", "Lv.--", "원한 -- (보류)")가 그대로 남았다. 왜 비었는지(빌드 메뉴는 정상 연결함)
        /// 저장 파일만으로는 확정하지 못해서, 비어 있는 참조는 UICanvasBuilder가 붙이는 자식 이름으로
        /// 직접 찾아 채우는 안전장치를 둔다. 이미 연결돼 있으면 아무것도 바꾸지 않는다.
        /// </summary>
        private void AutoBindMissingReferences()
        {
            if (p1HealthText == null) p1HealthText = FindChildComponent<Text>(transform, "P1HealthText");
            if (p2HealthText == null) p2HealthText = FindChildComponent<Text>(transform, "P2HealthText");
            if (p1HealthFillImage == null) p1HealthFillImage = FindFillImage("P1HealthBar");
            if (p2HealthFillImage == null) p2HealthFillImage = FindFillImage("P2HealthBar");
            if (waveTimerText == null) waveTimerText = FindChildComponent<Text>(transform, "WaveTimerText");
            if (levelText == null) levelText = FindChildComponent<Text>(transform, "LevelText");
            if (expFillImage == null) expFillImage = FindFillImage("ExpBar");
            if (grudgeText == null) grudgeText = FindChildComponent<Text>(transform, "GrudgeText");
            if (goldText == null) goldText = FindChildComponent<Text>(transform, "GoldText");

            if (grudgeSegmentImages == null || grudgeSegmentImages.Length == 0)
            {
                grudgeSegmentImages = new[]
                {
                    FindChildComponent<Image>(transform, "Segment_Clear"),
                    FindChildComponent<Image>(transform, "Segment_Deep"),
                    FindChildComponent<Image>(transform, "Segment_Han"),
                    FindChildComponent<Image>(transform, "Segment_Rampage")
                };
            }
        }

        /// <summary>CreateRoundedFillBar 구조(바 이름/Mask/Fill)에서 실제로 채워지는 Fill Image를 찾는다.</summary>
        private Image FindFillImage(string barName)
        {
            Transform bar = FindChildRecursive(transform, barName);
            return bar != null ? FindChildComponent<Image>(bar, "Fill") : null;
        }

        private static T FindChildComponent<T>(Transform root, string childName) where T : Component
        {
            Transform child = FindChildRecursive(root, childName);
            return child != null ? child.GetComponent<T>() : null;
        }

        private static Transform FindChildRecursive(Transform root, string childName)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == childName)
                {
                    return child;
                }
                Transform found = FindChildRecursive(child, childName);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        /// <summary>
        /// 261027 추가 - HUD 패널은 처음 화면에 뜰 때 Awake/OnEnable이 돈다. 그 전에 레벨/웨이브/원한/엽전
        /// 값이 이미 정해져 있으면(PartyProgress/WaveManager가 HUD보다 먼저 스폰됨) 그때 보낸 갱신은
        /// Instance가 없어서 버려졌다 - 켜질 때 현재 값을 직접 당겨온다.
        /// </summary>
        private void PullCurrentState()
        {
            if (PartyProgress.Instance != null && PartyProgress.Instance.IsSpawned)
            {
                PartyProgress.Instance.RefreshHudNow();
            }
            if (WaveManager.Instance != null && WaveManager.Instance.IsSpawned)
            {
                WaveManager.Instance.RefreshHudNow();
            }

            for (int i = 0; i < PlayerHealth.ActivePlayers.Count; i++)
            {
                PlayerHealth player = PlayerHealth.ActivePlayers[i];
                if (player != null && player.IsOwner && player.TryGetComponent(out PlayerProgress progress))
                {
                    SetGold(progress.Gold);
                    break;
                }
            }
        }

        private void Update()
        {
            UpdateHealthSlot(p1HealthText, p1HealthFillImage, 0, p1Cache);
            UpdateHealthSlot(p2HealthText, p2HealthFillImage, 1, p2Cache);
        }

        /// <summary>웨이브 매니저가 생기면 매 프레임 호출할 자리 - 초 단위로 반올림해서 캐시
        /// 비교하므로, 프레임마다 미세하게 흐르는 Time.deltaTime 때문에 매번 새로 그리지 않는다.</summary>
        public void SetWaveStatus(int currentWave, int totalWaves, float remainingSeconds)
        {
            if (waveTimerText == null)
            {
                return;
            }

            int roundedSeconds = Mathf.Max(0, Mathf.RoundToInt(remainingSeconds));
            if (cachedWaveCurrent == currentWave && cachedWaveTotal == totalWaves && cachedWaveRemainingSeconds == roundedSeconds)
            {
                return;
            }
            cachedWaveCurrent = currentWave;
            cachedWaveTotal = totalWaves;
            cachedWaveRemainingSeconds = roundedSeconds;

            int minutes = roundedSeconds / 60;
            int seconds = roundedSeconds % 60;
            // 261101 - 마지막 웨이브 뒤 억쇠 최종전(WaveManager가 currentWave = totalWaves + 1로 보낸다).
            waveTimerText.text = totalWaves > 0 && currentWave > totalWaves
                ? $"최종전 {minutes:00}:{seconds:00}"
                : $"WAVE {currentWave}/{totalWaves} {minutes:00}:{seconds:00}";
        }

        /// <summary>경험치/레벨 시스템이 생기면 호출할 자리.</summary>
        public void SetLevel(int level)
        {
            if (levelText == null || cachedLevel == level)
            {
                return;
            }
            cachedLevel = level;
            levelText.text = $"Lv.{level} · 영혼 공유";
        }

        /// <summary>
        /// 경험치 바 - PartyProgress.cs가 currentExp/다음 레벨 필요 경험치가 바뀔 때마다
        /// 호출한다(261013 추가, 예섭 피드백 "경험치 바 안 보임" 반영). requiredExp가 0
        /// 이하면(아직 아무도 호출 안 한 초기 상태 등) 0으로 나누기를 피하려고 그냥 무시한다.
        /// 레벨 디자인 수치(expPerLevelBase/Growth) 자체는 여전히 미정 플레이스홀더지만,
        /// "이미 들고 있는 값의 비율을 막대로 보여주는 것"이라 임의로 수치를 지어내는 것과는
        /// 다르다고 보고 추가했다.
        /// </summary>
        public void SetExpProgress(int currentExp, int requiredExp)
        {
            if (expFillImage == null || requiredExp <= 0)
            {
                return;
            }

            float ratio = Mathf.Clamp01((float)currentExp / requiredExp);
            if (Mathf.Approximately(cachedExpRatio, ratio))
            {
                return;
            }
            cachedExpRatio = ratio;
            expFillImage.fillAmount = ratio;
        }

        /// <summary>
        /// 261027 수정 - 기획서 5-3: 원한은 누적 수치(남은 원귀 1마리 = 1)이고, 수치 구간으로 4단계
        /// (맑음/짙음/한/폭주)가 정해진다. 수치와 단계를 같이 보여주고, 게이지 바에서는 현재 단계
        /// 구간만 원래 색으로 남기고 나머지는 어둡게 눌러서 하이라이트한다.
        /// </summary>
        public void SetGrudge(int grudgeValue, int stage)
        {
            RefreshGrudgeSegments(stage);

            if (grudgeText == null || cachedGrudge == grudgeValue)
            {
                return;
            }
            cachedGrudge = grudgeValue;

            string stageName = stage >= 0 && stage < GrudgeStageNames.Length ? GrudgeStageNames[stage] : "?";
            grudgeText.text = $"원한 {grudgeValue} · {stageName}";
        }

        /// <summary>
        /// grudgeSegmentImages 중 stage번째만 원래 색(Awake에서 캡처한 grudgeSegmentFullColors)
        /// 그대로 두고, 나머지는 알파를 낮춰서 "지금 단계"를 한눈에 보이게 한다. 배열을 안
        /// 채워뒀으면(에디터 연결 전) 그냥 조용히 생략 - 다른 Set 계열과 같은 null 안전 관례.
        /// </summary>
        private void RefreshGrudgeSegments(int stage)
        {
            if (grudgeSegmentImages == null || grudgeSegmentFullColors == null)
            {
                return;
            }

            for (int i = 0; i < grudgeSegmentImages.Length; i++)
            {
                if (grudgeSegmentImages[i] == null)
                {
                    continue;
                }

                Color fullColor = grudgeSegmentFullColors[i];
                grudgeSegmentImages[i].color = i == stage
                    ? fullColor
                    : new Color(fullColor.r, fullColor.g, fullColor.b, GrudgeInactiveAlpha);
            }
        }

        /// <summary>재화(엽전) 시스템이 생기면 호출할 자리.</summary>
        public void SetGold(int amount)
        {
            if (goldText == null || cachedGold == amount)
            {
                return;
            }
            cachedGold = amount;
            goldText.text = $"엽전 {amount}";
        }

        private static void UpdateHealthSlot(Text slot, Image fillImage, int playerIndex, HealthSlotCache cache)
        {
            if (slot == null)
            {
                return;
            }

            bool hasPlayer = playerIndex < PlayerHealth.ActivePlayers.Count && PlayerHealth.ActivePlayers[playerIndex] != null;
            if (!hasPlayer)
            {
                if (cache.HasPlayer) // 방금 전까지 있었는데 사라진 프레임에만 다시 그린다
                {
                    cache.HasPlayer = false;
                    slot.text = $"P{playerIndex + 1} --";
                    if (fillImage != null)
                    {
                        fillImage.fillAmount = 0f;
                    }
                }
                return;
            }

            PlayerHealth player = PlayerHealth.ActivePlayers[playerIndex];
            bool isDead = player.IsDead;
            float health = player.CurrentHealth;
            float maxHealth = player.MaxHealth;

            bool unchanged = cache.HasPlayer && cache.IsDead == isDead
                              && (isDead || (cache.Health == health && cache.MaxHealth == maxHealth));
            if (unchanged)
            {
                return;
            }

            cache.HasPlayer = true;
            cache.IsDead = isDead;
            cache.Health = health;
            cache.MaxHealth = maxHealth;

            slot.text = isDead
                ? $"P{playerIndex + 1} 쓰러짐 · 다음 웨이브에 부활"
                : $"P{playerIndex + 1} 체력 {health:0}/{maxHealth:0}";

            if (fillImage != null)
            {
                // 와이어프레임 PDF 기준 - 쓰러진 플레이어는 체력 바 자체를 안 그린다(0으로 비움).
                fillImage.fillAmount = isDead || maxHealth <= 0f ? 0f : Mathf.Clamp01(health / maxHealth);
            }
        }
    }
}
