using UnityEngine;

namespace CheonMyeong.Progression
{
    /// <summary>
    /// 경험치 드랍 1개 - LootPickup의 공통 로직(자석 끌림/수거)을 그대로 쓰되, 수거한
    /// 플레이어 개인이 아니라 PartyProgress(씬 전역, 파티 공유)에 더한다. HUD 캡션
    /// "Lv.12 · 영혼 공유"(wireframe)에 따라 레벨/경험치는 두 플레이어가 공유하기 때문에,
    /// 누가 주웠는지는 상관없이 같은 곳에 쌓인다.
    /// </summary>
    public class ExpLootPickup : LootPickup
    {
        [Header("영혼 모양 (261027 - 기획서 5-10: 작은 영혼 1 / 중간 영혼 2 / 큰 영혼 5)")]
        [SerializeField] private SpriteRenderer spriteRenderer;

        [Tooltip("작은 → 큰 순서. Assets/Art/Goods/coin_xp-Sheet 윗줄(청록/보라/붉은 영혼)을\n" +
                 "'CheonMyeong/Setup/Setup Loot Visuals' 메뉴가 연결한다.")]
        [SerializeField] private Sprite[] tierSprites;

        [Tooltip("tierSprites와 같은 순서 - 보상 양이 이 값 이상이면 그 모양을 쓴다(오름차순).")]
        [SerializeField] private int[] tierMinAmounts = { 1, 2, 5 };

        protected override void ApplyVisual(int rewardAmount)
        {
            if (spriteRenderer == null || tierSprites == null || tierSprites.Length == 0)
            {
                return;
            }

            int tier = 0;
            if (tierMinAmounts != null)
            {
                int count = Mathf.Min(tierMinAmounts.Length, tierSprites.Length);
                for (int i = 0; i < count; i++)
                {
                    if (rewardAmount >= tierMinAmounts[i])
                    {
                        tier = i;
                    }
                }
            }

            if (tierSprites[tier] != null)
            {
                spriteRenderer.sprite = tierSprites[tier];
            }
        }

        protected override void ApplyReward(PlayerProgress progress, int rewardAmount)
        {
            if (PartyProgress.Instance != null)
            {
                PartyProgress.Instance.AddExp(rewardAmount);
            }
        }

        protected override void AbsorbAtWaveEnd(int rewardAmount)
        {
            if (PartyProgress.Instance != null)
            {
                PartyProgress.Instance.AddExp(rewardAmount);
            }
        }
    }
}
