using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace CheonMyeong.Input
{
    /// <summary>바꿀 수 있는 조작(설정 &gt; 조작).</summary>
    public enum GameAction
    {
        MoveUp,
        MoveDown,
        MoveLeft,
        MoveRight,
        Interact,
        Skill,
    }

    /// <summary>
    /// 261116 - 조작 키 바꾸기(키보드 키 하나씩). 저장은 PlayerPrefs(cm.keys.*), 바꾸는 즉시 저장된다.
    /// - 기본: W · S · A · D 이동, E 상호작용(로비 건물), Space 임훈 철기 돌격.
    /// - 고정(바꿀 수 없음): 방향키 이동(항상 같이 동작), ESC 메뉴, 마우스 조준, F1~F12(개발자 치트).
    /// - 다른 조작이 이미 쓰는 키를 고르면 두 조작의 키를 맞바꾼다(같은 키 두 개 방지).
    /// - 설정 화면이 키 입력을 기다리는 동안(IsCapturing) ESC는 '취소'로만 쓰이고 메뉴를 닫지 않는다(UIFlowManager가 확인).
    /// 읽기는 매 프레임 배열 하나만 본다(할당 없음).
    /// </summary>
    public static class KeyBindings
    {
        private const string Prefix = "cm.keys.";

        private static readonly Key[] Defaults = { Key.W, Key.S, Key.A, Key.D, Key.E, Key.Space };
        private static readonly string[] Names = { "up", "down", "left", "right", "interact", "skill" };
        private static readonly Key[] keys = new Key[Defaults.Length];
        private static bool loaded;
        private static int escapeBlockedFrame = -1;

        /// <summary>키가 바뀌었을 때(설정 화면 · 안내 글자 갱신용).</summary>
        public static event Action Changed;

        public static int Count => Defaults.Length;

        /// <summary>설정 화면이 다음 키를 기다리는 중.</summary>
        public static bool IsCapturing { get; private set; }

        /// <summary>이번 프레임 ESC를 메뉴 닫기로 쓰면 안 되는지(키 입력 대기 중이거나 방금 ESC로 취소함).</summary>
        public static bool IsEscapeBlocked => IsCapturing || escapeBlockedFrame == Time.frameCount;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            loaded = false;
            Changed = null;
            IsCapturing = false;
            escapeBlockedFrame = -1;
        }

        public static Key Get(GameAction action)
        {
            EnsureLoaded();
            return keys[(int)action];
        }

        public static bool IsPressed(GameAction action)
        {
            Keyboard keyboard = Keyboard.current;
            Key key = Get(action);
            return keyboard != null && key != Key.None && keyboard[key].isPressed;
        }

        public static bool WasPressed(GameAction action)
        {
            Keyboard keyboard = Keyboard.current;
            Key key = Get(action);
            return keyboard != null && key != Key.None && keyboard[key].wasPressedThisFrame;
        }

        public static bool WasReleased(GameAction action)
        {
            Keyboard keyboard = Keyboard.current;
            Key key = Get(action);
            return keyboard != null && key != Key.None && keyboard[key].wasReleasedThisFrame;
        }

        /// <summary>바꿀 수 없는 키(ESC · 방향키 · F1~F12 등).</summary>
        public static bool IsReserved(Key key)
        {
            if (key == Key.None || key == Key.Escape || key == Key.IMESelected)
            {
                return true;
            }
            if (key == Key.UpArrow || key == Key.DownArrow || key == Key.LeftArrow || key == Key.RightArrow)
            {
                return true;
            }
            return key >= Key.F1 && key <= Key.F12;
        }

        /// <summary>키를 정한다. 다른 조작이 쓰던 키면 맞바꾼다. 막힌 키면 false.</summary>
        public static bool Set(GameAction action, Key key)
        {
            EnsureLoaded();
            if (IsReserved(key))
            {
                return false;
            }
            int index = (int)action;
            Key previous = keys[index];
            for (int i = 0; i < keys.Length; i++)
            {
                if (i != index && keys[i] == key)
                {
                    keys[i] = previous;
                    PlayerPrefs.SetInt(Prefix + Names[i], (int)previous);
                }
            }
            keys[index] = key;
            PlayerPrefs.SetInt(Prefix + Names[index], (int)key);
            PlayerPrefs.Save();
            Changed?.Invoke();
            return true;
        }

        public static void ResetToDefaults()
        {
            EnsureLoaded();
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i] = Defaults[i];
                PlayerPrefs.DeleteKey(Prefix + Names[i]);
            }
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>화면에 보일 키 이름(키보드 배치에 맞춘 이름, 없으면 enum 이름).</summary>
        public static string DisplayName(Key key)
        {
            if (key == Key.None)
            {
                return "-";
            }
            Keyboard keyboard = Keyboard.current;
            string name = keyboard != null ? keyboard[key].displayName : null;
            return string.IsNullOrEmpty(name) ? key.ToString() : name;
        }

        public static void BeginCapture()
        {
            IsCapturing = true;
        }

        /// <summary>대기 끝. escapeUsed = ESC로 취소했으면 이 프레임 메뉴가 닫히지 않게 막는다.</summary>
        public static void EndCapture(bool escapeUsed)
        {
            IsCapturing = false;
            if (escapeUsed)
            {
                escapeBlockedFrame = Time.frameCount;
            }
        }

        /// <summary>이번 프레임 새로 눌린 키(없으면 Key.None). 키 입력 대기 중에만 부른다.</summary>
        public static Key PollPressedKey()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return Key.None;
            }
            var all = keyboard.allKeys;
            for (int i = 0; i < all.Count; i++)
            {
                KeyControl control = all[i];
                if (control != null && control.wasPressedThisFrame)
                {
                    return control.keyCode;
                }
            }
            return Key.None;
        }

        private static void EnsureLoaded()
        {
            if (loaded)
            {
                return;
            }
            loaded = true;
            for (int i = 0; i < keys.Length; i++)
            {
                Key key = (Key)PlayerPrefs.GetInt(Prefix + Names[i], (int)Defaults[i]);
                keys[i] = IsReserved(key) || !Enum.IsDefined(typeof(Key), key) ? Defaults[i] : key;
            }
        }
    }
}
