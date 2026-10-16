using System;
using UnityEngine;

namespace CheonMyeong.Data
{
    /// <summary>
    /// CHOICE_MAP 화면에 나열할 맵 목록 - 씬 분리 작업(261013, 예섭 지시 "처음부터
    /// 맵별 교체 구조로") 전용 데이터. 하드코딩 금지 원칙에 따라 맵 추가/해금 여부를
    /// 전부 이 ScriptableObject 자산 하나로 관리한다.
    ///
    /// 맵이 새로 추가될 때 할 일은 딱 두 가지뿐이다: (1) 새 Gameplay 씬을 만들어
    /// Build Settings에 등록, (2) 이 자산에 행 하나 추가(표시 이름 + 그 씬 이름).
    /// ChoiceMapScreen/UICanvasBuilder 코드는 전혀 안 건드려도 된다.
    ///
    /// gameplaySceneName이 비어있으면 "추후 공개"(아직 만들어지지 않은 맵) 취급 -
    /// isUnlocked와는 별개 개념: 씬이 있어도 기획상 아직 안 풀렸으면 isUnlocked=false로
    /// 잠가둘 수 있다.
    /// </summary>
    [CreateAssetMenu(fileName = "MapCatalog", menuName = "CheonMyeong/Map Catalog")]
    public class MapCatalogSO : ScriptableObject
    {
        [Serializable]
        public class MapEntry
        {
            public string mapId;
            public string displayName;

            [Tooltip("Build Settings(Scenes In Build)에 등록된 Gameplay 씬 이름과 정확히 같아야 한다. " +
                     "비어있으면 '추후 공개'로 표시되고 선택할 수 없다.")]
            public string gameplaySceneName;

            [Tooltip("와이어프레임의 보스/웨이브 캡션 - 예: \"보스·억쇠 | 10웨이브\"")]
            public string bossCaption;

            public bool isUnlocked;
        }

        public MapEntry[] maps = Array.Empty<MapEntry>();

        /// <summary>mapId로 항목을 찾는다 - 못 찾으면 null(호출부에서 반드시 null 체크할 것).</summary>
        public MapEntry FindById(string mapId)
        {
            if (string.IsNullOrEmpty(mapId) || maps == null)
            {
                return null;
            }

            foreach (MapEntry entry in maps)
            {
                if (entry != null && entry.mapId == mapId)
                {
                    return entry;
                }
            }
            return null;
        }
    }
}
