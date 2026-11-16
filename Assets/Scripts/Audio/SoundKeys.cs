namespace CheonMyeong.Audio
{
    /// <summary>
    /// 261115 - 코드에서 부르는 소리 이름 목록. SoundLibrary 에셋이 이 목록으로 칸을 자동으로 만든다(빈 칸 = 아직 소리 없음, 조용히 넘어감).
    /// 새 소리 자리가 필요하면 여기 한 줄 추가 → 코드에서 GameAudio.Play(SoundKeys.X) → 라이브러리에 SoundCue 연결.
    /// 이름은 [사운드] 문서 목록과 맞춰 두면 찾기 쉽다.
    /// </summary>
    public static class SoundKeys
    {
        // 배경음
        public const string BgmTitle = "bgm.title";
        public const string BgmLobby = "bgm.lobby";
        public const string BgmWaveEarly = "bgm.wave_early";
        public const string BgmWaveLate = "bgm.wave_late";
        public const string BgmBoss = "bgm.boss";
        public const string BgmFinal = "bgm.final";
        public const string BgmVictory = "bgm.victory";
        public const string BgmDefeat = "bgm.defeat";

        // 웨이브 흐름
        public const string WaveStart = "wave.start";
        public const string WaveGong = "wave.gong";
        public const string FinalBattleStart = "wave.final_start";

        // 성장
        public const string LevelUp = "grow.level_up";
        public const string CardShow = "grow.card_show";
        public const string CardPick = "grow.card_pick";
        public const string CardBurn = "grow.card_burn";
        public const string CardReroll = "grow.card_reroll";
        public const string PickupSoul = "grow.pickup_soul";
        public const string PickupCoin = "grow.pickup_coin";

        // 전투
        public const string Hit = "combat.hit";
        public const string HitCrit = "combat.hit_crit";
        public const string EnemyDie = "combat.enemy_die";
        public const string PlayerHurt = "combat.player_hurt";
        public const string PlayerDown = "combat.player_down";
        public const string PlayerRevive = "combat.player_revive";

        // 보스(억쇠)
        public const string BossAppear = "boss.appear";
        public const string BossSlam = "boss.slam";
        public const string BossSummon = "boss.summon";
        public const string BossMental = "boss.mental";
        public const string BossGrab = "boss.grab";
        public const string BossDie = "boss.die";

        // UI
        public const string UiClick = "ui.click";
        public const string UiBack = "ui.back";
        public const string UiOpen = "ui.open";
        public const string UiError = "ui.error";
        public const string UiNotice = "ui.notice";

        /// <summary>라이브러리 칸 자동 생성용 전체 목록.</summary>
        public static readonly string[] All =
        {
            BgmTitle, BgmLobby, BgmWaveEarly, BgmWaveLate, BgmBoss, BgmFinal, BgmVictory, BgmDefeat,
            WaveStart, WaveGong, FinalBattleStart,
            LevelUp, CardShow, CardPick, CardBurn, CardReroll, PickupSoul, PickupCoin,
            Hit, HitCrit, EnemyDie, PlayerHurt, PlayerDown, PlayerRevive,
            BossAppear, BossSlam, BossSummon, BossMental, BossGrab, BossDie,
            UiClick, UiBack, UiOpen, UiError, UiNotice,
        };
    }
}
