using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceOdyssey.Blockout
{
    /// <summary>
    /// 배치 데이터 한 덩어리(ShipLayout)에서 배 전체를 만드는 생성기(설계 문서 0.5절, 계획서 4절).
    /// 바닥, 방 경계 벽과 문, 가구, 방 조명, 외피(거주 칸 + 구조 칸 전체를 감쌈), 형판 부품(엔진, 뱃머리, 용골, 덮개)을 만들고
    /// 구조층은 ShipStructureView에 맡긴다. 씬에는 빈 그룹만 저장하고 Play 시작 때 매번 만든다.
    /// 컨트롤러(ShipBlockoutController)가 Awake에서 바닥과 벽을 읽으므로 그보다 먼저 실행한다.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class ShipGenerator : MonoBehaviour
    {
        [field: Header("데이터")]
        [field: SerializeField]
        public ShipLayout Layout { get; set; }

        [field: SerializeField]
        [field: Tooltip("구조 칸 다시 채우기(컴포넌트 메뉴)에서만 쓴다. 생성은 Layout에 저장된 구조 칸 목록을 그대로 읽는다")]
        public ShipStructureProfile Profile { get; set; }

        [field: Header("그룹 (컨트롤러가 참조)")]
        [field: SerializeField] public Transform Floors { get; set; }
        [field: SerializeField] public Transform InnerWalls { get; set; }
        [field: SerializeField] public Transform Interior { get; set; }
        [field: SerializeField] public Transform RoomLights { get; set; }
        [field: SerializeField] public Transform Exterior { get; set; }
        [field: SerializeField] public Transform StructureParent { get; set; }
        [field: SerializeField] public ShipStructureView StructureView { get; set; }

        [field: SerializeField]
        [field: Tooltip("외부 궤도 카메라가 도는 중심. 선체 경계 상자 가운데로 옮긴다")]
        public Transform OrbitCenter { get; set; }

        [field: Header("외피 조각 키트")]
        [field: SerializeField] public GameObject Wall { get; set; }
        [field: SerializeField] public GameObject WallWindow { get; set; }
        [field: SerializeField] public GameObject CornerOut { get; set; }
        [field: SerializeField] public GameObject Roof { get; set; }
        [field: SerializeField] public GameObject Belly { get; set; }

        [field: Header("형판 부품")]
        [field: SerializeField] public GameObject Engine { get; set; }
        [field: SerializeField] public GameObject Prow { get; set; }
        [field: SerializeField] public GameObject BridgeBrow { get; set; }
        [field: SerializeField] public GameObject Keel { get; set; }

        [field: SerializeField]
        [field: Tooltip("엔진 포드 덮개로 늘려 쓰는 덮개 조각")]
        public GameObject PodCap { get; set; }

        [field: SerializeField]
        [field: Tooltip("탱크 돔에 쓰는 재질(외피 재질)")]
        public Material DomeMaterial { get; set; }

        [field: SerializeField]
        [field: Tooltip("엔진 포드 덮개 높이(m). 지붕 위로 솟는 정도")]
        public float PodCapHeight { get; set; } = 1.4f;

        [field: SerializeField]
        [field: Tooltip("탱크 돔 높이(m)")]
        public float TankDomeHeight { get; set; } = 2.2f;

        [field: SerializeField]
        [field: Tooltip("등줄기 덮개 높이(m). 0이면 만들지 않는다")]
        public float SpineHeight { get; set; } = 0.9f;

        [field: SerializeField]
        [field: Tooltip("등줄기 폭(m)")]
        public float SpineWidth { get; set; } = 3f;

        [field: SerializeField]
        [field: Tooltip("등줄기가 뱃머리 앞끝에서 멈추는 거리(m). 함교 위를 비워 둔다")]
        public float SpineFrontGap { get; set; } = 6f;

        [field: Header("실내 재질")]
        [field: SerializeField] public Material InnerWallMaterial { get; set; }
        [field: SerializeField] public Material DoorFrameMaterial { get; set; }

        [field: SerializeField]
        [field: Tooltip("바닥과 가구 재질의 원본. 색만 바꾼 복제본을 만든다")]
        public Material TintTemplate { get; set; }

        [field: SerializeField]
        [field: Tooltip("방 조명이 비추는 렌더링 레이어 마스크. 구조층 레이어를 빼서 방 빛이 기계 구역으로 새지 않게 한다")]
        public int RoomLightLayerMask { get; set; } = 1;

        const float RoomHeight = 3f;
        const float RoomLightIntensity = 2.5f;  // 확대판 시안의 방 조명과 같은 밝기
        const float WallThickness = 0.1f;
        const float KeelMeshLength = 26f;       // Hull_Keel 메시의 길이(로컬 x 0~26)
        const float CapMeshWidth = 6.7f;        // 덮개 메시(Hull_RoofCap_DroneHangar) 가로
        const float CapMeshDepth = 4.2f;        // 덮개 메시 세로
        const float CapMeshThickness = 0.8f;    // 덮개 메시 두께(지붕 3.5m 위)
        const float RoofTop = 3.5f;

        static readonly Vector2Int[] Dirs = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };

        readonly Dictionary<(Color, bool), Material> _tints = new();

        /// <summary>마지막 생성에서 만든 외피 조각 수(측정 보고용).</summary>
        public int HullPieceCount { get; private set; }

        void Awake() => Generate();

        public void Generate()
        {
            foreach (var g in new[] { Floors, InnerWalls, Interior, RoomLights, Exterior, StructureParent }) ClearChildren(g);
            _tints.Clear();
            HullPieceCount = 0;

            var roomOf = Layout.RoomIndexByCell();
            var hab = new HashSet<Vector2Int>(roomOf.Keys);
            var habSorted = new List<Vector2Int>(hab);
            habSorted.Sort(ShipLayout.CompareCells);

            var hull = new HashSet<Vector2Int>(hab);
            var roleOf = new Dictionary<Vector2Int, StructureRole>();
            foreach (var s in Layout.StructureCells)
            {
                if (hab.Contains(s.cell))
                    throw new System.InvalidOperationException($"구조 칸 {s.cell}이 거주 칸과 겹친다. 구조 칸을 다시 채워야 한다");
                hull.Add(s.cell);
                roleOf[s.cell] = s.role;
            }
            if (Layout.StructureCells.Count == 0)
                Debug.LogError($"[ShipGenerator] '{Layout.name}'에 구조 칸이 없다. 컴포넌트 메뉴의 Refill Structure Cells로 채워야 한다", this);

            var windowFaces = new HashSet<(Vector2Int, Vector2Int)>(Layout.WindowFaces());

            BuildFloors(habSorted, roomOf);
            BuildInnerWalls(habSorted, roomOf, hull, windowFaces);
            BuildFurniture();
            BuildRoomLights();
            BuildHull(hull, windowFaces);
            BuildParts(hull, hab, roleOf, roomOf);
            StructureView.Build(Layout, hab, StructureParent);

            OrbitCenter.position = HullCenter(hull);
        }

        // 바로 아래 컨트롤러 Awake가 자식을 읽으므로 Destroy(프레임 끝에 지움)가 아니라 즉시 지운다
        static void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) DestroyImmediate(t.GetChild(i).gameObject);
        }

        void BuildFloors(List<Vector2Int> habSorted, Dictionary<Vector2Int, int> roomOf)
        {
            foreach (var c in habSorted)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Floor";   // 변 편집기와 컨트롤러는 이름이 아니라 위치로 칸을 읽는다
                go.transform.SetParent(Floors, false);
                go.transform.localPosition = new Vector3(c.x + 0.5f, -0.05f, c.y + 0.5f);
                go.transform.localScale = new Vector3(1f, 0.1f, 1f);
                go.GetComponent<Renderer>().sharedMaterial = Tint(Layout.Rooms[roomOf[c]].floorColor, false);
            }
        }

        // 방마다 자기 쪽 벽을 갖는 두 겹 벽(설계 2.4절). 컨트롤러가 이름(Wall, WinSill, WinTop, DoorLintel)과 위치로 방향을 읽는다
        void BuildInnerWalls(List<Vector2Int> habSorted, Dictionary<Vector2Int, int> roomOf, HashSet<Vector2Int> hull, HashSet<(Vector2Int, Vector2Int)> windowFaces)
        {
            var doors = Layout.DoorEdges();
            foreach (var c in habSorted)
            {
                int room = roomOf[c];
                var center = new Vector3(c.x + 0.5f, 0f, c.y + 0.5f);
                foreach (var d in Dirs)
                {
                    var n = c + d;
                    if (roomOf.TryGetValue(n, out int other) && other == room) continue;

                    var pos = center + new Vector3(d.x, 0f, d.y) * (0.5f - WallThickness * 0.5f);
                    bool alongZ = d.x != 0;
                    if (doors.Contains(EdgeKey.Between(c, n)))
                        WallPiece("DoorLintel", pos, 2.5f, 1f, alongZ, DoorFrameMaterial);
                    else if (!hull.Contains(n) && windowFaces.Contains((c, d)))
                    {
                        WallPiece("WinSill", pos, 0.45f, 0.9f, alongZ, InnerWallMaterial);
                        WallPiece("WinTop", pos, 2.55f, 0.9f, alongZ, InnerWallMaterial);
                    }
                    else
                        WallPiece("Wall", pos, RoomHeight * 0.5f, RoomHeight, alongZ, InnerWallMaterial);
                }
            }
        }

        void WallPiece(string pieceName, Vector3 pos, float centerY, float height, bool alongZ, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = pieceName;
            go.transform.SetParent(InnerWalls, false);
            go.transform.localPosition = new Vector3(pos.x, centerY, pos.z);
            go.transform.localScale = alongZ ? new Vector3(WallThickness, height, 1f) : new Vector3(1f, height, WallThickness);
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }

        void BuildFurniture()
        {
            foreach (var f in Layout.FurnitureItems)
            {
                var go = GameObject.CreatePrimitive(f.shape);
                go.name = f.name;
                go.transform.SetParent(Interior, false);
                go.transform.SetLocalPositionAndRotation(f.position, Quaternion.Euler(f.euler));
                go.transform.localScale = f.scale;
                go.GetComponent<Renderer>().sharedMaterial = Tint(f.color, f.emissive);
            }
        }

        void BuildRoomLights()
        {
            foreach (var l in Layout.Lights)
            {
                var go = new GameObject(l.name);
                go.transform.SetParent(RoomLights, false);
                go.transform.localPosition = l.position;
                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = l.range;
                light.intensity = RoomLightIntensity;
                light.color = l.color;
                light.shadows = LightShadows.None;
                light.renderingLayerMask = RoomLightLayerMask;
            }
        }

        // 외피: 선체 칸(거주 + 구조) 전부에 지붕과 밑판, 빈 이웃 변마다 벽(창 면이면 창 벽), 볼록 꼭짓점마다 모서리.
        // 오목 꼭짓점은 두 벽 조각이 서로 파고들어 안쪽 모서리를 스스로 닫으므로 따로 조각을 두지 않는다
        void BuildHull(HashSet<Vector2Int> hull, HashSet<(Vector2Int, Vector2Int)> windowFaces)
        {
            var parent = new GameObject("HullGenerated").transform;
            parent.SetParent(Exterior, false);
            var cells = new List<Vector2Int>(hull);
            cells.Sort(ShipLayout.CompareCells);

            foreach (var c in cells)
            {
                var center = new Vector3(c.x + 0.5f, 0f, c.y + 0.5f);
                Place(Roof, center, Quaternion.identity, parent, $"Roof({c.x},{c.y})");
                Place(Belly, center, Quaternion.identity, parent, $"Belly({c.x},{c.y})");

                foreach (var d in Dirs)
                {
                    if (hull.Contains(c + d)) continue;
                    bool window = windowFaces.Contains((c, d));
                    var pos = center + new Vector3(d.x * 0.5f, 0f, d.y * 0.5f);
                    Place(window ? WallWindow : Wall, pos, Quaternion.LookRotation(new Vector3(d.x, 0f, d.y)), parent, $"{(window ? "WallWindow" : "Wall")}({c.x},{c.y},{d.x},{d.y})");
                }

                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        if (hull.Contains(c + new Vector2Int(sx, 0)) || hull.Contains(c + new Vector2Int(0, sz))) continue;
                        float yaw = sx > 0 ? (sz > 0 ? 0f : 90f) : (sz > 0 ? -90f : 180f);   // 모서리 프리팹은 (+x, +z) 기준
                        Place(CornerOut, center + new Vector3(sx * 0.5f, 0f, sz * 0.5f), Quaternion.Euler(0f, yaw, 0f), parent, $"CornerOut({c.x},{c.y},{sx},{sz})");
                    }
            }
        }

        // 형판 부품: 전부 칸 데이터에서 자리를 계산한다(절대 좌표 없음)
        void BuildParts(HashSet<Vector2Int> hull, HashSet<Vector2Int> hab, Dictionary<Vector2Int, StructureRole> roleOf, Dictionary<Vector2Int, int> roomOf)
        {
            var parent = new GameObject("StampParts").transform;
            parent.SetParent(Exterior, false);

            PlaceEngines(hull, roleOf, parent);

            // 중심선의 두 줄(거울 축 양쪽)에서 선체 맨 앞과 맨 뒤
            int mirrorSum = ShipStructureFill.MirrorSum(hab);
            float axisZ = mirrorSum * 0.5f + 0.5f;
            int rowA = mirrorSum / 2, rowB = mirrorSum - rowA;
            int front = int.MinValue, rear = int.MaxValue;
            foreach (var c in hull)
                if (c.y == rowA || c.y == rowB) { front = Mathf.Max(front, c.x); rear = Mathf.Min(rear, c.x); }
            if (front == int.MinValue)
                throw new System.InvalidOperationException("선체가 중심선을 지나지 않는다. 뱃머리와 용골 자리를 정할 수 없다");

            Place(Prow, new Vector3(front + 1f, 0f, axisZ), Quaternion.identity, parent, "Prow");
            var frontCell = new Vector2Int(front, rowB);
            if (roomOf.TryGetValue(frontCell, out int frontRoom) && Layout.Rooms[frontRoom].windows)
                Place(BridgeBrow, new Vector3(front + 1f, 0f, axisZ), Quaternion.identity, parent, "BridgeBrow");

            var keel = Place(Keel, new Vector3(rear, 0f, axisZ), Quaternion.identity, parent, "Keel");
            keel.transform.localScale = new Vector3((front + 1f - rear) / KeelMeshLength, 1f, 1f);

            // 등줄기: 중심선을 따라 뒤끝에서 뱃머리 조금 앞까지 이어지는 덮개. 평평한 지붕에 높낮이를 준다
            float spineLength = front + 1f - SpineFrontGap - rear;
            if (spineLength > 1f && SpineHeight > 0f)
            {
                float sy = SpineHeight / CapMeshThickness;
                var spine = Place(PodCap, new Vector3(rear + spineLength * 0.5f, RoofTop * (1f - sy), axisZ), Quaternion.identity, parent, "Spine");
                spine.transform.localScale = new Vector3(spineLength / CapMeshWidth, sy, SpineWidth / CapMeshDepth);
            }

            PlaceCaps(roleOf, parent);
        }

        // 엔진 자리: 엔진 역할 칸 중 바로 뒤가 우주인 칸이 세로로 이어진 줄마다 2×2 엔진을 나란히 놓는다
        void PlaceEngines(HashSet<Vector2Int> hull, Dictionary<Vector2Int, StructureRole> roleOf, Transform parent)
        {
            var faces = new List<Vector2Int>();
            foreach (var (c, role) in roleOf)
                if (role == StructureRole.Engine && !hull.Contains(c + Vector2Int.left)) faces.Add(c);
            faces.Sort(ShipLayout.CompareCells);

            int i = 0, n = 0;
            while (i < faces.Count)
            {
                int j = i;
                while (j + 1 < faces.Count && faces[j + 1].x == faces[i].x && faces[j + 1].y == faces[j].y + 1) j++;
                int len = j - i + 1, count = len / 2, start = faces[i].y + (len - count * 2) / 2;
                for (int k = 0; k < count; k++)
                    Place(Engine, new Vector3(faces[i].x + 1f, 0f, start + k * 2 + 1f), Quaternion.identity, parent, $"Engine({n++})");
                i = j + 1;
            }
        }

        // 형판 덮개: 같은 역할의 형판 칸 덩어리마다 하나. 엔진 포드는 각진 덮개, 탱크는 돔
        void PlaceCaps(Dictionary<Vector2Int, StructureRole> roleOf, Transform parent)
        {
            var sources = new Dictionary<Vector2Int, string>();
            foreach (var s in Layout.StructureCells) sources[s.cell] = s.source;

            var seen = new HashSet<Vector2Int>();
            var keys = new List<Vector2Int>(roleOf.Keys);
            keys.Sort(ShipLayout.CompareCells);
            int n = 0;
            foreach (var start in keys)
            {
                var role = roleOf[start];
                if (seen.Contains(start) || (role != StructureRole.Engine && role != StructureRole.Tank)) continue;

                // 같은 역할로 이어진 칸 덩어리의 경계 상자
                var stack = new Stack<Vector2Int>();
                stack.Push(start); seen.Add(start);
                int minX = start.x, maxX = start.x, minZ = start.y, maxZ = start.y;
                while (stack.Count > 0)
                {
                    var c = stack.Pop();
                    minX = Mathf.Min(minX, c.x); maxX = Mathf.Max(maxX, c.x);
                    minZ = Mathf.Min(minZ, c.y); maxZ = Mathf.Max(maxZ, c.y);
                    foreach (var d in Dirs)
                    {
                        var nb = c + d;
                        if (seen.Contains(nb) || !roleOf.TryGetValue(nb, out var r) || r != role) continue;
                        seen.Add(nb); stack.Push(nb);
                    }
                }

                float w = maxX - minX + 1, dpt = maxZ - minZ + 1;
                var center = new Vector3(minX + w * 0.5f, 0f, minZ + dpt * 0.5f);
                if (role == StructureRole.Engine)
                {
                    float sy = PodCapHeight / CapMeshThickness;
                    var cap = Place(PodCap, new Vector3(center.x, RoofTop * (1f - sy), center.z), Quaternion.identity, parent, $"PodCap({n++})");
                    cap.transform.localScale = new Vector3((w - 0.8f) / CapMeshWidth, sy, (dpt - 0.8f) / CapMeshDepth);
                }
                else
                {
                    var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    dome.name = $"TankDome({n++})";
                    Destroy(dome.GetComponent<Collider>());   // 외피 부품은 충돌하지 않는다(외피 프리팹과 같음)
                    dome.transform.SetParent(parent, false);
                    dome.transform.localPosition = new Vector3(center.x, RoofTop, center.z);
                    dome.transform.localScale = new Vector3(w - 1f, TankDomeHeight * 2f, dpt - 1f);
                    dome.GetComponent<Renderer>().sharedMaterial = DomeMaterial;
                    HullPieceCount++;
                }
            }
        }

        static Vector3 HullCenter(HashSet<Vector2Int> hull)
        {
            int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
            foreach (var c in hull)
            {
                minX = Mathf.Min(minX, c.x); maxX = Mathf.Max(maxX, c.x);
                minZ = Mathf.Min(minZ, c.y); maxZ = Mathf.Max(maxZ, c.y);
            }
            return new Vector3((minX + maxX + 1) * 0.5f, 1.5f, (minZ + maxZ + 1) * 0.5f);
        }

        GameObject Place(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent, string objectName)
        {
            var go = Instantiate(prefab, position, rotation, parent);
            go.name = objectName;
            HullPieceCount++;
            return go;
        }

        Material Tint(Color color, bool emissive)
        {
            if (_tints.TryGetValue((color, emissive), out var m)) return m;
            m = new Material(TintTemplate) { name = $"Tint_{ColorUtility.ToHtmlStringRGB(color)}" };
            m.SetColor("_BaseColor", color);
            if (emissive)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", color * 4f);
            }
            _tints[(color, emissive)] = m;
            return m;
        }

#if UNITY_EDITOR
        /// <summary>자동 채움 규칙으로 Layout의 구조 칸 목록을 새로 쓴다. 이전 칸의 내구도는 이어받는다.</summary>
        [ContextMenu("Refill Structure Cells")]
        public void RefillStructureCells()
        {
            var cells = ShipStructureFill.Compute(Layout.HabitableCells(), Layout.WindowFaces(), Profile, Layout.StructureCells);
            UnityEditor.Undo.RecordObject(Layout, "Refill Structure Cells");
            Layout.StructureCells = cells;
            UnityEditor.EditorUtility.SetDirty(Layout);
            Debug.Log($"[ShipGenerator] 구조 칸 {cells.Count}개, 비교값 {ShipStructureFill.Fingerprint(cells):X8}", this);
        }
#endif
    }
}
