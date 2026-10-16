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
        protected override void ApplyReward(PlayerProgress progress, int rewardAmount)
        {
            if (PartyProgress.Instance != null)
            {
                PartyProgress.Instance.AddExp(rewardAmount);
            }
        }
    }
}
