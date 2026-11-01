using Unity.Netcode;
using UnityEngine;
using UnityEngine.Pool;

namespace CheonMyeong.Networking
{
    /// <summary>
    /// 261014 추가 - 정리(리팩터링): EnemyPool.cs와 LootPool.cs가 토씨 하나 다르지 않게
    /// 중복 구현하고 있던 "NGO 공식 Object Pooling 가이드 패턴"(ObjectPool&lt;NetworkObject&gt;
    /// + INetworkPrefabInstanceHandler 등록/해제)을 이 베이스 클래스 하나로 뽑아냈다 -
    /// 동작은 기존 두 클래스와 완전히 동일(순서/조건/가드 전부 그대로)하고, 코드가 한 곳에만
    /// 있으니 이 패턴 자체를 고칠 일이 생기면(예: defaultCapacity 정책 변경) 여기 한 번만
    /// 고치면 된다. 서브클래스(EnemyPool/LootPool)는 자기 프리팹 타입에 맞는 GetAndSpawn()
    /// 오버로드만 추가로 구현하면 된다 - 이 공개 메서드 시그니처들은 바뀌지 않았다.
    ///
    /// - 서버: 서브클래스가 Pool.Get()으로 직접 풀에서 꺼내 초기화 후 NetworkObject.Spawn() 호출.
    ///   (문서상 "서버/호스트는 자기 스폰에 대해 Instantiate()가 불리지 않는다" -
    ///   즉 서버는 핸들러의 Instantiate를 거치지 않고 직접 풀에서 꺼내야 한다.)
    /// - 클라이언트: 서버가 보낸 스폰 메시지를 받으면 NGO가 이 핸들러의 Instantiate()를
    ///   자동으로 호출해서 풀에서 꺼내 재사용한다.
    /// - 디스폰: 서버에서 NetworkObject.Despawn()(destroy=true, 기본값)을 호출하면
    ///   서버/클라이언트 양쪽 각자 이 핸들러의 Destroy()가 호출되어 실제 파괴 대신
    ///   풀로 반환된다(NetworkSpawnManager.OnDespawnObject 소스로 확인함).
    /// </summary>
    public abstract class NetworkPrefabPoolBase : MonoBehaviour, INetworkPrefabInstanceHandler
    {
        [SerializeField] private int defaultCapacity = 32;
        [SerializeField] private int maxSize = 256;

        private ObjectPool<NetworkObject> pool;
        private GameObject prefabGameObject;
        private bool handlerRegistered;

        /// <summary>현재 풀에서 꺼내져 활성 상태인 인스턴스 수.</summary>
        public int ActiveCount => pool.CountActive;

        /// <summary>서브클래스의 Awake가 호출해서 풀을 초기화한다 - prefab의 GameObject를 넘긴다.</summary>
        protected void InitializePool(GameObject prefab)
        {
            prefabGameObject = prefab;

            pool = new ObjectPool<NetworkObject>(
                createFunc: CreateInstance,
                actionOnGet: no => no.gameObject.SetActive(true),
                actionOnRelease: no => no.gameObject.SetActive(false),
                actionOnDestroy: no => Destroy(no.gameObject),
                collectionCheck: false,
                defaultCapacity: defaultCapacity,
                maxSize: maxSize);
        }

        /// <summary>서브클래스의 GetAndSpawn()이 풀에서 꺼낼 때 쓴다.</summary>
        protected NetworkObject GetFromPool()
        {
            return pool.Get();
        }

        private NetworkObject CreateInstance()
        {
            return Instantiate(prefabGameObject, transform).GetComponent<NetworkObject>();
        }

        private void OnEnable()
        {
            // NetworkManager는 서버/클라이언트가 "시작"된 뒤에야 핸들러 등록 의미가 있다 -
            // 호스트는 OnServerStarted/OnClientStarted 둘 다, 순수 클라이언트는
            // OnClientStarted만 호출된다(둘 다 구독해두고 RegisterHandler에서 중복 방지).
            if (NetworkManager.Singleton == null)
            {
                return;
            }

            NetworkManager.Singleton.OnServerStarted += RegisterHandler;
            NetworkManager.Singleton.OnClientStarted += RegisterHandler;

            // 261027 수정 - Gameplay 씬은 대기실 START 때(=이미 호스트/클라이언트가 시작된 뒤) Additive로
            // 로드된다. 그래서 위 두 이벤트는 이미 지나가서 다시 오지 않고, 핸들러가 영영 등록되지 않았다
            // → 디스폰 때 풀 반환 대신 실제 Destroy가 일어나고 pool.CountActive(=ActiveCount)가 계속 쌓여서
            // 동시 최대 수 제한에 걸리면 스폰이 멈추는 버그. 이미 리스닝 중이면 바로 등록한다.
            if (NetworkManager.Singleton.IsListening)
            {
                RegisterHandler();
            }
        }

        private void OnDisable()
        {
            if (NetworkManager.Singleton == null)
            {
                return;
            }

            NetworkManager.Singleton.OnServerStarted -= RegisterHandler;
            NetworkManager.Singleton.OnClientStarted -= RegisterHandler;
            UnregisterHandler();
        }

        private void RegisterHandler()
        {
            if (handlerRegistered || NetworkManager.Singleton == null || prefabGameObject == null)
            {
                return;
            }

            NetworkManager.Singleton.PrefabHandler.AddHandler(prefabGameObject, this);
            handlerRegistered = true;
        }

        private void UnregisterHandler()
        {
            if (!handlerRegistered || NetworkManager.Singleton == null || prefabGameObject == null)
            {
                return;
            }

            NetworkManager.Singleton.PrefabHandler.RemoveHandler(prefabGameObject);
            handlerRegistered = false;
        }

        // ----- INetworkPrefabInstanceHandler -----
        // 클라이언트가 서버의 스폰 메시지를 받았을 때 NGO가 자동으로 호출한다
        // (서버/호스트 자신의 스폰에는 호출되지 않음 - 서브클래스의 GetAndSpawn을 직접 쓴다).
        NetworkObject INetworkPrefabInstanceHandler.Instantiate(ulong ownerClientId, Vector3 position, Quaternion rotation)
        {
            NetworkObject netObj = pool.Get();
            netObj.transform.SetPositionAndRotation(position, rotation);
            return netObj;
        }

        // 서버/클라이언트 양쪽에서 디스폰 시 NGO가 실제 Destroy 대신 호출한다.
        void INetworkPrefabInstanceHandler.Destroy(NetworkObject networkObject)
        {
            pool.Release(networkObject);
        }
    }
}
