using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceOdyssey.Blockout
{
    /// <summary>
    /// 구조층 표현(계획서 5절, 7절). 구조 칸마다 어두운 단면 상자를 놓고, 2×2칸 고정 묶음마다 시드로 소품을 고른다.
    /// 내부 뷰에서는 카메라 쪽 칸을 낮게, 반대쪽 칸을 높게 잘라 보이고(상자 높이 바꾸기 방식, 결정 7), 뚜껑을 닫으면 통째로 끈다.
    /// 생성기(ShipGenerator)가 Build를 부르고, 컨트롤러의 ViewChanged에 맞춰 높이를 다시 정한다.
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public class ShipStructureView : MonoBehaviour
    {
        [field: SerializeField]
        public ShipBlockoutController Controller { get; set; }

        [field: SerializeField]
        public ShipGreeblePool Pool { get; set; }

        [field: SerializeField]
        [field: Tooltip("단면 상자 재질의 원본. 역할마다 색만 바꾼 복제본을 만든다")]
        public Material SectionMaterial { get; set; }

        [field: SerializeField]
        [field: Tooltip("역할별 단면 색. 순서는 StructureRole(Fill, Armor, Tank, Engine). 방 바닥보다 확실히 어둡게")]
        public Color[] SectionColors { get; set; } =
        {
            new(0.20f, 0.21f, 0.24f), new(0.16f, 0.16f, 0.18f), new(0.14f, 0.22f, 0.24f), new(0.26f, 0.18f, 0.13f)
        };

        [field: SerializeField]
        [field: Tooltip("단면 자체 발광 세기(단면 색에 곱함). 구조층은 방 조명을 받지 않아서, 이 값이 0이면 우주 배경과 구분되지 않는다")]
        [field: Range(0f, 1f)]
        public float SectionGlow { get; set; } = 0.12f;

        [field: SerializeField]
        [field: Tooltip("카메라 쪽 칸의 단면 높이(m). 낮을수록 가장자리 방을 덜 가리지만 납작해진다")]
        public float NearCutHeight { get; set; } = 0.15f;

        [field: SerializeField]
        [field: Tooltip("반대쪽 칸의 단면 높이(m). 높을수록 벽 뒤에 기계가 쌓인 부피감이 커진다")]
        public float FarCutHeight { get; set; } = 1.4f;

        [field: SerializeField]
        [field: Tooltip("구조층 렌더러와 구조층 조명이 쓰는 렌더링 레이어 번호. 방 조명은 이 레이어를 비추지 않는다")]
        public int RenderingLayer { get; set; } = 1;

        [field: SerializeField]
        [field: Tooltip("4×4칸 묶음마다 경고등을 둘 확률")]
        [field: Range(0f, 1f)]
        public float WarningLightChance { get; set; } = 0.35f;

        [field: SerializeField]
        public Color WarningLightColor { get; set; } = new(1f, 0.45f, 0.12f);

        class CellView
        {
            public Transform box;
            public Vector2 toHabitable;   // 가장 가까운 거주 칸 쪽 방향
            public bool near;
        }

        class GreebleView
        {
            public GameObject go;
            public ShipGreeblePool.Layer layer;
            public int[] cells;           // 덮는 구조 칸(CellView 인덱스)
            public GreebleView partner;   // 같은 자리의 다른 층 소품(Low ↔ Tall)
            public float baseOffsetY;     // 단면 윗면에서 소품 피벗까지
        }

        readonly List<CellView> _cells = new();
        readonly List<GreebleView> _greebles = new();
        Transform _root;

        /// <summary>마지막 Build의 소품 수와 삼각형 수(측정 보고용).</summary>
        public int GreebleCount => _greebles.Count;
        public int GreebleTriangles { get; private set; }

        void OnEnable() => Controller.ViewChanged += Controller_ViewChanged;
        void OnDisable() => Controller.ViewChanged -= Controller_ViewChanged;

        void Controller_ViewChanged() => Apply(Controller.LidClosed, Controller.ViewYaw);

        public void Build(ShipLayout layout, HashSet<Vector2Int> habitable, Transform parent)
        {
            _cells.Clear();
            _greebles.Clear();
            GreebleTriangles = 0;
            _root = new GameObject("Structure").transform;
            _root.SetParent(parent, false);
            var blocks = new GameObject("Blocks").transform;
            blocks.SetParent(_root, false);
            var props = new GameObject("Greebles").transform;
            props.SetParent(_root, false);
            var lights = new GameObject("WarningLights").transform;
            lights.SetParent(_root, false);

            uint layerMask = 1u << RenderingLayer;
            var sectionMats = new Material[SectionColors.Length];
            for (int i = 0; i < sectionMats.Length; i++)
            {
                sectionMats[i] = new Material(SectionMaterial) { name = $"Section_{(StructureRole)i}" };
                sectionMats[i].SetColor("_BaseColor", SectionColors[i]);
                sectionMats[i].EnableKeyword("_EMISSION");
                sectionMats[i].SetColor("_EmissionColor", SectionColors[i] * SectionGlow);
            }

            var habList = new List<Vector2Int>(habitable);
            habList.Sort(ShipLayout.CompareCells);

            var indexOf = new Dictionary<Vector2Int, int>();
            var roleOf = new Dictionary<Vector2Int, StructureRole>();
            foreach (var s in layout.StructureCells)
            {
                var c = s.cell;
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"Section({c.x},{c.y})";
                Destroy(go.GetComponent<Collider>());   // 구조층은 걷는 곳이 아니라 보기용. 막는 것은 거주 칸 경계 벽이다
                go.transform.SetParent(blocks, false);
                go.transform.position = new Vector3(c.x + 0.5f, 0f, c.y + 0.5f);   // 높이와 세로 위치는 Apply가 정한다
                var r = go.GetComponent<Renderer>();
                r.sharedMaterial = sectionMats[(int)s.role];
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.renderingLayerMask = layerMask;

                indexOf[c] = _cells.Count;
                roleOf[c] = s.role;
                _cells.Add(new CellView { box = go.transform, toHabitable = NearestDirection(c, habList) });
            }

            PlaceGreebles(layout.Seed, indexOf, roleOf, props, layerMask);
            PlaceWarningLights(layout.Seed, indexOf, lights);
        }

        // 가장 가까운 거주 칸 쪽 방향. 같은 거리면 정렬 순서상 앞선 칸을 쓴다(결정론)
        static Vector2 NearestDirection(Vector2Int c, List<Vector2Int> habSorted)
        {
            int best = int.MaxValue;
            Vector2Int bestCell = c;
            foreach (var h in habSorted)
            {
                int d = (h - c).sqrMagnitude;
                if (d < best) { best = d; bestCell = h; }
            }
            return ((Vector2)(bestCell - c)).normalized;
        }

        void PlaceGreebles(int seed, Dictionary<Vector2Int, int> indexOf, Dictionary<Vector2Int, StructureRole> roleOf, Transform parent, uint layerMask)
        {
            // 2×2칸 고정 격자로 묶는다. 묶음 경계가 고정이라 거주 칸이 바뀌어도 바뀐 근처 묶음만 달라진다
            var blocks = new SortedDictionary<Vector2Int, List<Vector2Int>>(Comparer<Vector2Int>.Create(ShipLayout.CompareCells));
            foreach (var c in indexOf.Keys)
            {
                var key = new Vector2Int(FloorDiv(c.x, 2), FloorDiv(c.y, 2));
                if (!blocks.TryGetValue(key, out var list)) blocks[key] = list = new List<Vector2Int>();
                list.Add(c);
            }

            foreach (var (key, cells) in blocks)
            {
                cells.Sort(ShipLayout.CompareCells);
                if (cells.Count == 4)
                {
                    var role = DominantRole(cells, roleOf);
                    var slot = new Vector3(key.x * 2 + 1f, 0f, key.y * 2 + 1f);
                    int[] idx = { indexOf[cells[0]], indexOf[cells[1]], indexOf[cells[2]], indexOf[cells[3]] };
                    PlaceSlot(seed, key, 0, slot, 2, role, idx, parent, layerMask);
                }
                else
                {
                    for (int i = 0; i < cells.Count; i++)
                    {
                        var c = cells[i];
                        var slot = new Vector3(c.x + 0.5f, 0f, c.y + 0.5f);
                        PlaceSlot(seed, c, 1, slot, 1, roleOf[c], new[] { indexOf[c] }, parent, layerMask);
                    }
                }
            }
        }

        void PlaceSlot(int seed, Vector2Int key, int salt, Vector3 slot, int footprint, StructureRole role, int[] cells, Transform parent, uint layerMask)
        {
            var low = Spawn(Pool.Pick(ShipGreeblePool.Layer.Low, footprint, role, Rand01(seed, key, salt * 16 + 1)),
                ShipGreeblePool.Layer.Low, slot, footprint, Hash(seed, key, salt * 16 + 2), cells, parent, layerMask);

            if (Rand01(seed, key, salt * 16 + 3) >= Pool.TallChanceByRole[(int)role]) return;
            var tall = Spawn(Pool.Pick(ShipGreeblePool.Layer.Tall, footprint, role, Rand01(seed, key, salt * 16 + 4)),
                ShipGreeblePool.Layer.Tall, slot, footprint, Hash(seed, key, salt * 16 + 5), cells, parent, layerMask);
            low.partner = tall;
            tall.partner = low;
        }

        GreebleView Spawn(ShipGreeblePool.Entry e, ShipGreeblePool.Layer layer, Vector3 slot, int footprint, uint rotHash, int[] cells, Transform parent, uint layerMask)
        {
            float maxH = layer == ShipGreeblePool.Layer.Low ? Pool.LowMaxHeight : Pool.TallMaxHeight;
            float s = Mathf.Min(footprint * 0.92f / Mathf.Max(e.size.x, e.size.z), maxH / e.size.y, 1.5f);
            var rot = Quaternion.Euler(0f, 90f * (rotHash % 4), 0f);
            var go = Instantiate(e.prefab, parent);
            go.name = $"{e.prefab.name}({slot.x:0.#},{slot.z:0.#})";
            go.transform.localScale = Vector3.one * s;
            go.transform.rotation = rot;
            var flatCenter = rot * new Vector3(e.center.x, 0f, e.center.z) * s;
            go.transform.position = slot - flatCenter;

            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.renderingLayerMask = layerMask;
            }
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                GreebleTriangles += mf.sharedMesh.triangles.Length / 3;

            var view = new GreebleView { go = go, layer = layer, cells = cells, baseOffsetY = -e.center.y * s + e.size.y * s * 0.5f };
            _greebles.Add(view);
            return view;
        }

        void PlaceWarningLights(int seed, Dictionary<Vector2Int, int> indexOf, Transform parent)
        {
            var groups = new SortedSet<Vector2Int>(Comparer<Vector2Int>.Create(ShipLayout.CompareCells));
            foreach (var c in indexOf.Keys) groups.Add(new Vector2Int(FloorDiv(c.x, 4), FloorDiv(c.y, 4)));
            foreach (var g in groups)
            {
                var center = new Vector2Int(g.x * 4 + 2, g.y * 4 + 2);
                if (!indexOf.ContainsKey(center) || Rand01(seed, g, 900) >= WarningLightChance) continue;
                var go = new GameObject($"WarningLight({center.x},{center.y})");
                go.transform.SetParent(parent, false);
                go.transform.position = new Vector3(center.x + 0.5f, 2.2f, center.y + 0.5f);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = 4f;
                l.intensity = 3f;
                l.color = WarningLightColor;
                l.shadows = LightShadows.None;
                l.renderingLayerMask = 1 << RenderingLayer;
            }
        }

        /// <summary>뚜껑 상태와 화면 방향에 맞춰 단면 높이와 소품 표시를 다시 정한다.</summary>
        public void Apply(bool lidClosed, float viewYaw)
        {
            _root.gameObject.SetActive(!lidClosed);   // 닫으면 외피가 덮으므로 그리는 비용과 그림자 비용을 아낀다
            if (lidClosed) return;

            Vector3 f = Quaternion.Euler(0f, viewYaw, 0f) * Vector3.forward;
            var camForward = new Vector2(f.x, f.z);
            foreach (var cv in _cells)
            {
                // 거주 칸이 이 칸보다 카메라 시선 방향 앞쪽에 있으면 이 칸은 카메라와 방 사이(카메라 쪽)
                cv.near = Vector2.Dot(cv.toHabitable, camForward) >= 0f;
                float h = cv.near ? NearCutHeight : FarCutHeight;
                var p = cv.box.position;
                cv.box.localScale = new Vector3(1f, h, 1f);
                cv.box.position = new Vector3(p.x, h * 0.5f, p.z);
            }

            foreach (var g in _greebles)
            {
                bool near = false;
                float top = 0f;
                foreach (int i in g.cells)
                {
                    near |= _cells[i].near;
                    top = Mathf.Max(top, _cells[i].near ? NearCutHeight : FarCutHeight);
                }
                // 카메라 쪽: 낮은 소품만. 반대쪽: 큰 소품이 있으면 그것만, 없으면 낮은 소품
                bool visible = g.layer == ShipGreeblePool.Layer.Low ? near || g.partner == null : !near;
                g.go.SetActive(visible);
                var pos = g.go.transform.position;
                g.go.transform.position = new Vector3(pos.x, top + g.baseOffsetY, pos.z);
            }
        }

        static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

        static uint Hash(int seed, Vector2Int c, int salt)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u;
                h ^= (uint)c.x * 0x85EBCA77u;
                h = (h << 13) | (h >> 19);
                h ^= (uint)c.y * 0xC2B2AE3Du;
                h ^= (uint)salt * 0x27D4EB2Fu;
                h ^= h >> 16; h *= 0x7FEB352Du;
                h ^= h >> 15; h *= 0x846CA68Bu;
                h ^= h >> 16;
                return h;
            }
        }

        static float Rand01(int seed, Vector2Int c, int salt) => (Hash(seed, c, salt) & 0xFFFFFF) / 16777216f;

        static StructureRole DominantRole(List<Vector2Int> cells, Dictionary<Vector2Int, StructureRole> roleOf)
        {
            var count = new int[4];
            foreach (var c in cells) count[(int)roleOf[c]]++;
            int best = 0;
            for (int i = 1; i < count.Length; i++)
                if (count[i] >= count[best]) best = i;   // 같으면 우선순위 높은(뒤쪽) 역할
            return (StructureRole)best;
        }
    }
}
