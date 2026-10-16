using CheonMyeong.Player;
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
    /// 같은 씬 전역 싱글턴 관례). 지금은 그 호출자가 없으니 OnEnable이 "시스템
    /// 미구현" placeholder 텍스트로 기본값만 채워둔다 - 수치를 임의로 지어내는 게
    /// 아니라 "아직 아무도 호출 안 한 상태의 기본 표시"일 뿐이다.
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
                waveTimerText.text = "WAVE --/-- (웨이브 시스템 미구현)";
            }
            if (levelText != null)
            {
                levelText.text = "Lv.-- · 영혼 공유 (경험치 시스템 미구현)";
            }
            if (expFillImage != null)
            {
                // PartyProgress가 아직 SetExpProgress를 호출하기 전엔 빈 바로 둔다 -
                // 가득 찬(1.0) 기본값으로 두면 "이미 레벨업 직전"처럼 오해를 줄 수 있어서.
                expFillImage.fillAmount = 0f;
            }
            if (grudgeText != null)
            {
                grudgeText.text = "원한 -- (시스템 미정 - 보류)";
            }
            if (goldText != null)
            {
                goldText.text = "엽전 -- (재화 시스템 미구현)";
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
            waveTimerText.text = $"WAVE {currentWave}/{totalWaves} {minutes:00}:{seconds:00}";
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

        /// <summary>원한 게이지 수치/단계가 확정되면 호출할 자리 - 4단계 게이지 바(UICanvasBuilder가
        /// 그려둔 정적 색 구간)와 실제로 연동하는 건 그 시스템이 확정된 다음 할 일(지금은 수치
        /// 텍스트만 갱신, 게이지 하이라이트 로직은 없음 - 임의로 만들지 않는다는 규칙).</summary>
        public void SetGrudge(int grudgeValue)
        {
            if (grudgeText == null || cachedGrudge == grudgeValue)
            {
                return;
            }
            cachedGrudge = grudgeValue;
            grudgeText.text = $"원한 {grudgeValue}";
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
