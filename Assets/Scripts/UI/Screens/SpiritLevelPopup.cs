using CheonMyeong.Audio;
using CheonMyeong.Data;
using CheonMyeong.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace CheonMyeong.UI.Screens
{
    /// <summary>
    /// 261116 - 영력(캐릭터 레벨) 수련 창. 영웅 화면의 '레벨업' 버튼으로 연다(클래시 로얄 업그레이드 창처럼 가운데에 뜬다).
    /// - 지금 수치 → 수련 후 수치(체력 · 공격력 · 공격 속도), 비용(지금 레벨 × 10 엽전), 보유 엽전을 보여 준다.
    /// - '수련하기': 엽전이 충분하면 1레벨 올린다(CharacterProgressStore.TryLevelUp). 모자라면 버튼이 잠기고 모자란 만큼 알려 준다.
    /// - '취소': 창을 닫는다. 30레벨이면 '최대'로 잠긴다.
    /// 씬 오브젝트를 이름으로 찾는다: Portrait · Header · Row_Health/Row_Attack/Row_Speed(Current · Next) · Cost · Status · TrainButton · CancelButton.
    /// 열려 있는 동안만 엽전 변화를 구독한다. 매 프레임 할당 없음(갱신은 값이 바뀔 때만).
    /// </summary>
    public class SpiritLevelPopup : MonoBehaviour
    {
        private static readonly Color Good = new Color(0.12f, 0.45f, 0.2f, 1f);
        private static readonly Color Bad = new Color(0.62f, 0.12f, 0.12f, 1f);
        private static readonly Color Muted = new Color(0.36f, 0.27f, 0.2f, 1f);
        private static readonly Color LabelOn = Color.white;
        private static readonly Color LabelOff = new Color(0.85f, 0.8f, 0.78f, 1f);

        private const float PopSeconds = 0.25f;

        private Image portrait;
        private Text header;
        private Text healthNow, healthNext, attackNow, attackNext, speedNow, speedNext;
        private Text cost;
        private Text status;
        private Button trainButton;
        private Text trainLabel;
        private Button cancelButton;
        private bool bound;

        private CharacterCatalogSO.CharacterEntry entry;
        private float popTime = -1f;
        private string successMessage;

        public bool IsOpen => gameObject.activeSelf;

        public void Open(CharacterCatalogSO.CharacterEntry target)
        {
            entry = target;
            if (entry == null)
            {
                return;
            }
            Bind();
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            successMessage = null;
            Refresh();
            GameAudio.Play(SoundKeys.UiOpen);
        }

        public void Close()
        {
            gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            PlayerWallet.YeopjeonChanged += HandleWalletChanged;
        }

        private void OnDisable()
        {
            PlayerWallet.YeopjeonChanged -= HandleWalletChanged;
            popTime = -1f;
            if (header != null)
            {
                header.rectTransform.localScale = Vector3.one;
            }
        }

        private void HandleWalletChanged(int value) => Refresh();

        private void Bind()
        {
            if (bound)
            {
                return;
            }
            bound = true;
            Transform root = transform;
            portrait = Find<Image>(root, "Portrait");
            header = Find<Text>(root, "Header");
            healthNow = Find<Text>(root, "Row_Health/Current");
            healthNext = Find<Text>(root, "Row_Health/Next");
            attackNow = Find<Text>(root, "Row_Attack/Current");
            attackNext = Find<Text>(root, "Row_Attack/Next");
            speedNow = Find<Text>(root, "Row_Speed/Current");
            speedNext = Find<Text>(root, "Row_Speed/Next");
            cost = Find<Text>(root, "Cost");
            status = Find<Text>(root, "Status");
            trainButton = Find<Button>(root, "TrainButton");
            cancelButton = Find<Button>(root, "CancelButton");
            if (trainButton != null)
            {
                trainLabel = trainButton.GetComponentInChildren<Text>(true);
                trainButton.onClick.AddListener(OnTrain);
            }
            if (cancelButton != null)
            {
                cancelButton.onClick.AddListener(Close);
            }
        }

        private void Refresh()
        {
            if (entry == null || !bound)
            {
                return;
            }
            int level = CharacterProgressStore.Level(entry.characterId);
            bool max = level >= SpiritLevel.MaxLevel;
            int next = max ? level : level + 1;
            int price = SpiritLevel.Cost(level);
            int wallet = PlayerWallet.Yeopjeon;
            bool owned = CharacterProgressStore.IsUnlocked(entry);
            bool canAfford = wallet >= price;

            if (portrait != null)
            {
                portrait.sprite = entry.portrait;
                portrait.enabled = entry.portrait != null;
            }
            if (header != null)
            {
                header.text = max
                    ? entry.displayName + " · 영력 Lv." + level + " (최대)"
                    : entry.displayName + " · 영력 Lv." + level + "  →  Lv." + next;
            }

            CharacterStatsSO stats = entry.stats;
            if (stats != null)
            {
                SetPair(healthNow, healthNext, stats.maxHealth * SpiritLevel.HealthScale(level), stats.maxHealth * SpiritLevel.HealthScale(next), "0", max);
                SetPair(attackNow, attackNext, stats.attackPower * SpiritLevel.AttackScale(level, entry.characterId), stats.attackPower * SpiritLevel.AttackScale(next, entry.characterId), "0.#", max);
                SetPair(speedNow, speedNext, stats.attacksPerSecond * SpiritLevel.AttackSpeedScale(level, entry.characterId),
                    stats.attacksPerSecond * SpiritLevel.AttackSpeedScale(next, entry.characterId), "0.00", max, "회/초");
            }

            if (cost != null)
            {
                cost.text = max ? "더 수련할 수 없습니다" : "수련 비용  엽전 " + price.ToString("N0") + "   ·   보유 " + wallet.ToString("N0");
            }

            bool can = owned && !max && canAfford;
            if (trainButton != null)
            {
                trainButton.interactable = can;
            }
            if (trainLabel != null)
            {
                trainLabel.text = max ? "최대" : "수련하기";
                trainLabel.color = can ? LabelOn : LabelOff;
            }
            if (!owned)
            {
                SetStatus("해금한 캐릭터만 수련할 수 있습니다", Bad);
            }
            else if (!max && !canAfford)
            {
                SetStatus("엽전이 " + (price - wallet).ToString("N0") + " 모자랍니다", Bad);
            }
            else
            {
                SetStatus(successMessage ?? string.Empty, successMessage != null ? Good : Muted);
            }
        }

        private static void SetPair(Text now, Text after, float a, float b, string format, bool max, string unit = "")
        {
            if (now != null)
            {
                now.text = a.ToString(format) + unit;
            }
            if (after != null)
            {
                after.text = max ? "-" : b.ToString(format) + unit;
                after.color = Good;
            }
        }

        private void OnTrain()
        {
            if (entry == null)
            {
                return;
            }
            CharacterProgressStore.LevelUpResult result = CharacterProgressStore.TryLevelUp(entry, PlayerWallet.TrySpendYeopjeon);
            switch (result)
            {
                case CharacterProgressStore.LevelUpResult.Done:
                    GameAudio.Play(SoundKeys.OutgameUpgrade);
                    successMessage = "수련 완료! 영력 Lv." + CharacterProgressStore.Level(entry.characterId);
                    popTime = 0f;
                    break;
                case CharacterProgressStore.LevelUpResult.NotEnoughYeopjeon:
                    GameAudio.Play(SoundKeys.UiError);
                    break;
            }
            Refresh();
        }

        private void Update()
        {
            // 수련 직후 머리글이 잠깐 커졌다 돌아온다.
            if (popTime < 0f || header == null)
            {
                return;
            }
            popTime += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(popTime / PopSeconds);
            float scale = 1f + 0.15f * Mathf.Sin(u * Mathf.PI);
            header.rectTransform.localScale = new Vector3(scale, scale, 1f);
            if (u >= 1f)
            {
                popTime = -1f;
                header.rectTransform.localScale = Vector3.one;
            }
        }

        private void SetStatus(string message, Color color)
        {
            if (status != null)
            {
                status.text = message;
                status.color = color;
            }
        }

        private static T Find<T>(Transform root, string path) where T : Component
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
