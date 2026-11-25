using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// 피곤 게이지 하나. 시계를 따라 쌓이고 동면이 아닌 잠을 자면 줄어든다.
    /// 높을 때의 불이익은 "상호작용에 드는 시계 시간이 늘어남" 하나뿐이다(계획서 9장 6번).
    /// </summary>
    public class ProtoFatigue : MonoBehaviour
    {
        const float Max = 100f;

        /// <summary>깨어 있는 시계 시간이 이만큼이면 피곤이 가득 찬다.</summary>
        const float FullAfterSeconds = 300f;

        /// <summary>자는 시계 시간이 이만큼이면 가득 찬 피곤이 바닥난다.</summary>
        const float EmptyAfterSleepSeconds = 150f;

        /// <summary>이 값부터 상호작용 시간이 늘어나기 시작한다.</summary>
        const float PenaltyStart = 60f;

        /// <summary>피곤이 가득일 때 상호작용 시간이 늘어나는 비율(1이면 두 배).</summary>
        const float PenaltyAtFull = 1f;

        ProtoClock _clock;
        ProtoSeedState _seed;
        float _dim;

        public float Value { get; private set; }

        public bool Sleeping { get; private set; }

        /// <summary>상호작용 시계 시간에 곱하는 값. 1이면 불이익 없음.</summary>
        public float TimeMultiplier => 1f + Mathf.Clamp01((Value - PenaltyStart) / (Max - PenaltyStart)) * PenaltyAtFull;

        void Awake()
        {
            _clock = GetComponent<ProtoClock>();
            _seed = GetComponent<ProtoSeedState>();
        }

        void Update()
        {
            _dim = Mathf.MoveTowards(_dim, Sleeping ? 0.85f : 0f, Time.unscaledDeltaTime * 1.5f);

            if (Sleeping)
            {
                Value -= Max / EmptyAfterSleepSeconds * _clock.Delta;
                if (Value <= 0f)
                {
                    Value = 0f;
                    WakeUp();
                }
            }
            else
            {
                Value = Mathf.Min(Max, Value + Max / FullAfterSeconds * _clock.Delta);
            }
        }

        public void StartSleep()
        {
            Sleeping = true;
            _clock.Scale = ProtoClock.SleepScale;
            _seed.SetWalk(false);
        }

        public void WakeUp()
        {
            Sleeping = false;
            _clock.Scale = 1f;
            _seed.SetWalk(true);
        }

        public void SetValue(float value)
        {
            Value = Mathf.Clamp(value, 0f, Max);
        }

        /// <summary>처음부터 다시 할 때. 걷기를 켜고 끄는 일은 부른 쪽이 한다.</summary>
        public void ResetFatigue(float value)
        {
            Sleeping = false;
            _dim = 0f;
            Value = value;
        }

        void OnGUI()
        {
            // 잠자는 동안 화면을 가리는 막은 시계 표시(depth 0)보다 뒤에 그려서 흐르는 초가 계속 보이게 한다.
            GUI.depth = 10;
            float s = ProtoGui.S;
            if (_dim > 0.01f)
            {
                ProtoGui.Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, _dim));
                if (Sleeping)
                {
                    ProtoGui.Text(new Rect(0f, Screen.height * 0.5f - 20f * s, Screen.width, 40f * s),
                        "자는 중... 피곤이 줄고 시계가 빨리 흐른다   [F] 일어남", 22, new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleCenter);
                }
            }

            var r = new Rect(Screen.width - 312f * s, 46f * s, 300f * s, 42f * s);
            ProtoGui.Fill(r, ProtoGui.Dark);
            string penalty = TimeMultiplier > 1.01f ? $"  (상호작용 x{TimeMultiplier:0.0})" : "";
            ProtoGui.Text(new Rect(r.x + 8f * s, r.y + 3f * s, r.width - 16f * s, 22f * s), $"피곤 {(int)Value}{penalty}", 13, Color.white);
            var barBg = new Rect(r.x + 8f * s, r.y + 29f * s, r.width - 16f * s, 9f * s);
            ProtoGui.Fill(barBg, new Color(1f, 1f, 1f, 0.15f));
            var fill = Color.Lerp(new Color(0.45f, 0.8f, 1f), new Color(1f, 0.45f, 0.3f), Value / Max);
            ProtoGui.Fill(new Rect(barBg.x, barBg.y, barBg.width * Value / Max, barBg.height), fill);
        }
    }
}
