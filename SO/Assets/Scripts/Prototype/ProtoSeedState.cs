using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Prototype
{
    /// <summary>판의 큰 단계.</summary>
    public enum ProtoPhase
    {
        Intro,
        Play,
        Hibernating,
        WakeSummary,
        Result,
    }

    /// <summary>지금 고른 길. 길은 하나만 고를 수 있다.</summary>
    public enum ProtoPath
    {
        None,
        Scanner,
        Instrument,
    }

    /// <summary>동면에 들어갈 때 정해지는 결과 갈래 네 가지.</summary>
    public enum ProtoOutcome
    {
        None,
        Scanner,
        Instrument,
        Neglect,
        ReserveOnly,
    }

    /// <summary>
    /// 씨앗 "조건 충족" 한 줄기의 상태 전부와 판의 흐름. 하드코딩한 한 덩어리다.
    /// 징후 눈금, 고른 길, 설치 진척, 결과를 들고 있고, 장소 표식(ProtoSpot)이 묻는 글과 일을 정한다.
    /// 기존 컨트롤러는 컴포넌트 일반형(Behaviour)으로만 받아 켜고 끈다.
    /// </summary>
    public class ProtoSeedState : MonoBehaviour
    {
        /// <summary>시계 몇 초마다 눈금이 한 칸 오르나. 씨앗의 "하루에 한 칸"을 옮긴 값(출발값).</summary>
        const float NotchInterval = 45f;

        /// <summary>온도 눈금과 물 눈금의 칸 수(출발값).</summary>
        public const int GaugeMax = 16;

        /// <summary>이 칸부터는 되얼릴 수 없다. 눈금의 빨간 선 위치(출발값).</summary>
        public const int NoReturnMark = 8;

        /// <summary>첫 줄 상자의 시작 칸. 이미 선을 넘은 상태로 시작한다.</summary>
        const int Row1Start = 10;

        /// <summary>둘째 줄 상자의 시작 칸. 선 아래에서 시작해 눈금이 오르면 넘는다.</summary>
        const int Row2Start = 4;

        /// <summary>재배 베이 물 사용량의 시작 칸.</summary>
        const int WaterStart = 5;

        /// <summary>드론이 함교 스캐너 설치를 끝내는 데 드는 시계 시간(초). 판정 (나)가 이 값에 달림.</summary>
        const float InstallSeconds = 150f;

        /// <summary>내 손으로 스캔하는 데 드는 시계 시간(초).</summary>
        const float ScanSeconds = 20f;

        /// <summary>좌표를 기계에 넘기는 데 드는 시계 시간(초).</summary>
        const float HandoverSeconds = 6f;

        /// <summary>가락을 쳐서 녹음과 함께 넘기는 데 드는 시계 시간(초).</summary>
        const float MelodySeconds = 25f;

        /// <summary>선반에서 악기와 녹음을 집는 데 드는 시계 시간(초).</summary>
        const float PickSeconds = 3f;

        /// <summary>처음 시작할 때 피곤.</summary>
        const float StartFatigue = 20f;

        /// <summary>동면에서 깬 직후 피곤.</summary>
        const float WakeFatigue = 35f;

        /// <summary>상자 줄과 벽 눈금이 보이는 거리(미터). 에어록 표식 기준.</summary>
        const float SeedVisibleRange = 6f;

        /// <summary>돌이킬 수 없는 일(동면 입구, 설치된 스캐너 예약 취소)에서 두 번째 F를 기다리는 실제 시간(초).</summary>
        const float ConfirmSeconds = 8f;

        [field: SerializeField]
        public Behaviour WalkController { get; set; }

        [field: SerializeField]
        public CharacterController Player { get; set; }

        /// <summary>에어록 앞의 저쪽 기계와 상자 줄을 묶은 오브젝트.</summary>
        [field: SerializeField]
        public GameObject SeedGroup { get; set; }

        [field: SerializeField]
        public Transform CrateRow1 { get; set; }

        [field: SerializeField]
        public Transform CrateRow2 { get; set; }

        /// <summary>방치 결과의 키를 그은 벽 눈금.</summary>
        [field: SerializeField]
        public GameObject WallMarks { get; set; }

        /// <summary>함교의 스캐너 표식(설치 진척에 따라 키가 자란다).</summary>
        [field: SerializeField]
        public Transform Scanner { get; set; }

        /// <summary>걷기 컨트롤러와 같은 오브젝트에 있는 기존 벽 편집 도구. 걷기를 막는 동안 같이 꺼서, 패널과 카드를 누른 클릭이 뒤의 벽을 고치지 않게 한다.</summary>
        Behaviour _editTool;
        ProtoClock _clock;
        ProtoFatigue _fatigue;
        ProtoReservePanel _panel;
        ProtoHibernation _hib;
        ProtoWakeSummary _summary;
        ProtoSpot[] _spots;
        ProtoSpot _airlock;
        Renderer[] _row1;
        Renderer[] _row2;
        Renderer _scannerRenderer;
        Vector3 _startPos;

        float _nextNotchAt;
        bool _crateDirty;
        bool _installedBefore;
        float _focusDistance;

        Action _busyDone;
        string _busyLabel;
        float _busyEnd;
        float _busyDuration;
        float _busyMultiplier;

        bool _podArmed;
        float _podArmedAt;
        bool _shelfArmed;
        float _shelfArmedAt;
        string _notice;
        float _noticeUntil;

        public ProtoPhase Phase { get; private set; }

        public ProtoPath Path { get; private set; }

        public ProtoOutcome Outcome { get; private set; }

        /// <summary>드론의 스캐너 설치 진척(0~1). 시계를 따라 오른다.</summary>
        public float InstallProgress { get; private set; }

        public bool Installed => InstallProgress >= 1f;

        public bool Scanned { get; private set; }

        public bool CoordsHanded { get; private set; }

        public bool InstrumentPicked { get; private set; }

        public bool InstrumentHanded { get; private set; }

        /// <summary>눈금이 오른 횟수. 온도와 물이 같은 간격으로 오른다.</summary>
        public int Notch { get; private set; }

        /// <summary>해치 앞에 저쪽 기계와 상자 줄이 있나(씨앗선이 떠나면 없다).</summary>
        public bool SeedPresent { get; private set; }

        public bool WallMarksOn { get; private set; }

        public bool WaterEmpty { get; private set; }

        /// <summary>지금 표식을 보여 줄 장소. 가까운 하나만 보인다.</summary>
        public ProtoSpot Focus { get; private set; }

        public bool Busy => _busyDone != null;

        /// <summary>기계에 이미 넘겼으면 길을 바꿀 수 없다. 기계는 스스로 무르지 않고 넘긴 것은 되돌릴 수 없다(씨앗 문장).</summary>
        public bool Committed => CoordsHanded || InstrumentHanded;

        int Row1Temp => Mathf.Min(GaugeMax, Row1Start + Notch);

        int Row2Temp => Mathf.Min(GaugeMax, Row2Start + Notch);

        int WaterUsage => Mathf.Min(GaugeMax, WaterStart + Notch);

        bool ScannerShown => Phase <= ProtoPhase.Play ? Path == ProtoPath.Scanner : Outcome == ProtoOutcome.Scanner || Outcome == ProtoOutcome.ReserveOnly;

        void Awake()
        {
            _clock = GetComponent<ProtoClock>();
            _fatigue = GetComponent<ProtoFatigue>();
            _panel = GetComponent<ProtoReservePanel>();
            _hib = GetComponent<ProtoHibernation>();
            _summary = GetComponent<ProtoWakeSummary>();
            _spots = GetComponentsInChildren<ProtoSpot>();
            foreach (var spot in _spots)
            {
                if (spot.Role == ProtoSpot.Kind.Airlock) _airlock = spot;
            }
            _row1 = CrateRow1.GetComponentsInChildren<Renderer>(true);
            _row2 = CrateRow2.GetComponentsInChildren<Renderer>(true);
            _scannerRenderer = Scanner.GetComponent<Renderer>();
            _startPos = Player.transform.position;
            // 형식 이름을 부르지 않으려고 이름 문자열로 찾는다. 씬에 없으면 SetWalk에서 바로 터진다.
            _editTool = (Behaviour)WalkController.GetComponent("ShipEdgeEditor");

            // 새 머티리얼 에셋을 만들지 않으려고 기본 도형 색은 실행 때 입힌다.
            foreach (var r in SeedGroup.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name == "Machine") r.material.color = new Color(0.45f, 0.3f, 0.6f);
            }
            foreach (var r in WallMarks.GetComponentsInChildren<Renderer>(true))
            {
                r.material.color = r.name == "Slab" ? new Color(0.35f, 0.4f, 0.45f) : new Color(1f, 0.6f, 0.2f);
            }
        }

        void Start()
        {
            ResetAll();
        }

        /// <summary>처음부터 다시. 어느 단계에서든 부를 수 있다.</summary>
        public void ResetAll()
        {
            Phase = ProtoPhase.Intro;
            Path = ProtoPath.None;
            Outcome = ProtoOutcome.None;
            InstallProgress = 0f;
            Scanned = false;
            CoordsHanded = false;
            InstrumentPicked = false;
            InstrumentHanded = false;
            Notch = 0;
            _nextNotchAt = NotchInterval;
            SeedPresent = true;
            WallMarksOn = false;
            WaterEmpty = false;
            _crateDirty = true;
            _installedBefore = false;
            _busyDone = null;
            _podArmed = false;
            _shelfArmed = false;
            _notice = null;
            Focus = null;

            _clock.ResetClock();
            _clock.Paused = true;
            _fatigue.ResetFatigue(StartFatigue);
            _panel.Close(false);
            _hib.Stop();

            Player.enabled = false;
            Player.transform.position = _startPos;
            Player.enabled = true;

            SetWalk(false);
            _summary.ShowIntro();
        }

        /// <summary>기존 컨트롤러를 켜고 끈다. 형식 이름을 부르지 않고 컴포넌트 일반형으로만 다룬다.</summary>
        public void SetWalk(bool on)
        {
            WalkController.enabled = on;
            // 건설 모드(B)가 켜져 있으면 IMGUI 버튼 클릭이 Input System의 클릭으로도 잡혀 뒤의 벽이 바뀐다.
            _editTool.enabled = on;
        }

        public void BeginPlay()
        {
            Phase = ProtoPhase.Play;
            _clock.Paused = false;
            SetWalk(true);
        }

        public void BeginResult()
        {
            Phase = ProtoPhase.Result;
            _clock.Paused = false;
            SetWalk(true);
        }

        void BeginHibernation()
        {
            _podArmed = false;
            if (Path == ProtoPath.Scanner && CoordsHanded) Outcome = ProtoOutcome.Scanner;
            else if (Path == ProtoPath.Instrument && InstrumentHanded) Outcome = ProtoOutcome.Instrument;
            else if (Path == ProtoPath.Scanner) Outcome = ProtoOutcome.ReserveOnly;
            else Outcome = ProtoOutcome.Neglect;

            _installedBefore = Installed;
            // 드론 설치는 내가 자는 동안에도 끝난다(0.1절 결정). 스캔과 좌표 넘기기는 내 손이 필요해서 그대로 남는다.
            if (Path == ProtoPath.Scanner) InstallProgress = 1f;

            Phase = ProtoPhase.Hibernating;
            Focus = null;
            SetWalk(false);
            _clock.Paused = true;
            _hib.Begin(Outcome, _installedBefore);
        }

        /// <summary>동면 화면이 끝났을 때 불린다. 결과를 배 안에 적용하고 기상 요약을 띄운다.</summary>
        public void EndHibernation()
        {
            Phase = ProtoPhase.WakeSummary;
            SeedPresent = Outcome == ProtoOutcome.Instrument;
            WallMarksOn = Outcome == ProtoOutcome.Neglect || Outcome == ProtoOutcome.ReserveOnly;
            WaterEmpty = WallMarksOn;
            // 집기만 하고 넘기지 않은 악기는 내 손이 필요한 일을 안 한 것이라 없던 일이 된다(0.1절).
            InstrumentPicked = Outcome == ProtoOutcome.Instrument;
            _crateDirty = true;
            _fatigue.SetValue(WakeFatigue);
            _summary.ShowAfterHibernation();
        }

        void Update()
        {
            // 키보드가 없는 환경(장치 분리)에서는 키 입력만 건너뛰고 시계와 화면은 그대로 돈다.
            var kb = Keyboard.current;
            if (kb != null && kb.tKey.wasPressedThisFrame)
            {
                ResetAll();
                return;
            }

            if (Phase == ProtoPhase.Play) Tick();
            UpdateVisuals();

            if (Phase != ProtoPhase.Play && Phase != ProtoPhase.Result)
            {
                Focus = null;
                return;
            }

            if (_fatigue.Sleeping)
            {
                Focus = null;
                if (kb != null && kb.fKey.wasPressedThisFrame) _fatigue.WakeUp();
                return;
            }

            if (_panel.Open)
            {
                Focus = null;
                return;
            }

            if (Busy)
            {
                Focus = null;
                if (_clock.Elapsed >= _busyEnd)
                {
                    var done = _busyDone;
                    _busyDone = null;
                    SetWalk(true);
                    done();
                }
                return;
            }

            UpdateFocus();
            if (_podArmed && (Focus == null || Focus.Role != ProtoSpot.Kind.Pod || Time.unscaledTime - _podArmedAt > ConfirmSeconds)) _podArmed = false;
            if (_shelfArmed && (Focus == null || Focus.Role != ProtoSpot.Kind.Shelf || Time.unscaledTime - _shelfArmedAt > ConfirmSeconds)) _shelfArmed = false;
            if (kb != null && kb.fKey.wasPressedThisFrame && Focus != null && _focusDistance <= Focus.UseRange) Interact(Focus.Role);
        }

        // 시계가 흐르는 만큼 눈금이 오르고 드론이 설치를 진행한다.
        void Tick()
        {
            while (_clock.Elapsed >= _nextNotchAt)
            {
                Notch++;
                _nextNotchAt += NotchInterval;
                _crateDirty = true;
            }
            if (Path == ProtoPath.Scanner && InstallProgress < 1f)
            {
                InstallProgress = Mathf.Min(1f, InstallProgress + _clock.Delta / InstallSeconds);
            }
        }

        void UpdateFocus()
        {
            Focus = null;
            float best = float.MaxValue;
            Vector3 p = Player.transform.position;
            foreach (var spot in _spots)
            {
                float d = spot.FlatDistance(p);
                if (d <= spot.VisibleRange && d < best)
                {
                    best = d;
                    Focus = spot;
                }
            }
            _focusDistance = best;
        }

        // 가까이 갔을 때만 상자 줄과 벽 눈금이 보인다. 스캐너 표식은 진척에 따라 키가 자란다.
        void UpdateVisuals()
        {
            bool near = _airlock.FlatDistance(Player.transform.position) <= SeedVisibleRange;
            SeedGroup.SetActive(SeedPresent && near);
            WallMarks.SetActive(WallMarksOn && near);

            bool scannerShown = ScannerShown;
            Scanner.gameObject.SetActive(scannerShown);
            if (scannerShown)
            {
                float progress = Phase <= ProtoPhase.Play ? InstallProgress : 1f;
                var scale = Scanner.localScale;
                scale.y = Mathf.Lerp(0.15f, 1.4f, progress);
                Scanner.localScale = scale;
                var pos = Scanner.localPosition;
                pos.y = scale.y * 0.5f;
                Scanner.localPosition = pos;
                _scannerRenderer.material.color = progress >= 1f ? new Color(0.35f, 0.95f, 0.55f) : new Color(1f, 0.65f, 0.25f);
            }

            if (_crateDirty)
            {
                ColorRow(_row1, Row1Temp);
                ColorRow(_row2, Row2Temp);
                _crateDirty = false;
            }
        }

        static void ColorRow(Renderer[] row, int temp)
        {
            var c = Color.Lerp(new Color(0.55f, 0.85f, 1f), new Color(1f, 0.3f, 0.12f), temp / (float)GaugeMax);
            foreach (var r in row) r.material.color = c;
        }

        // ---- 길 고르기 ----

        /// <summary>예약 패널에서 함교 스캐너 설치를 예약한다. 소지품 길을 골랐다면 그것을 취소한다.</summary>
        public void ReserveScanner()
        {
            if (Committed)
            {
                Notice("기계에 넘긴 것은 되돌릴 수 없다. 길을 바꿀 수 없다.");
                return;
            }
            if (Path == ProtoPath.Instrument)
            {
                InstrumentPicked = false;
                InstrumentHanded = false;
                Notice("길은 하나만 고른다. 소지품 길이 취소됐다.");
            }
            Path = ProtoPath.Scanner;
            InstallProgress = 0f;
            Scanned = false;
            CoordsHanded = false;
        }

        public void CancelScannerReservation()
        {
            if (Committed)
            {
                Notice("기계에 넘긴 것은 되돌릴 수 없다. 길을 바꿀 수 없다.");
                return;
            }
            Path = ProtoPath.None;
            InstallProgress = 0f;
            Scanned = false;
            CoordsHanded = false;
            Notice("스캐너 설치 예약이 취소됐다.");
        }

        void PickInstrument()
        {
            if (Path == ProtoPath.Scanner)
            {
                InstallProgress = 0f;
                Scanned = false;
                CoordsHanded = false;
                Notice("길은 하나만 고른다. 스캐너 설치 예약이 취소됐다.");
            }
            Path = ProtoPath.Instrument;
            InstrumentPicked = true;
            InstrumentHanded = false;
        }

        // ---- 상호작용 ----

        void Interact(ProtoSpot.Kind kind)
        {
            if (Phase == ProtoPhase.Result && kind != ProtoSpot.Kind.Bed) return;

            switch (kind)
            {
                case ProtoSpot.Kind.Bed:
                    if (_fatigue.Value < 3f) Notice("피곤하지 않아 잠이 오지 않는다.");
                    else _fatigue.StartSleep();
                    break;
                case ProtoSpot.Kind.Shelf:
                    if (Committed) Notice("기계에 넘긴 것은 되돌릴 수 없다. 길을 바꿀 수 없다.");
                    else if (!InstrumentPicked)
                    {
                        // 설치가 진행 중이거나 끝난 스캐너 예약은 집는 순간 취소되므로 한 번 확인한다. 설치가 없으면 한 번에 집는다.
                        if (Path == ProtoPath.Scanner && InstallProgress > 0f && !_shelfArmed)
                        {
                            _shelfArmed = true;
                            _shelfArmedAt = Time.unscaledTime;
                        }
                        else
                        {
                            _shelfArmed = false;
                            StartAction("악기와 고향의 녹음을 집는다", PickSeconds, PickInstrument);
                        }
                    }
                    break;
                case ProtoSpot.Kind.Pod:
                    if (_podArmed) BeginHibernation();
                    else
                    {
                        _podArmed = true;
                        _podArmedAt = Time.unscaledTime;
                    }
                    break;
                case ProtoSpot.Kind.Airlock:
                    if (Path == ProtoPath.Instrument && InstrumentPicked && !InstrumentHanded)
                    {
                        StartAction("가락을 쳐서 녹음과 함께 기계에 넘긴다", MelodySeconds, () => InstrumentHanded = true);
                    }
                    else if (Path == ProtoPath.Scanner && Scanned && !CoordsHanded)
                    {
                        StartAction("좌표를 기계에 넘긴다", HandoverSeconds, () => CoordsHanded = true);
                    }
                    break;
                case ProtoSpot.Kind.Bridge:
                    if (Installed && !Scanned) StartAction("스캐너로 지금도 그런지 본다", ScanSeconds, () => Scanned = true);
                    break;
            }
        }

        // 내 손으로 하는 일은 시계 시간이 들고, 피곤이 높으면 더 든다. 그동안 걷지 못한다.
        void StartAction(string label, float baseSeconds, Action done)
        {
            _busyMultiplier = _fatigue.TimeMultiplier;
            _busyDuration = baseSeconds * _busyMultiplier;
            _busyEnd = _clock.Elapsed + _busyDuration;
            _busyLabel = label;
            _busyDone = done;
            SetWalk(false);
        }

        void Notice(string text)
        {
            _notice = text;
            _noticeUntil = Time.unscaledTime + 4f;
        }

        string Secs(float baseSeconds)
        {
            return $"{Mathf.CeilToInt(baseSeconds * _fatigue.TimeMultiplier)}초";
        }

        /// <summary>장소 표식의 마지막 줄에 쓰는 F 키 안내. 지금 할 일이 없으면 null. 보이는 거리 안이지만 쓰는 거리 밖이면 "더 가까이"로 안내한다.</summary>
        public string ActionText(ProtoSpot.Kind kind)
        {
            string text = RawActionText(kind);
            if (text != null && Focus != null && Focus.Role == kind && _focusDistance > Focus.UseRange) return "더 가까이 가야 쓸 수 있다";
            return text;
        }

        string RawActionText(ProtoSpot.Kind kind)
        {
            if (Phase == ProtoPhase.Result && kind != ProtoSpot.Kind.Bed) return null;

            switch (kind)
            {
                case ProtoSpot.Kind.Bed:
                    return "[F] 잔다 (피곤이 줄고 시계가 빨리 흐른다)";
                case ProtoSpot.Kind.Shelf:
                    if (InstrumentPicked || Committed) return null;
                    if (_shelfArmed) return "스캐너 설치 예약이 취소된다. 한 번 더 [F]";
                    return $"[F] 악기와 고향의 녹음을 집는다 ({Secs(PickSeconds)})" + (Path == ProtoPath.Scanner ? "\n집으면 스캐너 설치 예약이 취소된다" : "");
                case ProtoSpot.Kind.Pod:
                    return _podArmed ? "한 번 더 [F]: 동면에 들어간다 (돌이킬 수 없다)" : "[F] 동면 포드에 들어간다";
                case ProtoSpot.Kind.Airlock:
                    if (Path == ProtoPath.Instrument && InstrumentPicked && !InstrumentHanded) return $"[F] 가락을 쳐서 녹음과 함께 기계에 넘긴다 ({Secs(MelodySeconds)})";
                    if (Path == ProtoPath.Scanner && Scanned && !CoordsHanded) return $"[F] 좌표를 기계에 넘긴다 ({Secs(HandoverSeconds)})";
                    return null;
                case ProtoSpot.Kind.Bridge:
                    if (Installed && !Scanned) return $"[F] 스캐너로 지금도 그런지 본다 ({Secs(ScanSeconds)})";
                    return null;
            }
            return null;
        }

        /// <summary>장소 표식의 본문(징후 문장과 눈금)을 그리고 쓴 높이를 돌려준다. draw가 false면 높이만 잰다.</summary>
        public float MarkerBody(ProtoSpot spot, Rect r, bool draw)
        {
            float s = ProtoGui.S;
            float y = r.y;

            void Line(string text, int size = 15)
            {
                float h = ProtoGui.TextHeight(text, size, r.width);
                if (draw) ProtoGui.Text(new Rect(r.x, y, r.width, h), text, size, Color.white, TextAnchor.UpperLeft, true);
                y += h + 2f * s;
            }

            void Gauge(string label, int value, int mark, Color fill)
            {
                float h = 18f * s;
                if (draw)
                {
                    ProtoGui.Text(new Rect(r.x, y, 84f * s, h), label, 13, ProtoGui.Dim, TextAnchor.MiddleLeft);
                    ProtoGui.Gauge(new Rect(r.x + 88f * s, y + 3f * s, r.width - 88f * s, 12f * s), value, GaugeMax, mark, fill);
                }
                y += h + 2f * s;
            }

            Color temp1 = Color.Lerp(new Color(0.55f, 0.85f, 1f), new Color(1f, 0.3f, 0.12f), Row1Temp / (float)GaugeMax);
            Color temp2 = Color.Lerp(new Color(0.55f, 0.85f, 1f), new Color(1f, 0.3f, 0.12f), Row2Temp / (float)GaugeMax);
            Color water = new Color(0.4f, 0.7f, 1f);

            switch (spot.Role)
            {
                case ProtoSpot.Kind.Planter:
                    // 씨앗 "조건 충족"의 징후와 방치 문장
                    if (WaterEmpty)
                    {
                        Line("재배 베이의 물은 바닥나 있다.");
                        Gauge("물", 0, -1, water);
                    }
                    else
                    {
                        Line("도킹한 뒤 재배 베이의 물 사용량이 오른다.");
                        Gauge("물 사용량", WaterUsage, -1, water);
                    }
                    break;

                case ProtoSpot.Kind.Airlock:
                    if (SeedPresent)
                    {
                        Line("에어록 앞에 저쪽 기계가 작은 상자들을 줄지어 내놓았다.");
                        Line("상자의 온도 눈금", 13);
                        Gauge("첫 줄", Row1Temp, NoReturnMark, temp1);
                        Gauge("둘째 줄", Row2Temp, NoReturnMark, temp2);
                        Line("첫 줄은 이미 되얼릴 수 없는 칸을 넘었다.");
                        if (Outcome == ProtoOutcome.Instrument)
                        {
                            // 씨앗 "악기 + 고향의 녹음" 길의 원문
                            Line("기계가 그것을 가르칠 것의 목록 맨 위에 올리고 \"더 모아야 한다\"고 판정한다.");
                            Line("깨우기가 한 사이클 미뤄진다.");
                            Line("남는 것: 번 시간. 뒤 시기에 그 아이들의 자장가는 내 가락이다.");
                        }
                    }
                    else if (WallMarksOn)
                    {
                        // 씨앗 "조건 충족"의 방치 원문
                        Line("깼을 때 씨앗선은 이미 떠났고 에어록 앞 벽에 키를 그은 눈금만 남았다.");
                        Line("눈금의 수로 몇 대가 지났는지 센다.");
                    }
                    break;

                case ProtoSpot.Kind.Bridge:
                    if (Phase <= ProtoPhase.Play)
                    {
                        if (Path != ProtoPath.Scanner)
                        {
                            Line("장거리 스캐너: 설치 예약 없음 ([R] 예약 패널)", 14);
                        }
                        else if (!Installed)
                        {
                            Line($"장거리 스캐너: 드론 배정됨, 설치 중 {Mathf.FloorToInt(InstallProgress * 100f)}%", 14);
                            Gauge("설치", Mathf.RoundToInt(InstallProgress * GaugeMax), -1, new Color(1f, 0.65f, 0.25f));
                        }
                        else if (!Scanned)
                        {
                            Line("장거리 스캐너: 설치됨. 아직 보지 않았다.", 14);
                        }
                        else if (!CoordsHanded)
                        {
                            Line("스캔을 마쳤다. 좌표를 얻었다. 해치 앞 기계에 넘길 차례다.", 14);
                        }
                        else
                        {
                            Line("좌표를 기계에 넘겼다.", 14);
                        }
                    }
                    else if (Outcome == ProtoOutcome.Scanner)
                    {
                        // 씨앗 "장거리 스캐너" 길의 원문
                        Line("남는 것: 넘긴 좌표. 뒤 시기에 그곳에 사람이 산다. 스캐너가 본 곳은 저쪽에서도 나를 봤다.");
                    }
                    else if (Outcome == ProtoOutcome.ReserveOnly)
                    {
                        Line("스캐너는 설치돼 있음.");
                    }
                    break;

                case ProtoSpot.Kind.Shelf:
                    if (InstrumentPicked) Line("악기와 고향의 녹음을 집었다.", 14);
                    break;

                case ProtoSpot.Kind.Pod:
                    if (Phase == ProtoPhase.Result) Line("[T] 처음부터 다시", 14);
                    break;
            }
            return y - r.y;
        }

        string PathStatus()
        {
            // 동면 뒤에는 "~할 차례"가 아니라 결과 갈래로 보여 준다(손이 필요한 일은 이미 지나갔다).
            if (Phase >= ProtoPhase.WakeSummary)
            {
                switch (Outcome)
                {
                    case ProtoOutcome.Scanner: return "길: 장거리 스캐너 (함교)\n좌표를 넘김";
                    case ProtoOutcome.Instrument: return "길: 악기 + 고향의 녹음\n기계에 넘김";
                    case ProtoOutcome.ReserveOnly: return "길: 장거리 스캐너 (함교)\n설치만 됨";
                    default: return "길: 고르지 않았다";
                }
            }

            switch (Path)
            {
                case ProtoPath.Scanner:
                    if (!Installed) return $"길: 장거리 스캐너 (함교)\n드론 설치 중 {Mathf.FloorToInt(InstallProgress * 100f)}%";
                    if (!Scanned) return "길: 장거리 스캐너 (함교)\n설치됨. 함교에서 스캔할 차례";
                    if (!CoordsHanded) return "길: 장거리 스캐너 (함교)\n스캔함. 해치 앞 기계에 좌표를 넘길 차례";
                    return "길: 장거리 스캐너 (함교)\n좌표를 넘김";
                case ProtoPath.Instrument:
                    return InstrumentHanded ? "길: 악기 + 고향의 녹음\n기계에 넘김" : "길: 악기 + 고향의 녹음\n집음. 해치 앞 기계에 넘길 차례";
            }
            return "길: 아직 없음";
        }

        void OnGUI()
        {
            GUI.depth = 0;
            float s = ProtoGui.S;

            // 오른쪽 위: 시계와 피곤 아래에 길 상태
            string path = PathStatus();
            float ph = ProtoGui.TextHeight(path, 14, 284f * s) + 10f * s;
            var pr = new Rect(Screen.width - 312f * s, 92f * s, 300f * s, ph);
            ProtoGui.Fill(pr, ProtoGui.Dark);
            ProtoGui.Text(new Rect(pr.x + 8f * s, pr.y + 5f * s, pr.width - 16f * s, ph), path, 14, Color.white, TextAnchor.UpperLeft, true);

            // 왼쪽 아래: 새 키 도움말. 항상 보인다.
            string help = "F 상호작용   R 예약 패널   T 처음부터 다시   Enter 카드 닫기\n(기존) WASD 이동   Q/E 화면 회전   Tab 뚜껑";
            float hh = ProtoGui.TextHeight(help, 13, 600f * s) + 8f * s;
            var hr = new Rect(12f * s, Screen.height - hh - 12f * s, 620f * s, hh);
            ProtoGui.Fill(hr, ProtoGui.Dark);
            ProtoGui.Text(new Rect(hr.x + 8f * s, hr.y + 4f * s, hr.width - 16f * s, hh), help, 13, Color.white, TextAnchor.UpperLeft, true);

            // 위 가운데: 잠깐 뜨는 알림
            if (_notice != null && Time.unscaledTime < _noticeUntil)
            {
                var nr = new Rect((Screen.width - 520f * s) * 0.5f, 14f * s, 520f * s, 34f * s);
                ProtoGui.Fill(nr, ProtoGui.Dark);
                ProtoGui.Text(nr, _notice, 16, ProtoGui.Accent, TextAnchor.MiddleCenter);
            }

            // 가운데 아래: 내 손으로 하는 일의 진행
            if (Busy)
            {
                var br = new Rect((Screen.width - 460f * s) * 0.5f, Screen.height - 150f * s, 460f * s, 68f * s);
                ProtoGui.Fill(br, ProtoGui.Dark);
                string tired = _busyMultiplier > 1.01f ? $"  (피곤해서 {Mathf.RoundToInt((_busyMultiplier - 1f) * 100f)}% 더 걸린다)" : "";
                ProtoGui.Text(new Rect(br.x + 10f * s, br.y + 6f * s, br.width - 20f * s, 24f * s), _busyLabel + tired, 15, Color.white);
                float t = 1f - Mathf.Clamp01((_busyEnd - _clock.Elapsed) / _busyDuration);
                var track = new Rect(br.x + 10f * s, br.y + 38f * s, br.width - 20f * s, 14f * s);
                ProtoGui.Fill(track, new Color(1f, 1f, 1f, 0.15f));
                ProtoGui.Fill(new Rect(track.x, track.y, track.width * t, track.height), ProtoGui.Accent);
            }
        }
    }
}
