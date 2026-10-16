using UnityEngine;

namespace CheonMyeong.Progression
{
    /// <summary>
    /// Enemy.Die()(서버)가 사망 위치에 드랍을 만들 때 쓰는 단일 접점 - PlayerCameraRig/
    /// InGameHudScreen/GameplaySceneLoader와 같은 씬 전역 싱글턴 관례(Awake에 자기 자신을
    /// 등록, OnDestroy에 해제, 중복 생성 시 경고).
    ///
    /// Enemy처럼 EnemySpawner가 직접 참조를 들고 있는 방식(SerializeField)을 안 쓴 이유:
    /// Enemy 프리팹은 에셋이라 씬 인스턴스(골드/경험치 풀)를 미리 참조로 박아둘 수 없다 -
    /// Enemy.Die()는 런타임에 "지금 씬에 떠 있는 LootSpawner"를 찾아야 하므로 싱글턴이 맞다.
    /// </summary>
    public class LootSpawner : MonoBehaviour
    {
        public static LootSpawner Instance { get; private set; }

        [SerializeField] private LootPool goldPool;
        [SerializeField] private LootPool expPool;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[CheonMyeong] LootSpawner가 씬에 두 개 이상 있습니다 - 하나만 남기고 제거하세요.");
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>서버 전용 - 호출 쪽(Enemy.Die 등)이 이미 IsServer로 가드된 컨텍스트에서만 불러야 한다.</summary>
        public void SpawnGold(Vector2 position, int amount)
        {
            if (goldPool == null || amount <= 0)
            {
                return;
            }
            goldPool.GetAndSpawn(position, amount);
        }

        /// <summary>서버 전용 - 호출 쪽(Enemy.Die 등)이 이미 IsServer로 가드된 컨텍스트에서만 불러야 한다.</summary>
        public void SpawnExp(Vector2 position, int amount)
        {
            if (expPool == null || amount <= 0)
            {
                return;
            }
            expPool.GetAndSpawn(position, amount);
        }
    }
}
