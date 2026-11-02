using UnityEngine;

namespace CheonMyeong.Boss
{
    /// <summary>
    /// 261101 추가 - 억쇠 "정신병" 차원 규칙(예섭: "샤코 분신이 모데카이저 궁을 쓰는 것").
    /// 모데카이저 '죽음의 세계'처럼 맵 위 같은 자리에 겹쳐 있는 별도 차원으로 처리한다: 차원 안(끌려간 플레이어 + 분신)과
    /// 밖(나머지 플레이어 · 원귀 · 보스 본체)은 서로 때리지도, 노리지도 못한다. 실제 위치는 그대로라 카메라/맵 경계/네트워크
    /// 위치 동기화를 따로 손볼 필요가 없다.
    ///
    /// 판정은 서버에서 하지만, 화면 연출(어둡게 덮기/반투명)용으로 클라이언트도 같은 값을 읽는다(EoksoeBoss의 NetworkVariable).
    /// </summary>
    public static class DeathRealm
    {
        /// <summary>차원이 열려 있는지.</summary>
        public static bool IsActive => EoksoeBoss.Instance != null && EoksoeBoss.Instance.RealmActive;

        /// <summary>이 오브젝트가 차원 안에 있는지(끌려간 플레이어 본인 또는 분신).</summary>
        public static bool IsInRealm(GameObject target)
        {
            if (target == null || !IsActive)
            {
                return false;
            }
            if (target.TryGetComponent(out BossCloneHitbox _))
            {
                return true;
            }
            return EoksoeBoss.Instance.IsRealmPlayer(target);
        }

        /// <summary>두 오브젝트가 서로 영향을 줄 수 있는지 - 둘 다 차원 안이거나 둘 다 밖일 때만.</summary>
        public static bool CanInteract(GameObject a, GameObject b)
        {
            if (!IsActive)
            {
                return true;
            }
            return IsInRealm(a) == IsInRealm(b);
        }
    }
}
