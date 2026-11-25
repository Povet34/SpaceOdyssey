using UnityEngine;
using UnityEngine.InputSystem;

namespace Prototype
{
    /// <summary>
    /// 기상 요약. 시작할 때는 카드 한 장이고, 동면 뒤에는 연도 바에 이벤트 표식이 남은 형태다.
    /// 방치 결과를 글로 다 쓰지 않는다. 키 눈금과 바닥난 물은 걸어가서 보게 한다(계획서 9장 4번).
    /// </summary>
    public class ProtoWakeSummary : MonoBehaviour
    {
        enum Mode
        {
            None,
            Intro,
            AfterHibernation,
        }

        ProtoSeedState _seed;
        ProtoHibernation _hib;
        Mode _mode;
        int _selected;

        void Awake()
        {
            _seed = GetComponent<ProtoSeedState>();
            _hib = GetComponent<ProtoHibernation>();
        }

        public void ShowIntro()
        {
            _mode = Mode.Intro;
        }

        public void ShowAfterHibernation()
        {
            _mode = Mode.AfterHibernation;
            _selected = 0;
        }

        public void Hide()
        {
            _mode = Mode.None;
        }

        void Update()
        {
            if (_mode == Mode.None) return;
            // 키보드가 없는 환경(장치 분리)에서는 카드를 키로 닫을 수 없어 건너뛴다.
            var kb = Keyboard.current;
            if (kb == null) return;

            // 닫는 키는 상호작용 키(F)와 겹치지 않게 Enter와 Space로 한다. 같은 프레임에 장소 일이 같이 실행되는 것을 막는다.
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)
            {
                var mode = _mode;
                _mode = Mode.None;
                if (mode == Mode.Intro) _seed.BeginPlay();
                else _seed.BeginResult();
                return;
            }

            if (_mode == Mode.AfterHibernation)
            {
                if (kb.rightArrowKey.wasPressedThisFrame) _selected = (_selected + 1) % _hib.Marks.Count;
                if (kb.leftArrowKey.wasPressedThisFrame) _selected = (_selected + _hib.Marks.Count - 1) % _hib.Marks.Count;
            }
        }

        void OnGUI()
        {
            if (_mode == Mode.None) return;
            GUI.depth = -50;
            float s = ProtoGui.S;
            ProtoGui.Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, _mode == Mode.Intro ? 0.5f : 0.8f));

            float w = (_mode == Mode.Intro ? 640f : 900f) * s;
            float h = (_mode == Mode.Intro ? 330f : 380f) * s;
            var card = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            ProtoGui.Fill(card, new Color(0.05f, 0.07f, 0.1f, 0.96f));
            ProtoGui.Fill(new Rect(card.x, card.y, 4f * s, card.height), ProtoGui.Accent);
            float pad = 24f * s;
            float x = card.x + pad;
            float iw = w - pad * 2f;

            ProtoGui.Text(new Rect(x, card.y + 16f * s, iw, 32f * s), "기상 요약", 24, new Color(0.6f, 0.9f, 1f));

            if (_mode == Mode.Intro) DrawIntro(x, card.y, iw, s);
            else DrawAfter(card, x, iw, s);
        }

        void DrawIntro(float x, float top, float iw, float s)
        {
            // 첫 줄은 씨앗의 징후 문장(05_Objects.md "조건 충족")
            ProtoGui.Text(new Rect(x, top + 64f * s, iw, 30f * s), "도킹한 뒤 재배 베이의 물 사용량이 오른다.", 20, Color.white);

            string keys =
                "F 상호작용 (가까이 가야 표식이 보인다)\n" +
                "R 예약 패널 (함교 스캐너 설치)\n" +
                "T 처음부터 다시\n" +
                "WASD 이동   Q/E 화면 회전   Tab 뚜껑 (기존 키)";
            ProtoGui.Text(new Rect(x, top + 130f * s, iw, 110f * s), keys, 15, ProtoGui.Dim, TextAnchor.UpperLeft, true);
            ProtoGui.Text(new Rect(x, top + 330f * s - 56f * s, iw, 28f * s), "[Enter] 시작", 18, ProtoGui.Accent);
        }

        void DrawAfter(Rect card, float x, float iw, float s)
        {
            ProtoGui.Text(new Rect(x, card.y + 56f * s, iw, 28f * s), $"{ProtoHibernation.EndYear}년 기상", 20, Color.white);

            var bar = new Rect(x + 30f * s, card.y + 150f * s, iw - 60f * s, 14f * s);
            ProtoHibernation.DrawTrack(bar, 1f);

            // 마우스를 올린 표식이 우선이고, 없으면 화살표 키로 고른 표식이다.
            int hover = -1;
            Vector2 mouse = Event.current.mousePosition;
            for (int i = 0; i < _hib.Marks.Count; i++)
            {
                var mark = MarkRect(bar, _hib.Marks[i], s);
                if (mark.Contains(mouse)) hover = i;
            }
            int shown = hover >= 0 ? hover : _selected;

            for (int i = 0; i < _hib.Marks.Count; i++)
            {
                var mark = MarkRect(bar, _hib.Marks[i], s);
                ProtoGui.Fill(mark, i == shown ? ProtoGui.Accent : new Color(1f, 0.85f, 0.35f, 0.55f));
            }

            var m = _hib.Marks[shown];
            ProtoGui.Text(new Rect(x, card.y + 224f * s, iw, 28f * s), $"{m.Year}년  {m.Title}", 19, Color.white);
            ProtoGui.Text(new Rect(x, card.y + 256f * s, iw, 56f * s), m.Detail, 16, ProtoGui.Dim, TextAnchor.UpperLeft, true);
            ProtoGui.Text(new Rect(x, card.y + 98f * s, iw, 24f * s), "표식에 마우스를 올리거나 좌우 화살표로 고른다", 13, ProtoGui.Dim);
            ProtoGui.Text(new Rect(x, card.yMax - 44f * s, iw, 28f * s), "[Enter] 닫고 배를 둘러본다", 18, ProtoGui.Accent);
        }

        static Rect MarkRect(Rect bar, ProtoHibernation.Mark m, float s)
        {
            float size = 20f * s;
            return new Rect(bar.x + bar.width * m.Fraction - size * 0.5f, bar.y - size - 6f * s, size, size);
        }
    }
}
