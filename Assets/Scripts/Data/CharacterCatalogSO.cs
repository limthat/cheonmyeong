using System;
using UnityEngine;

namespace CheonMyeong.Data
{
    /// <summary>
    /// 261014 추가 - CHOICE_MAP 화면에 맵 선택과 "같은 화면"에서 캐릭터 선택도 같이 보여달라는
    /// 요청(예섭) 반영용. MapCatalogSO와 완전히 같은 패턴(하드코딩 금지 원칙 - 캐릭터
    /// 목록/해금 여부를 코드가 아니라 이 자산 하나로 관리) - 맵처럼 캐릭터가 늘어나도
    /// ChoiceMapScreen/UICanvasBuilder 코드는 안 건드리고 이 자산 행만 고치면 된다.
    ///
    /// 8명 로스터 자체는 이미 확정된 기획 사실이다(wireframe-reference_261013.md 섹션 3-1,
    /// 예섭 확인 - "기본(구현 대상) 캐릭터는 하영 1명뿐, 나머지 7명은 이름만 정해진 추후
    /// 개발 예정") - 임의로 지어낸 목록이 아니라 이미 문서로 확정된 것을 그대로 데이터화한
    /// 것뿐이고, 실제로 선택 가능한 건 하영(isUnlocked=true) 하나뿐이다.
    /// </summary>
    [CreateAssetMenu(fileName = "CharacterCatalog", menuName = "CheonMyeong/Character Catalog")]
    public class CharacterCatalogSO : ScriptableObject
    {
        [Serializable]
        public class CharacterEntry
        {
            public string characterId;
            public string displayName;

            [Tooltip("와이어프레임 캡션 - 예: \"사인검 · 근접 · 다수\". 잠긴 캐릭터는 보통 비워둔다.")]
            public string weaponCaption;

            public bool isUnlocked;

            [Header("로비 마당 (261027 추가)")]
            [Tooltip("이동 속도 등 - 로비 캐릭터가 인게임과 같은 속도로 걷도록.")]
            public CharacterStatsSO stats;

            public Sprite[] lobbyIdleFrames = Array.Empty<Sprite>();
            [Min(0.1f)] public float lobbyIdleFps = 8f;
            public Sprite[] lobbyWalkFrames = Array.Empty<Sprite>();
            [Min(0.1f)] public float lobbyWalkFps = 10f;

            [Tooltip("261114 - 얼굴 초상(Portrait-Sheet). 로비 왼쪽 위 정보칸에 쓴다. 비면 로비 대기 첫 프레임.")]
            public Sprite portrait;

            [Header("261114 - 팀원 모집 미리보기(롤 스킬 미리보기처럼 공격을 반복해서 보여준다)")]
            public Sprite[] previewAttackFrames = Array.Empty<Sprite>();
            [Min(1f)] public float previewAttackFps = 12f;
            [Tooltip("공격 프레임 중 맞는 순간(0부터). 이때 허수아비가 맞고 피해 숫자가 뜬다.")]
            [Min(0)] public int previewHitFrame = 5;
            [Tooltip("베기 이펙트 프레임(인게임 PlayerAttackEffect와 같은 그림).")]
            public Sprite[] previewSlashFrames = Array.Empty<Sprite>();
            [Tooltip("미리보기 아래 태그 - 예: \"근접 · 다수 · 결계\"")]
            public string roleTags;
            [TextArea] public string basicAttackDescription;
            [TextArea] public string passiveDescription;

            [Header("261114 - 인게임 모션 (IDLE · WALK는 위 로비 프레임을 같이 쓴다, 각 100ms)")]
            public Sprite[] atkFrames = Array.Empty<Sprite>();
            [Tooltip("쓰러짐(지친 자세) 2프레임 × 300ms 반복.")]
            public Sprite[] dieFrames = Array.Empty<Sprite>();
            [Tooltip("근접 공격 이펙트(EFFECT, 50ms씩). 왼쪽을 향해 그려진 원본 그대로.")]
            public Sprite[] effectFrames = Array.Empty<Sprite>();
            [Tooltip("원거리 투사체 그림(Projectile). 왼쪽을 향해 그려진 원본 그대로.")]
            public Sprite projectileSprite;

            [Header("261114 - 영웅 화면 (기획서 6장 ① 개요)")]
            [Tooltip("해금 엽전(0 = 기본 보유).")]
            [Min(0)] public int unlockPrice;
            public string passiveName;
            [Tooltip("전용 신통력 2종 이름(기획서 ⑤).")]
            public string[] exclusiveNames = Array.Empty<string>();
            [Tooltip("전용 신통력 2종 설명.")]
            [TextArea] public string[] exclusiveDescriptions = Array.Empty<string>();
            public string quote;

            [Header("261114 - 신내림 노드 (기획서 6장 ③, 순서 = SinnaerimSlot)")]
            public SinnaerimNode[] sinnaerim = Array.Empty<SinnaerimNode>();
        }

        /// <summary>신내림 칸 순서(아래 → 위). 기본 → 갈래(선봉 · 수호 · 유격 각 2칸) 택1 → 관문 → 택1(A · B) → 각성.</summary>
        public enum SinnaerimSlot
        {
            Base = 0,
            Vanguard1 = 1,
            Vanguard2 = 2,
            Guard1 = 3,
            Guard2 = 4,
            Raid1 = 5,
            Raid2 = 6,
            Gate = 7,
            ChoiceA = 8,
            ChoiceB = 9,
            Awaken = 10
        }

        public const int SinnaerimSlotCount = 11;

        [Serializable]
        public class SinnaerimNode
        {
            public string nodeName;
            [Min(0)] public int cost;
            [TextArea] public string selfEffect;
            [TextArea] public string allyEffect;
        }

        public CharacterEntry[] characters = Array.Empty<CharacterEntry>();

        /// <summary>목록 순서(0부터). 못 찾으면 -1.</summary>
        public int IndexOf(CharacterEntry entry)
        {
            if (entry == null || characters == null)
            {
                return -1;
            }
            for (int i = 0; i < characters.Length; i++)
            {
                if (characters[i] == entry)
                {
                    return i;
                }
            }
            return -1;
        }

        public CharacterEntry At(int index) =>
            characters != null && index >= 0 && index < characters.Length ? characters[index] : null;

        /// <summary>characterId로 항목을 찾는다 - 못 찾으면 null(호출부에서 반드시 null 체크할 것).</summary>
        public CharacterEntry FindById(string characterId)
        {
            if (string.IsNullOrEmpty(characterId) || characters == null)
            {
                return null;
            }

            foreach (CharacterEntry entry in characters)
            {
                if (entry != null && entry.characterId == characterId)
                {
                    return entry;
                }
            }
            return null;
        }
    }
}
