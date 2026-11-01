namespace CheonMyeong.Progression
{
    /// <summary>
    /// 골드(엽전) 드랍 1개 - LootPickup의 공통 로직(자석 끌림/수거)을 그대로 쓰고, 수거됐을
    /// 때 그 플레이어 개인의 PlayerProgress(소지금)에 더한다. REWARD 화면 와이어프레임 캡션
    /// ("엽전은 각자 따로 받음")에 따라 개인별로 분리된다 - PartyProgress(경험치/레벨, 공유)와는
    /// 반대.
    /// </summary>
    public class GoldLootPickup : LootPickup
    {
        protected override void ApplyReward(PlayerProgress progress, int rewardAmount)
        {
            if (progress != null)
            {
                progress.AddGold(rewardAmount);
            }
        }

        protected override void AbsorbAtWaveEnd(int rewardAmount)
        {
            AddPendingSplitGold(rewardAmount);
        }
    }
}
