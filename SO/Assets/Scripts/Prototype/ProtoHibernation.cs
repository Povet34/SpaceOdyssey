using System.Collections.Generic;
using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// 동면 화면. 검은 화면에 가로로 긴 연도 바가 차고, 이벤트가 바 위의 자기 연도에 하나씩 떴다가 사라진다.
    /// 동면 길이는 하나로 고정이다(계획서 9장 2번). 이벤트는 배의 감지 수단이 잡은 것만 뜬다(18장 제안 4번을 흉내 냄).
    /// </summary>
    public class ProtoHibernation : MonoBehaviour
    {
        /// <summary>바의 시작과 끝 절대 연도. 방치 결과의 "몇 대"가 말이 되게 수십 년 이상으로 잡은 출발값.</summary>
        public const int StartYear = 2241;
        public const int EndYear = 2325;

        /// <summary>바의 눈금 라벨 간격(연).</summary>
        const int TickYears = 12;

        /// <summary>바가 끝까지 차는 데 드는 실제 시간(초). 판정 (다)와 한 판 길이가 이 값에 달림.</summary>
        const float BarSeconds = 45f;

        /// <summary>바가 찬 뒤 기상으로 넘어가기 전에 머무는 실제 시간(초).</summary>
        const float HoldSeconds = 1.5f;

        /// <summary>이벤트 하나가 떠 있는 실제 시간(초). 이벤트 사이 간격은 이보다 길어야 겹치지 않는다.</summary>
        const float EventShowSeconds = 4f;

        public struct Mark
        {
            public float Fraction;
            public int Year;
            public string Title;
            public string Detail;
        }

        ProtoSeedState _seed;
        float _t;

        public bool Active { get; private set; }

        /// <summary>이번 동면에서 뜬 이벤트. 기상 요약이 같은 바에 표식으로 남긴다.</summary>
        public List<Mark> Marks { get; } = new List<Mark>();

        void Awake()
        {
            _seed = GetComponent<ProtoSeedState>();
        }

        public void Begin(ProtoOutcome outcome, bool installedBefore)
        {
            Marks.Clear();
            switch (outcome)
            {
                case ProtoOutcome.Scanner:
                    AddMark(0.10f, "씨앗선이 떨어져 나감", "씨앗선이 내가 넘긴 좌표 쪽으로 떠났다.");
                    break;
                case ProtoOutcome.Instrument:
                    AddMark(0.10f, "깨우기 멈춤", "기르는 기계가 \"더 모아야 한다\"고 판정했다. 깨우기가 한 사이클 미뤄진다.");
                    break;
                case ProtoOutcome.ReserveOnly:
                    if (!installedBefore)
                    {
                        AddMark(0.10f, "함교 장거리 스캐너 설치 완료", "드론이 설치를 끝냈다. 스캔과 좌표 넘기기는 내 손이 필요해서 그대로 남았다.");
                    }
                    AddMark(0.24f, "에어록 앞에서 움직임 감지", "에어록 앞에서 움직임이 잡혔다. 가서 봐야 안다.");
                    break;
                default:
                    AddMark(0.10f, "에어록 앞에서 움직임 감지", "에어록 앞에서 움직임이 잡혔다. 가서 봐야 안다.");
                    break;
            }
            AddMark(0.50f, "미세 운석 충돌", "외피에 경미한 손상.");
            AddMark(0.80f, "태양풍 통과", "배의 감지 수단이 잡았다.");

            _t = 0f;
            Active = true;
        }

        public void Stop()
        {
            Active = false;
            Marks.Clear();
        }

        void AddMark(float fraction, string title, string detail)
        {
            int year = StartYear + Mathf.RoundToInt((EndYear - StartYear) * fraction);
            Marks.Add(new Mark { Fraction = fraction, Year = year, Title = title, Detail = detail });
        }

        void Update()
        {
            if (!Active) return;
            _t += Time.deltaTime;
            if (_t >= BarSeconds + HoldSeconds)
            {
                Active = false;
                _seed.EndHibernation();
            }
        }

        /// <summary>바의 몸통과 연도 눈금. 동면 화면과 기상 요약이 같이 쓴다.</summary>
        public static void DrawTrack(Rect bar, float progress)
        {
            float s = ProtoGui.S;
            ProtoGui.Fill(bar, new Color(1f, 1f, 1f, 0.14f));
            ProtoGui.Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(progress), bar.height), new Color(0.85f, 0.92f, 1f, 0.9f));
            int ticks = (EndYear - StartYear) / TickYears;
            for (int i = 0; i <= ticks; i++)
            {
                float x = bar.x + bar.width * i / ticks;
                ProtoGui.Fill(new Rect(x - 1f, bar.yMax, 2f, 8f * s), new Color(1f, 1f, 1f, 0.5f));
                ProtoGui.Text(new Rect(x - 40f * s, bar.yMax + 10f * s, 80f * s, 20f * s), (StartYear + i * TickYears).ToString(), 13, ProtoGui.Dim, TextAnchor.UpperCenter);
            }
        }

        void OnGUI()
        {
            if (!Active) return;
            GUI.depth = -100;
            float s = ProtoGui.S;
            ProtoGui.Fill(new Rect(0f, 0f, Screen.width, Screen.height), Color.black);

            var bar = new Rect(Screen.width * 0.08f, Screen.height * 0.56f, Screen.width * 0.84f, 14f * s);
            float progress = Mathf.Clamp01(_t / BarSeconds);
            DrawTrack(bar, progress);

            int year = StartYear + Mathf.RoundToInt((EndYear - StartYear) * progress);
            ProtoGui.Text(new Rect(bar.x + bar.width * progress - 60f * s, bar.y - 30f * s, 120f * s, 24f * s), $"{year}년", 16, Color.white, TextAnchor.MiddleCenter);

            foreach (var m in Marks)
            {
                float age = _t - m.Fraction * BarSeconds;
                if (age < 0f || age > EventShowSeconds) continue;

                // 툭 떴다가 천천히 사라진다
                float a = Mathf.Min(1f, age / 0.15f) * Mathf.Min(1f, (EventShowSeconds - age) / 0.8f);
                float x = bar.x + bar.width * m.Fraction;
                float w = 360f * s;
                float bx = Mathf.Clamp(x - w * 0.5f, 8f * s, Screen.width - w - 8f * s);
                // 선은 지금 연도 라벨(막대 머리 위) 앞에서 멈춘다. 라벨과 겹쳐 글자를 가리지 않게.
                ProtoGui.Fill(new Rect(x - 1f, bar.y - 96f * s, 2f, 62f * s), new Color(1f, 0.85f, 0.35f, a));
                ProtoGui.Text(new Rect(bx, bar.y - 150f * s, w, 26f * s), $"{m.Year}년", 15, new Color(1f, 0.85f, 0.35f, a), TextAnchor.MiddleCenter);
                ProtoGui.Text(new Rect(bx, bar.y - 126f * s, w, 28f * s), m.Title, 19, new Color(1f, 1f, 1f, a), TextAnchor.MiddleCenter);
            }
        }
    }
}
