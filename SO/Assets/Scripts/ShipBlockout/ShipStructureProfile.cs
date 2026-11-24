using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceOdyssey.Blockout
{
    /// <summary>
    /// 구조층 자동 채움의 노브와 윤곽 형판(계획서 3.6절). 일부러 과하게 잡은 값에서 시작해 사용자 판정으로 줄여 간다(결정 8).
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceOdyssey/Blockout/Structure Profile")]
    public class ShipStructureProfile : ScriptableObject
    {
        /// <summary>기본 띠의 거리 재는 방식. 가로세로 합이면 모서리가 비스듬히 깎이고, 큰 쪽이면 네모난 모서리가 된다.</summary>
        public enum BandMetric { Chamfered, Square }

        /// <summary>형판의 기준점. 뒤/앞은 배 중심선에서 옆으로 offset만큼, 옆은 거주 칸 맨 뒤에서 앞으로 offset만큼 떨어진 곳.</summary>
        public enum StampAnchor { Rear, Front, Side }

        /// <summary>바깥으로 나가며 폭이 어떻게 변하는가. Rect는 그대로, Taper는 끝 폭까지 줄어듦, Dome은 반타원.</summary>
        public enum StampShape { Rect, Taper, Dome }

        [Serializable]
        public class Stamp
        {
            public string name;
            public bool enabled = true;
            public StampAnchor anchor;
            public StructureRole role;
            public StampShape shape;

            [Tooltip("Rear/Front: 중심선에서 옆으로 떨어진 거리(칸, + 쪽이 좌현). Side: 거주 칸 맨 뒤에서 앞으로 떨어진 거리(칸)")]
            public float offset;

            [Tooltip("기준 가장자리에서 바깥으로 튀어나오는 길이(칸)")]
            [Min(1)]
            public int length = 4;

            [Tooltip("기준 가장자리 쪽 폭(칸)")]
            [Min(1)]
            public int width = 4;

            [Tooltip("Taper일 때 끝 폭(칸)")]
            [Min(1)]
            public int tipWidth = 1;

            [Tooltip("Side일 때 바깥으로 갈수록 뒤로 밀리는 양(칸). 뒤로 젖힌 지느러미 모양")]
            public int sweep;

            [Tooltip("기준 가장자리 안쪽으로 겹쳐 들어가는 깊이(칸). 몸통과 이음매 없이 붙게 한다")]
            [Min(0)]
            public int overlap = 1;
        }

        [field: SerializeField]
        [field: Tooltip("거주 칸 둘레 기본 띠의 두께(칸). 두꺼울수록 방 바깥 기계 구역이 넓어진다")]
        [field: Min(0)]
        public int BandThickness { get; set; } = 4;

        [field: SerializeField]
        public BandMetric Metric { get; set; } = BandMetric.Chamfered;

        [field: SerializeField]
        [field: Tooltip("움푹한 곳 메우기 반경(칸). 이 값의 두 배 폭까지의 홈이 메워진다. 윤곽을 방 모양에서 떼어 내는 핵심 노브")]
        [field: Min(0)]
        public int CloseRadius { get; set; } = 3;

        [field: SerializeField]
        [field: Tooltip("창 면 앞으로 구조 칸을 비워 두는 깊이(칸). 띠 두께+메우기 반경보다 커야 창 앞이 완전히 열린다")]
        [field: Min(0)]
        public int WindowClearDepth { get; set; } = 16;

        [field: SerializeField]
        [field: Tooltip("배 길이 방향 중심선 기준 좌우 대칭. 창 면 예외도 같이 대칭으로 적용된다")]
        public bool Symmetry { get; set; } = true;

        [field: SerializeField]
        public List<Stamp> Stamps { get; set; } = new();
    }
}
