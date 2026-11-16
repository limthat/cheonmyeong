using UnityEngine;

namespace CheonMyeong.Audio
{
    /// <summary>소리가 어느 볼륨 줄(설정 &gt; 소리)을 따르는지.</summary>
    public enum SoundBus : byte
    {
        Sfx,
        Bgm,
        Voice,
        Ui,
    }

    /// <summary>
    /// 261115 - 소리 하나(효과음 · 배경음 · 목소리)의 재생 규칙. 프로젝트 창 Create &gt; CheonMyeong &gt; Sound Cue로 만든다.
    /// 클립만 넣고 SoundLibrary의 키 칸에 끌어다 놓으면 코드 수정 없이 소리가 난다.
    /// - 클립이 여러 개면 매번 무작위(같은 클립 연속 방지). 배경음에서 Layered를 켜면 클립들이 동시에 겹쳐 재생되는 '층'이 된다
    ///   (최종전 원한 단계마다 악기 층 추가 - 기획서 11장).
    /// - MaxInstances / MinInterval로 같은 소리가 한꺼번에 쏟아지는 것을 막는다(기획서 "원귀 소리 동시 재생 제한").
    /// - Positional이면 카메라(내 화면)에서 멀수록 작아지고 좌우로 나뉜다. 화면 밖 동료 쪽 소리 처리용.
    /// </summary>
    [CreateAssetMenu(menuName = "CheonMyeong/Sound Cue", fileName = "SoundCue")]
    public sealed class SoundCue : ScriptableObject
    {
        [Tooltip("재생할 클립. 여러 개면 무작위(배경음 Layered면 동시에 겹치는 층).")]
        public AudioClip[] clips = new AudioClip[0];

        public SoundBus bus = SoundBus.Sfx;

        [Range(0f, 1f)] public float volume = 1f;
        [Tooltip("매번 볼륨을 이만큼 무작위로 줄인다(0 = 그대로). 반복음이 덜 기계적으로 들린다.")]
        [Range(0f, 0.5f)] public float volumeJitter;
        [Tooltip("피치 무작위 범위(1 = 원래 높이).")]
        public Vector2 pitchRange = Vector2.one;

        [Header("반복 · 겹침 제한")]
        public bool loop;
        [Tooltip("동시에 울릴 수 있는 최대 개수. 넘으면 가장 오래된 것을 끊고 새로 낸다. 0 = 제한 없음.")]
        [Min(0)] public int maxInstances = 4;
        [Tooltip("같은 소리를 다시 낼 수 있는 최소 간격(초, 실제 시간).")]
        [Min(0f)] public float minInterval = 0.04f;
        [Tooltip("소리 칸이 모자랄 때 우선순위(0 = 가장 중요, 256 = 가장 덜 중요). AudioSource.priority와 같은 의미.")]
        [Range(0, 256)] public int priority = 128;

        [Header("반복 (한 파일 여러 쓰임 - 사운드 기획서 1장)")]
        [Tooltip("한 번 부를 때 몇 번 울릴지(예: 북 3타 = 3). 1 = 한 번.")]
        [Range(1, 8)] public int repeatCount = 1;
        [Tooltip("반복 사이 간격(초).")]
        [Min(0.02f)] public float repeatInterval = 0.3f;
        [Tooltip("이 길이(초)에서 끊는다(0 = 끝까지). 파일 뒤 무음 · 긴 꼬리를 잘라 쓸 때.")]
        [Min(0f)] public float maxDuration;

        [Header("위치")]
        [Tooltip("켜면 위치를 준 소리는 내 카메라에서 멀수록 작아지고 좌우로 나뉜다.")]
        public bool positional;
        [Tooltip("이 거리(유닛)부터 안 들린다.")]
        [Min(0.1f)] public float hearRange = 16f;
        [Tooltip("좌우로 나뉘는 정도(0 = 가운데 고정).")]
        [Range(0f, 1f)] public float stereoSpread = 0.6f;

        [Header("배경음")]
        [Tooltip("배경음 전용 - 클립들을 동시에 겹쳐 재생(층). GameAudio.SetBgmIntensity로 몇 층까지 들릴지 정한다.")]
        public bool layered;

        public bool HasClips => clips != null && clips.Length > 0;
    }
}
