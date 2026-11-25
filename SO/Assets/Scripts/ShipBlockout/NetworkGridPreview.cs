using System.Collections.Generic;
using UnityEngine;

namespace SpaceOdyssey.Blockout
{
    /// <summary>
    /// 통로 그물 A1(Docs/ShipLayoutNetworkPlan.md 11절 A1)의 판정용 미리보기.
    /// 프리셋 텍스트와 형판 텍스트를 ShipNetworkRules에 넘기고, 돌려받은 배를 그리기만 한다(파서와 검사는 규칙 클래스에 있음).
    /// 통로와 방칸 바닥, 설비 칸 판, 못 들어가게 막는 보이지 않는 충돌체, 블록별 색조, 접합 변 선, 차단막 띠를 깐다.
    /// 벽, 문 조각, 외피, 단면, 소품, 모듈은 만들지 않는다. B1b에서 형식 2 생성기로 대체되고 지운다.
    /// 컨트롤러(ShipBlockoutController)가 Awake에서 바닥 그룹을 읽으므로 그보다 먼저 실행한다.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class NetworkGridPreview : MonoBehaviour
    {
        [field: Header("데이터")]
        [field: SerializeField]
        [field: Tooltip("프리셋 텍스트(.txt). 줄 문법은 계획서 2.6절")]
        public TextAsset Preset { get; set; }

        [field: SerializeField]
        [field: Tooltip("블록 형판 문자 격자(.txt) 목록. 프리셋이 에셋 이름으로 찾는다(계획서 2.7절)")]
        public TextAsset[] Templates { get; set; }

        [field: Header("그룹 (컨트롤러가 참조)")]
        [field: SerializeField] public Transform Floors { get; set; }
        [field: SerializeField] public Transform Marks { get; set; }
        [field: SerializeField] public Transform StructureParent { get; set; }

        [field: SerializeField]
        [field: Tooltip("외부 궤도 카메라가 도는 중심. 선체 경계 상자 가운데로 옮긴다")]
        public Transform OrbitCenter { get; set; }

        [field: SerializeField]
        [field: Tooltip("프리셋 start 칸으로 옮길 플레이어")]
        public CharacterController Player { get; set; }

        [field: Header("표시")]
        [field: SerializeField]
        [field: Tooltip("바닥과 판 재질의 원본. 색만 바꾼 복제본을 만든다")]
        public Material TintTemplate { get; set; }

        [field: SerializeField]
        [field: Tooltip("설비 칸 판의 윗면 높이(m). 높을수록 통로가 기계 사이에 파인 길로 읽히지만 내부 뷰에서 뒤쪽 통로를 가린다")]
        public float StructureHeight { get; set; } = 0.35f;

        [field: SerializeField]
        [field: Tooltip("문 방칸 앞 통로 바닥에 긋는 줄 색")]
        public Color DoorMarkColor { get; set; } = new(1f, 0.95f, 0.55f);

        [field: SerializeField]
        [field: Tooltip("역할별 설비 칸 판 색. 순서는 StructureRole(Fill, Armor, Tank, Engine). 바닥보다 확실히 어둡게")]
        public Color[] StructureColors { get; set; } =
        {
            new(0.22f, 0.23f, 0.26f), new(0.13f, 0.13f, 0.15f), new(0.12f, 0.24f, 0.27f), new(0.32f, 0.18f, 0.10f)
        };

        [field: SerializeField]
        [field: Tooltip("접합 변을 따라 긋는 선 색")]
        public Color SeamColor { get; set; } = new(1f, 0.25f, 0.85f);

        [field: SerializeField]
        [field: Tooltip("차단막 띠 색. 통로 바닥보다 밝고 눈에 띄게")]
        public Color BarrierColor { get; set; } = new(1f, 0.45f, 0.1f);

        const float RoomHeight = 3f;          // 설비 칸과 우주 쪽을 막는 보이지 않는 충돌체 높이(방 안 높이)
        const float DoorMarkWidth = 0.15f;    // 문 줄 폭(m)
        const float SeamWidth = 0.12f;        // 접합 변 선 폭(m)
        const float BarrierWidth = 0.35f;     // 차단막 띠 폭(m). 변을 가운데에 두고 양쪽 칸으로 반씩
        const float CorridorSaturation = 0.22f, CorridorValue = 0.9f;   // 통로 바닥: 블록 색조를 옅게, 밝게
        const float RoomSaturation = 0.4f, RoomValue = 0.55f;           // 방칸 바닥: 통로보다 탁하고 어둡게
        const float StructureTint = 0.35f;    // 설비 판 역할 색에 블록 색조를 섞는 비율. 높을수록 블록 구분이 강하고 역할 구분이 약함

        static readonly Vector2Int[] Dirs = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };

        readonly List<Material> _materials = new();

        void Awake()
        {
            var templates = new List<(string, string)>();
            for (int i = 0; i < Templates.Length; i++)
            {
                // 인스펙터 연결이 끊긴 칸을 이름 없는 형판으로 넘기지 않고 어느 칸인지 드러낸다
                if (Templates[i] == null) throw new MissingReferenceException($"{name}: Templates {i}번째 칸이 비어 있다");
                templates.Add((Templates[i].name, Templates[i].text));
            }
            var ship = ShipNetworkRules.Build(Preset.name, Preset.text, templates);
            Build(ship);
            Debug.Log(ShipNetworkRules.Describe(ship), this);
            foreach (var w in ship.Warnings) Debug.LogWarning($"[NetworkGridPreview] {w}", this);
        }

        void OnDestroy()
        {
            foreach (var m in _materials) Destroy(m);
        }

        static float Hue(ShipNetworkRules.HullBlock hullBlock) => hullBlock.Index * 0.618034f % 1f;   // 블록마다 황금비 간격의 색상

        void Build(ShipNetworkRules.Ship ship)
        {
            var mats = new Dictionary<(int hullBlock, int kind), Material>();
            Material HullBlockMaterial(ShipNetworkRules.HullBlock hullBlock, Vector2Int c)
            {
                var kind = ship.KindOf[c];
                int key = kind == CellKind.Structure ? 2 + (int)ship.RoleOf[c] : (int)kind;
                if (mats.TryGetValue((hullBlock.Index, key), out var m)) return m;
                float h = Hue(hullBlock);
                var color = kind switch
                {
                    CellKind.Corridor => Color.HSVToRGB(h, CorridorSaturation, CorridorValue),
                    CellKind.Room => Color.HSVToRGB(h, RoomSaturation, RoomValue),
                    _ => Color.Lerp(StructureColors[(int)ship.RoleOf[c]], Color.HSVToRGB(h, 0.6f, 0.35f), StructureTint),
                };
                return mats[(hullBlock.Index, key)] = MakeMaterial($"{hullBlock.Id}_{key}", color);
            }

            var all = new List<Vector2Int>(ship.KindOf.Keys);
            all.Sort(ShipNetworkRules.CompareCells);
            var plates = new GameObject("StructurePlates").transform;
            plates.SetParent(StructureParent, false);
            foreach (var c in all)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.GetComponent<Renderer>().sharedMaterial = HullBlockMaterial(ship.OwnerOf[c], c);
                if (ship.KindOf[c] != CellKind.Structure)
                {
                    // 컨트롤러는 이 그룹 자식의 월드 위치를 반내림해서 칸을 읽는다. 생성기(ShipGenerator)와 같은 모양과 위치 규칙
                    go.name = "Floor";
                    go.transform.SetParent(Floors, false);
                    go.transform.localPosition = new Vector3(c.x + 0.5f, -0.05f, c.y + 0.5f);
                    go.transform.localScale = new Vector3(1f, 0.1f, 1f);
                }
                else
                {
                    // 보이는 판은 낮지만 충돌체는 방 높이까지 세워 걸어 들어가지 못하게 함(계획서 8.4절 A0 행)
                    go.name = $"Structure({c.x},{c.y})";
                    go.transform.SetParent(plates, false);
                    go.transform.localPosition = new Vector3(c.x + 0.5f, StructureHeight * 0.5f, c.y + 0.5f);
                    go.transform.localScale = new Vector3(1f, StructureHeight, 1f);
                    var box = go.GetComponent<BoxCollider>();
                    box.size = new Vector3(1f, RoomHeight / StructureHeight, 1f);
                    box.center = new Vector3(0f, (RoomHeight * 0.5f - StructureHeight * 0.5f) / StructureHeight, 0f);
                }
            }

            var markMat = MakeMaterial("DoorMark", DoorMarkColor);
            foreach (var (door, front) in ship.Doors)
            {
                var d = front - door;
                var go = MakeMark("DoorMark", markMat);
                // 문 변에서 통로 쪽으로 줄 폭 절반만큼 들어간 자리
                go.transform.localPosition = new Vector3(door.x + 0.5f + d.x * (0.5f + DoorMarkWidth * 0.5f), 0.01f, door.y + 0.5f + d.y * (0.5f + DoorMarkWidth * 0.5f));
                go.transform.localScale = d.x != 0 ? new Vector3(DoorMarkWidth, 0.02f, 0.9f) : new Vector3(0.9f, 0.02f, DoorMarkWidth);
            }

            // 접합 변 선: 두 칸 중 높은 윗면 위에 변을 따라 긋는다(설비 판 옆이면 판 윗면 높이)
            var seamMat = MakeMaterial("Seam", SeamColor);
            foreach (var joint in ship.Joints.Values)
                foreach (var e in joint.Edges)
                {
                    float top = Mathf.Max(Top(ship, e.CellA), Top(ship, e.CellB));
                    var go = MakeMark($"Seam({joint.Id})", seamMat);
                    go.transform.localPosition = e.Center + Vector3.up * (top + 0.02f);
                    go.transform.localScale = e.axis == 0 ? new Vector3(SeamWidth, 0.03f, 1f) : new Vector3(1f, 0.03f, SeamWidth);
                }

            // 차단막 띠: 접합부 통로↔통로 변과 통로 중간 차단막 글자 자리. 통로를 가로지르는 밝은 띠
            var barrierMat = MakeMaterial("Barrier", BarrierColor);
            var barriers = new List<EdgeKey>(ship.InnerBarriers);
            foreach (var joint in ship.Joints.Values)
                foreach (var (e, state) in joint.States)
                    if (state == SeamEdge.Barrier) barriers.Add(e);
            foreach (var e in barriers)
            {
                var go = MakeMark("Barrier", barrierMat);
                go.transform.localPosition = e.Center + Vector3.up * 0.04f;
                go.transform.localScale = e.axis == 0 ? new Vector3(BarrierWidth, 0.05f, 1f) : new Vector3(1f, 0.05f, BarrierWidth);
            }

            // 외피가 없으므로 우주와 맞닿은 가압 칸 변(창 면, 에어락)으로 걸어 나가지 못하게 우주 칸에 충돌체만 둔다
            var blockers = new GameObject("SpaceBlockers").transform;
            blockers.SetParent(StructureParent, false);
            var spaceCells = new SortedSet<Vector2Int>(Comparer<Vector2Int>.Create(ShipNetworkRules.CompareCells));
            foreach (var c in all)
                if (ship.IsPressurized(c))
                    foreach (var d in Dirs)
                        if (!ship.OwnerOf.ContainsKey(c + d)) spaceCells.Add(c + d);
            foreach (var c in spaceCells)
            {
                var go = new GameObject($"SpaceBlocker({c.x},{c.y})");
                go.transform.SetParent(blockers, false);
                go.transform.localPosition = new Vector3(c.x + 0.5f, RoomHeight * 0.5f, c.y + 0.5f);
                go.AddComponent<BoxCollider>().size = new Vector3(1f, RoomHeight, 1f);
            }

            int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
            foreach (var c in all)
            {
                minX = Mathf.Min(minX, c.x); maxX = Mathf.Max(maxX, c.x);
                minZ = Mathf.Min(minZ, c.y); maxZ = Mathf.Max(maxZ, c.y);
            }
            OrbitCenter.position = transform.TransformPoint(new Vector3((minX + maxX + 1) * 0.5f, 1.5f, (minZ + maxZ + 1) * 0.5f));

            // 컨트롤러 Awake가 플레이어 위치로 카메라 리그를 맞추므로 그 전에 옮긴다.
            // 캐릭터 컨트롤러는 물리 쪽 위치를 따로 들고 있어서 변환만 바꾸면 다음 동기화 전까지 옛 위치에 남는다
            Player.transform.position = transform.TransformPoint(new Vector3(ship.Start.x + 0.5f, 0f, ship.Start.y + 0.5f));
            Physics.SyncTransforms();
        }

        float Top(ShipNetworkRules.Ship ship, Vector2Int c) => ship.KindOf[c] == CellKind.Structure ? StructureHeight : 0f;

        // 표시용 납작한 상자. 충돌체는 지운다(걸음에 걸리면 안 됨)
        GameObject MakeMark(string markName, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = markName;
            DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(Marks, false);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        Material MakeMaterial(string materialName, Color color)
        {
            var m = new Material(TintTemplate) { name = $"Preview_{materialName}" };
            m.SetColor("_BaseColor", color);
            _materials.Add(m);
            return m;
        }
    }
}
