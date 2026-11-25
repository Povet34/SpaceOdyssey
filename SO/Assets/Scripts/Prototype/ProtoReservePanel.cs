using UnityEngine;
using UnityEngine.InputSystem;

namespace Prototype
{
    /// <summary>
    /// 임시 예약 패널(월드에 놓지 않음). 항목은 "함교 장거리 스캐너 설치" 하나뿐이다.
    /// 진짜 배치 패널은 설계가 아직 없어서(ShipBuildDesign.md 0.5절) 버튼 하나로 대신한다.
    /// </summary>
    public class ProtoReservePanel : MonoBehaviour
    {
        ProtoSeedState _seed;
        ProtoFatigue _fatigue;

        public bool Open { get; private set; }

        void Awake()
        {
            _seed = GetComponent<ProtoSeedState>();
            _fatigue = GetComponent<ProtoFatigue>();
        }

        void Update()
        {
            // 키보드가 없는 환경(장치 분리)에서는 키 입력을 받을 수 없어 건너뛴다. 패널은 마우스 버튼으로는 계속 쓸 수 있다.
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.rKey.wasPressedThisFrame)
            {
                if (Open) Close(true);
                else if (_seed.Phase == ProtoPhase.Play && !_fatigue.Sleeping && !_seed.Busy)
                {
                    Open = true;
                    _seed.SetWalk(false);
                }
            }

            if (Open && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) Reserve();
        }

        public void Close(bool restoreWalk)
        {
            if (!Open) return;
            Open = false;
            if (restoreWalk) _seed.SetWalk(true);
        }

        void Reserve()
        {
            if (_seed.Path == ProtoPath.Scanner) return;
            _seed.ReserveScanner();
        }

        void OnGUI()
        {
            if (!Open) return;
            GUI.depth = -20;
            float s = ProtoGui.S;
            float w = 520f * s;
            float h = 270f * s;
            var r = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            ProtoGui.Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.35f));
            ProtoGui.Fill(r, new Color(0.05f, 0.07f, 0.1f, 0.94f));
            float pad = 16f * s;
            float x = r.x + pad;
            float iw = w - pad * 2f;

            ProtoGui.Text(new Rect(x, r.y + pad, iw, 28f * s), "예약 패널 (임시)", 20, new Color(0.6f, 0.9f, 1f));
            ProtoGui.Text(new Rect(x, r.y + pad + 34f * s, iw, 26f * s), "장거리 스캐너 설치 (함교)", 18, Color.white);

            string status;
            if (_seed.Path == ProtoPath.Scanner)
            {
                status = _seed.Installed
                    ? "설치됨. 함교 조종석 근처에서 내가 스캔한다."
                    : $"드론 배정됨, 설치 중 {Mathf.FloorToInt(_seed.InstallProgress * 100f)}%";
            }
            else
            {
                status = "설치 주체는 드론. 예약하면 자는 동안에도 설치는 끝난다.";
                if (_seed.Path == ProtoPath.Instrument) status += "\n길은 하나만 고른다. 예약하면 소지품 길이 취소된다.";
            }
            ProtoGui.Text(new Rect(x, r.y + pad + 66f * s, iw, 80f * s), status, 15, ProtoGui.Dim, TextAnchor.UpperLeft, true);

            if (_seed.Path == ProtoPath.Scanner)
            {
                if (ProtoGui.Button(new Rect(x, r.y + 160f * s, iw, 44f * s), "예약 취소", 18)) _seed.CancelScannerReservation();
            }
            else
            {
                if (ProtoGui.Button(new Rect(x, r.y + 160f * s, iw, 44f * s), "예약  [Enter]", 18)) Reserve();
            }
            ProtoGui.Text(new Rect(x, r.y + h - 38f * s, iw, 26f * s), "[R] 닫기", 14, ProtoGui.Accent);
        }
    }
}
