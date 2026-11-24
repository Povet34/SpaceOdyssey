using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceOdyssey.Blockout
{
    /// <summary>
    /// 구조 칸을 채우는 소품 풀(계획서 7.2절). 소품마다 차지 칸 규격, 층(낮음/높음), 어울리는 역할, 가중치를 갖는다.
    /// 크기와 중심은 풀을 만들 때 프리팹에서 재어 적어 둔다(실행 중에 메시 경계를 다시 재지 않기 위함).
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceOdyssey/Blockout/Greeble Pool")]
    public class ShipGreeblePool : ScriptableObject
    {
        /// <summary>Low는 카메라 쪽(낮게 자른) 칸에서도 보이는 납작한 소품, Tall은 반대쪽(높게 자른) 칸에서만 보이는 큰 소품.</summary>
        public enum Layer { Low, Tall }

        [Serializable]
        public class Entry
        {
            public GameObject prefab;
            public Layer layer;

            [Tooltip("차지하는 칸 규격. 1이면 1×1칸, 2면 2×2칸")]
            [Range(1, 2)]
            public int footprint = 1;

            public List<StructureRole> roles = new();

            [Min(0f)]
            public float weight = 1f;

            [Tooltip("프리팹 루트 기준 경계 상자 크기(m). 풀을 만들 때 잰 값")]
            public Vector3 size;

            [Tooltip("프리팹 루트 기준 경계 상자 중심(m). 칸 가운데에 맞출 때 쓴다")]
            public Vector3 center;
        }

        [field: SerializeField]
        public List<Entry> Entries { get; set; } = new();

        [field: SerializeField]
        [field: Tooltip("Low 소품의 최대 높이(m). 더 크면 줄여서 넣는다. 카메라 쪽 칸이 가장자리 방을 가리는 정도를 정함")]
        public float LowMaxHeight { get; set; } = 0.45f;

        [field: SerializeField]
        [field: Tooltip("Tall 소품의 최대 높이(m). 반대쪽 칸의 부피감을 정함")]
        public float TallMaxHeight { get; set; } = 3.2f;

        [field: SerializeField]
        [field: Tooltip("역할별로 2×2 묶음에 Tall 소품을 올릴 확률. 순서는 StructureRole(Fill, Armor, Tank, Engine)")]
        public float[] TallChanceByRole { get; set; } = { 0.7f, 0.5f, 0.95f, 0.9f };

        /// <summary>층, 규격, 역할에 맞는 후보 중 가중치로 하나를 고른다. 후보가 없으면 풀 데이터가 모자란 것이므로 예외를 던진다.</summary>
        public Entry Pick(Layer layer, int footprint, StructureRole role, float random01)
        {
            float total = 0f;
            foreach (var e in Entries)
                if (Matches(e, layer, footprint, role)) total += e.weight;
            if (total <= 0f)
                throw new InvalidOperationException($"소품 풀 '{name}'에 {layer}/{footprint}칸/{role} 후보가 없다");

            float pick = random01 * total;
            Entry last = null;
            foreach (var e in Entries)
            {
                if (!Matches(e, layer, footprint, role)) continue;
                last = e;
                pick -= e.weight;
                if (pick < 0f) return e;
            }
            return last;   // random01이 1에 아주 가까울 때 부동소수 오차로 끝까지 온 경우
        }

        static bool Matches(Entry e, Layer layer, int footprint, StructureRole role) =>
            e.layer == layer && e.footprint == footprint && e.weight > 0f && e.roles.Contains(role);
    }
}
