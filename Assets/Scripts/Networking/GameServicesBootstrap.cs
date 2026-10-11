using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace CheonMyeong.Networking
{
    /// <summary>
    /// Unity Gaming Services(UGS) 초기화 + 익명 로그인. 세션/Relay를 쓰려면
    /// 반드시 이게 먼저 끝나 있어야 한다. 씬에 하나만 있어야 하므로
    /// DontDestroyOnLoad + 싱글턴으로 관리한다.
    ///
    /// 사전 준비(코드로 불가 - 직접 해야 함):
    /// 1) Project Settings -> Services에서 유니티 계정 로그인 + 이 프로젝트를
    ///    Unity Cloud 프로젝트에 연결
    /// 2) dashboard.unity.com에서 해당 프로젝트의 Relay/Lobby 서비스 활성화
    /// 둘 다 안 돼 있으면 InitializeAsync()가 예외를 던진다.
    /// </summary>
    public class GameServicesBootstrap : MonoBehaviour
    {
        public static GameServicesBootstrap Instance { get; private set; }

        public bool IsReady { get; private set; }

        private Task initializeTask;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// 여러 곳에서 동시에 호출해도 초기화가 한 번만 일어나도록 Task를 캐싱한다.
        /// </summary>
        public Task InitializeAsync()
        {
            if (initializeTask == null)
            {
                initializeTask = InitializeInternalAsync();
            }
            return initializeTask;
        }

        private async Task InitializeInternalAsync()
        {
            if (IsReady)
            {
                return;
            }

            await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            IsReady = true;
            Debug.Log($"[CheonMyeong] UGS 초기화 완료. PlayerId: {AuthenticationService.Instance.PlayerId}");
        }
    }
}
