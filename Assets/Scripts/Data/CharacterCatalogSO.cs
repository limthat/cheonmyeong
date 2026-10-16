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
        }

        public CharacterEntry[] characters = Array.Empty<CharacterEntry>();

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
