using System;
using System.Threading;
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
    ///
    /// 261114 - 한 PC에서 빌드를 두 개 띄워 2인 시연할 때: 익명 로그인 토큰은 PC(PlayerPrefs)에 저장돼서 두 창이 같은 플레이어로
    /// 로그인되고, 그러면 자기 방에 자기가 참가하는 꼴이라 참가가 실패한다. 빌드에서는 먼저 뜬 창 = 기본 프로필,
    /// 두 번째 창 = "player2" 프로필로 따로 로그인한다(윈도우 이름 있는 Mutex로 몇 번째 창인지 판별, 창을 닫으면 OS가 풀어줌).
    /// 실행 인자 -ugsProfile 이름 으로 직접 정할 수도 있다. 에디터(멀티플레이 플레이 모드 포함)는 기존 동작 그대로.
    /// </summary>
    public class GameServicesBootstrap : MonoBehaviour
    {
        private const int MaxLocalInstances = 4;

        // 프로세스가 살아 있는 동안 들고 있어야 한다(GC로 핸들이 닫히면 다음 창이 같은 번호를 받음).
        private static Mutex instanceMutex;

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

            // 261114 - 창이 포커스를 잃어도(알트탭 · 한 PC에 창 두 개) 게임 루프를 계속 돌린다. 꺼져 있으면 방장 창이 멈춰
            // 참가자 연결이 끊긴다. Player Settings의 Run In Background가 꺼져 있어도 여기서 켠다.
            Application.runInBackground = true;
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

            string profile = ResolveProfile();
            if (string.IsNullOrEmpty(profile))
            {
                await UnityServices.InitializeAsync();
            }
            else
            {
                await UnityServices.InitializeAsync(new InitializationOptions().SetProfile(profile));
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            IsReady = true;
            Debug.Log($"[CheonMyeong] UGS 초기화 완료. PlayerId: {AuthenticationService.Instance.PlayerId} (프로필: {(string.IsNullOrEmpty(profile) ? "기본" : profile)})");
        }

        /// <summary>null = 기본 프로필.</summary>
        private static string ResolveProfile()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "-ugsProfile", StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
#if UNITY_EDITOR
            return null;
#else
            for (int slot = 1; slot <= MaxLocalInstances; slot++)
            {
                var mutex = new Mutex(true, "CheonMyeong.UGS.LocalInstance." + slot, out bool createdNew);
                if (createdNew)
                {
                    instanceMutex = mutex;
                    return slot == 1 ? null : "player" + slot;
                }
                mutex.Dispose();
            }
            return "player" + UnityEngine.Random.Range(100, 1000);
#endif
        }
    }
}
