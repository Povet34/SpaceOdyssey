using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// 프로토타입 게임 시계. 경과 초 하나만 센다. 일차, 날짜, 시각은 어디에도 없다.
    /// 평소 배율은 1(실제 1초 = 시계 1초)이고 잠자는 동안만 빨리 흐른다.
    /// 버릴 프로토타입이다(Docs/PrototypePlan.md).
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class ProtoClock : MonoBehaviour
    {
        /// <summary>자는 동안 시계 배율. 잠 한 번이 실제 수십 초 안에 끝나게 잡은 출발값(계획서 9장 3번).</summary>
        public const float SleepScale = 6f;

        public float Elapsed { get; private set; }

        /// <summary>이번 프레임에 흐른 시계 초. 멈춰 있으면 0.</summary>
        public float Delta { get; private set; }

        public float Scale { get; set; } = 1f;

        public bool Paused { get; set; }

        void Update()
        {
            Delta = Paused ? 0f : Time.deltaTime * Scale;
            Elapsed += Delta;
        }

        public void ResetClock()
        {
            Elapsed = 0f;
            Delta = 0f;
            Scale = 1f;
            Paused = false;
        }

        void OnGUI()
        {
            GUI.depth = 0;
            float s = ProtoGui.S;
            var r = new Rect(Screen.width - 312f * s, 12f * s, 300f * s, 30f * s);
            ProtoGui.Fill(r, ProtoGui.Dark);
            ProtoGui.Text(r, $"경과 {(int)Elapsed}초", 20, Color.white, TextAnchor.MiddleCenter);
        }
    }

    /// <summary>OnGUI로 글자와 사각형만 그리는 도구. 캔버스와 폰트 에셋을 새로 만들지 않기 위함.</summary>
    internal static class ProtoGui
    {
        public static readonly Color Dark = new Color(0f, 0f, 0f, 0.72f);
        public static readonly Color Accent = new Color(1f, 0.85f, 0.35f);
        public static readonly Color Dim = new Color(1f, 1f, 1f, 0.7f);

        static Texture2D _white;
        static GUIStyle _label;
        static GUIStyle _button;

        public static float S => Mathf.Max(1f, Screen.height / 720f);

        static Texture2D White
        {
            get
            {
                if (_white == null)
                {
                    _white = new Texture2D(1, 1);
                    _white.SetPixel(0, 0, Color.white);
                    _white.Apply();
                }
                return _white;
            }
        }

        public static void Fill(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, White);
            GUI.color = prev;
        }

        static GUIStyle Label(int size, Color c, TextAnchor anchor, bool wrap)
        {
            if (_label == null) _label = new GUIStyle(GUI.skin.label);
            _label.fontSize = Mathf.RoundToInt(size * S);
            _label.alignment = anchor;
            _label.wordWrap = wrap;
            _label.normal.textColor = c;
            return _label;
        }

        public static void Text(Rect r, string text, int size, Color c, TextAnchor anchor = TextAnchor.UpperLeft, bool wrap = false)
        {
            GUI.Label(r, text, Label(size, c, anchor, wrap));
        }

        public static float TextHeight(string text, int size, float width)
        {
            return Label(size, Color.white, TextAnchor.UpperLeft, true).CalcHeight(new GUIContent(text), width);
        }

        public static bool Button(Rect r, string text, int size)
        {
            if (_button == null) _button = new GUIStyle(GUI.skin.button);
            _button.fontSize = Mathf.RoundToInt(size * S);
            return GUI.Button(r, text, _button);
        }

        /// <summary>칸으로 나뉜 눈금. mark가 0 이상이면 그 칸 앞에 빨간 선(되얼릴 수 없는 칸)을 긋는다.</summary>
        public static void Gauge(Rect r, int value, int max, int mark, Color fill)
        {
            Fill(r, new Color(0f, 0f, 0f, 0.55f));
            float w = r.width / max;
            for (int i = 0; i < max; i++)
            {
                var cell = new Rect(r.x + i * w + 1f, r.y + 1f, w - 2f, r.height - 2f);
                Fill(cell, i < value ? fill : new Color(1f, 1f, 1f, 0.12f));
            }
            if (mark >= 0) Fill(new Rect(r.x + mark * w - 1f, r.y - 3f, 2f, r.height + 6f), new Color(1f, 0.3f, 0.25f));
        }
    }
}
