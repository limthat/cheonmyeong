using CheonMyeong.Data;
using UnityEngine;
using UnityEngine.UI;

namespace CheonMyeong.UI
{
    /// <summary>
    /// 261114 추가 - 팀원 모집 화면 오른쪽 두루마리의 캐릭터 미리보기 무대.
    /// 참고: 롤 챔피언 선택의 스킬 미리보기(짧은 영상 반복 + 역할 태그 + 스킬 넘겨보기), 오버워치 영웅 선택(대표 동작 → 작은 대기).
    /// - 기본 공격 탭: 대기 → 공격 모션 → 맞는 순간 베기 이펙트 · 허수아비(원귀)가 붉게 번쩍 · 밀림 · 피해 숫자 → 쉬기, 를 계속 반복.
    ///   세 번째 공격마다 치명타(노란 숫자 + "!").
    /// - 패시브 탭: 로비 대기 모션 반복 + 패시브 설명.
    /// - 잠긴 캐릭터: 초상을 검은 실루엣으로 + "추후 공개".
    /// 그림 · 태그 · 설명은 전부 CharacterCatalog의 캐릭터 항목에서 읽는다(캐릭터가 늘어도 이 스크립트는 그대로).
    /// 화면이 켜져 있을 때만 돌고(Update), 시간은 unscaled라 일시정지와 무관하다. 매 프레임 할당 없음.
    /// </summary>
    public class CharacterPreviewStage : MonoBehaviour
    {
        private const float IdleBeforeAttack = 0.45f;
        private const float RestAfterAttack = 0.7f;
        private const float SlashFps = 20f;
        private const float HitFlashSeconds = 0.14f;
        private const float KnockbackPixels = 18f;
        private const float KnockbackSeconds = 0.25f;
        private const float DamageRiseSeconds = 0.65f;
        private const float DamageRisePixels = 46f;
        private const int CritEvery = 3;

        private static readonly Color HitTint = new Color(1f, 0.45f, 0.45f, 1f);
        private static readonly Color NormalDamageColor = new Color(1f, 0.97f, 0.9f, 1f);
        private static readonly Color CritDamageColor = new Color(1f, 0.82f, 0.25f, 1f);
        private static readonly Color SilhouetteColor = new Color(0f, 0f, 0f, 0.85f);
        private static readonly Color TabOnColor = Color.white;
        private static readonly Color TabOffColor = new Color(0.55f, 0.52f, 0.5f, 1f);

        [SerializeField] private CharacterCatalogSO catalog;
        [SerializeField] private Image characterImage;
        [SerializeField] private Image dummyImage;
        [SerializeField] private Image slashImage;
        [SerializeField] private Text damageText;
        [SerializeField] private Text tagsText;
        [SerializeField] private Text descText;
        [SerializeField] private Text lockedText;
        [SerializeField] private Button attackTab;
        [SerializeField] private Button passiveTab;
        [SerializeField] private Sprite[] dummyFrames;
        [SerializeField, Min(1f)] private float dummyFps = 8f;
        [Tooltip("261114 - 베기 이펙트 원본은 왼쪽을 향해 그려져 있다(인게임 PlayerAttackEffect도 오른쪽 조준 시 뒤집음). 미리보기는 오른쪽 허수아비를 베니 좌우를 뒤집는다.")]
        [SerializeField] private bool mirrorSlash = true;
        [Tooltip("261116 - 캐릭터 그림도 원본이 왼쪽을 본다. 오른쪽 허수아비(원귀)를 보도록 좌우를 뒤집는다.")]
        [SerializeField] private bool faceRight = true;

        private enum Tab
        {
            BasicAttack,
            Passive
        }

        private CharacterCatalogSO.CharacterEntry entry;
        private Tab tab = Tab.BasicAttack;
        private float cycleTime;
        private float dummyTime;
        private float slashStart = -1f;
        private float hitTime = -1f;
        private bool hitThisCycle;
        private int attackCount;
        private Vector2 dummyBase;
        private Vector2 damageBase;

        private bool initialized;
        private bool slashBaseCaptured;
        private Vector2 slashBase;

        private void Awake()
        {
            Init();
        }

        // 부모 화면(CharSkillTreeScreen)의 Awake가 먼저 Show를 부를 수 있어서, 처음 쓰일 때 한 번만 준비한다.
        private void Init()
        {
            if (initialized)
            {
                return;
            }
            initialized = true;
            if (dummyImage != null)
            {
                dummyBase = dummyImage.rectTransform.anchoredPosition;
            }
            if (damageText != null)
            {
                damageBase = damageText.rectTransform.anchoredPosition;
            }
            if (slashImage != null)
            {
                Vector3 s = slashImage.rectTransform.localScale;
                slashImage.rectTransform.localScale = new Vector3(mirrorSlash ? -Mathf.Abs(s.x) : Mathf.Abs(s.x), s.y, s.z);
            }
            if (characterImage != null)
            {
                Vector3 c = characterImage.rectTransform.localScale;
                characterImage.rectTransform.localScale = new Vector3(faceRight ? -Mathf.Abs(c.x) : Mathf.Abs(c.x), c.y, c.z);
            }
            if (attackTab != null)
            {
                attackTab.onClick.AddListener(() => SetTab(Tab.BasicAttack));
            }
            if (passiveTab != null)
            {
                passiveTab.onClick.AddListener(() => SetTab(Tab.Passive));
            }
        }

        /// <summary>CharSkillTreeScreen이 캐릭터를 고를 때 부른다(index = 캐릭터 목록 순서).</summary>
        public void Show(int index)
        {
            Init();
            entry = catalog != null && index >= 0 && index < catalog.characters.Length ? catalog.characters[index] : null;
            SetTab(Tab.BasicAttack);
        }

        private bool IsPlayable => entry != null && entry.previewAttackFrames != null && entry.previewAttackFrames.Length > 0;

        private void SetTab(Tab value)
        {
            tab = value;
            cycleTime = 0f;
            slashStart = -1f;
            hitTime = -1f;
            hitThisCycle = false;
            attackCount = 0;

            bool playable = IsPlayable;
            if (attackTab != null)
            {
                attackTab.interactable = playable;
                attackTab.targetGraphic.color = tab == Tab.BasicAttack ? TabOnColor : TabOffColor;
            }
            if (passiveTab != null)
            {
                passiveTab.interactable = playable;
                passiveTab.targetGraphic.color = tab == Tab.Passive ? TabOnColor : TabOffColor;
            }
            if (tagsText != null)
            {
                tagsText.text = entry != null && playable ? entry.roleTags : string.Empty;
            }
            if (descText != null)
            {
                descText.text = !playable ? "추후 개발 예정 - 상세 미정"
                    : tab == Tab.BasicAttack ? entry.basicAttackDescription
                    : entry.passiveDescription;
            }
            if (lockedText != null)
            {
                lockedText.gameObject.SetActive(!playable);
            }
            if (characterImage != null)
            {
                characterImage.color = playable ? Color.white : SilhouetteColor;
                characterImage.sprite = playable ? FirstFrame() : entry != null ? entry.portrait : null;
                characterImage.enabled = characterImage.sprite != null;
            }
            bool showDummy = playable && tab == Tab.BasicAttack;
            if (dummyImage != null)
            {
                dummyImage.gameObject.SetActive(showDummy);
                dummyImage.color = Color.white;
                dummyImage.rectTransform.anchoredPosition = dummyBase;
            }
            if (slashImage != null)
            {
                slashImage.enabled = false;
                slashImage.rectTransform.localRotation = Quaternion.identity;
                if (!slashBaseCaptured)
                {
                    slashBaseCaptured = true;
                    slashBase = slashImage.rectTransform.anchoredPosition;
                }
                slashImage.rectTransform.anchoredPosition = slashBase;
            }
            if (damageText != null)
            {
                damageText.gameObject.SetActive(false);
            }
        }

        private Sprite FirstFrame()
        {
            if (tab == Tab.Passive && entry.lobbyIdleFrames != null && entry.lobbyIdleFrames.Length > 0)
            {
                return entry.lobbyIdleFrames[0];
            }
            return entry.previewAttackFrames[0];
        }

        private void Update()
        {
            if (!IsPlayable || characterImage == null)
            {
                return;
            }
            float dt = Time.unscaledDeltaTime;
            cycleTime += dt;
            dummyTime += dt;

            if (tab == Tab.Passive)
            {
                Sprite[] idle = entry.lobbyIdleFrames;
                if (idle != null && idle.Length > 0)
                {
                    characterImage.sprite = idle[(int)(cycleTime * entry.lobbyIdleFps) % idle.Length];
                }
                return;
            }

            UpdateDummyIdle();

            Sprite[] frames = entry.previewAttackFrames;
            float attackLength = frames.Length / entry.previewAttackFps;
            float t = cycleTime - IdleBeforeAttack;
            if (t < 0f)
            {
                characterImage.sprite = frames[0];
            }
            else if (t < attackLength)
            {
                int index = Mathf.Min(frames.Length - 1, (int)(t * entry.previewAttackFps));
                characterImage.sprite = frames[index];
                // 261114 - 기획서: 이펙트는 타격 프레임에 띄운다. 원거리는 발사 프레임에 투사체가 날아가 닿을 때 맞는다.
                if (slashStart < 0f && index >= entry.previewHitFrame)
                {
                    slashStart = cycleTime;
                    if (!IsRanged)
                    {
                        hitThisCycle = true;
                        Hit();
                    }
                }
            }
            else
            {
                characterImage.sprite = frames[frames.Length - 1];
                if (t >= attackLength + RestAfterAttack)
                {
                    cycleTime = 0f;
                    slashStart = -1f;
                    hitThisCycle = false;
                }
            }

            if (IsRanged)
            {
                UpdateProjectile();
            }
            else
            {
                UpdateSlash();
            }
            UpdateHitReaction();
        }

        private const float ProjectileTravelSeconds = 0.2f;

        private bool IsRanged => entry != null && entry.projectileSprite != null
                                 && (entry.previewSlashFrames == null || entry.previewSlashFrames.Length == 0);

        /// <summary>261114 - 원거리 미리보기: 발사 프레임부터 투사체가 캐릭터 → 허수아비로 날아가 닿으면 맞는다.</summary>
        private void UpdateProjectile()
        {
            if (slashImage == null)
            {
                return;
            }
            if (slashStart < 0f || hitThisCycle)
            {
                if (slashImage.enabled)
                {
                    slashImage.enabled = false;
                }
                return;
            }
            float u = Mathf.Clamp01((cycleTime - slashStart) / ProjectileTravelSeconds);
            RectTransform rt = slashImage.rectTransform;
            Vector2 from = characterImage.rectTransform.anchoredPosition;
            Vector2 to = dummyImage != null ? dummyBase : from + new Vector2(200f, 0f);
            rt.anchoredPosition = Vector2.Lerp(from, to, u);
            CharacterStatsSO stats = entry.stats;
            float spin = stats != null ? stats.projectileSpinDegreesPerSecond : 0f;
            rt.localRotation = Quaternion.Euler(0f, 0f, spin != 0f ? -spin * (cycleTime - slashStart) : 0f);
            if (!slashImage.enabled)
            {
                slashImage.sprite = entry.projectileSprite;
                slashImage.enabled = true;
            }
            if (u >= 1f)
            {
                hitThisCycle = true;
                slashImage.enabled = false;
                Hit();
            }
        }

        private void UpdateDummyIdle()
        {
            if (dummyImage != null && dummyFrames != null && dummyFrames.Length > 0)
            {
                dummyImage.sprite = dummyFrames[(int)(dummyTime * dummyFps) % dummyFrames.Length];
            }
        }

        private void Hit()
        {
            attackCount++;
            hitTime = 0f;
            if (damageText == null)
            {
                return;
            }
            bool crit = attackCount % CritEvery == 0;
            float power = entry.stats != null ? entry.stats.attackPower : 20f;
            float multiplier = crit && entry.stats != null ? entry.stats.critMultiplier : crit ? 2f : 1f;
            int damage = Mathf.RoundToInt(power * multiplier);
            damageText.text = crit ? damage + "!" : damage.ToString();
            damageText.color = crit ? CritDamageColor : NormalDamageColor;
            damageText.gameObject.SetActive(true);
        }

        private void UpdateSlash()
        {
            if (slashImage == null)
            {
                return;
            }
            Sprite[] slash = entry.previewSlashFrames;
            if (slashStart < 0f || slash == null || slash.Length == 0)
            {
                if (slashImage.enabled)
                {
                    slashImage.enabled = false;
                }
                return;
            }
            int index = (int)((cycleTime - slashStart) * SlashFps);
            if (index >= slash.Length)
            {
                slashImage.enabled = false;
                return;
            }
            slashImage.enabled = true;
            slashImage.sprite = slash[index];
        }

        private void UpdateHitReaction()
        {
            if (hitTime < 0f)
            {
                return;
            }
            hitTime += Time.unscaledDeltaTime;
            if (dummyImage != null)
            {
                dummyImage.color = hitTime < HitFlashSeconds ? HitTint : Color.white;
                float k = Mathf.Clamp01(hitTime / KnockbackSeconds);
                // 바로 밀렸다가 천천히 제자리로(ease-out).
                float push = KnockbackPixels * (1f - k) * (1f - k);
                dummyImage.rectTransform.anchoredPosition = dummyBase + new Vector2(push, 0f);
            }
            if (damageText != null && damageText.gameObject.activeSelf)
            {
                float u = Mathf.Clamp01(hitTime / DamageRiseSeconds);
                float rise = DamageRisePixels * (1f - (1f - u) * (1f - u));
                damageText.rectTransform.anchoredPosition = damageBase + new Vector2(0f, rise);
                float pop = u < 0.15f ? Mathf.Lerp(1.4f, 1f, u / 0.15f) : 1f;
                damageText.rectTransform.localScale = new Vector3(pop, pop, 1f);
                Color c = damageText.color;
                c.a = u < 0.6f ? 1f : 1f - (u - 0.6f) / 0.4f;
                damageText.color = c;
                if (u >= 1f)
                {
                    damageText.gameObject.SetActive(false);
                }
            }
            if (hitTime >= Mathf.Max(DamageRiseSeconds, KnockbackSeconds))
            {
                hitTime = -1f;
            }
        }
    }
}
