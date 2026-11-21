using UnityEngine;

namespace CheonMyeong.Maps
{
    /// <summary>
    /// 261121 추가 - 맵 배치 문서 v1.5 5-3 "투사체": 집 · 나무 · 초롱 기둥 · 좌판 · 바위 · 장독 · 울타리 같은 딱딱한 물체는 투사체도 막는다.
    /// 물 · 땅 꺼짐 구덩이는 투사체가 지나간다 - 그래서 딱딱한 물체만 따로 MapSolid 레이어에 둔다(물 · 구덩이는 Map 레이어).
    /// 플레이어 투사체 · 역귀 호리병이 매 스텝 점 하나만 검사한다(Physics2D.OverlapPoint, 할당 없음).
    /// </summary>
    public static class MapObstacles
    {
        public const string SolidLayerName = "MapSolid";

        private static int solidMask = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            solidMask = -1;
        }

        public static int SolidMask
        {
            get
            {
                if (solidMask < 0)
                {
                    solidMask = LayerMask.GetMask(SolidLayerName);
                }
                return solidMask;
            }
        }

        /// <summary>그 자리에 딱딱한 물체가 있어 투사체가 막히는지. MapSolid 레이어가 없는 맵(옛 40×40)이면 늘 false.</summary>
        public static bool BlocksProjectile(Vector2 position)
        {
            int mask = SolidMask;
            return mask != 0 && Physics2D.OverlapPoint(position, mask) != null;
        }
    }
}
