using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceOdyssey.Blockout
{
    /// <summary>
    /// 통로 그물 계획 1a(Docs/ShipLayoutNetworkPlan.md 11절)의 판정용 미리보기.
    /// 섹션 하나짜리 문자 격자를 읽어 통로와 모듈 자리 바닥, 구조 칸 판, 못 들어가게 막는 보이지 않는 충돌체만 깐다.
    /// 벽, 문 조각, 외피, 단면, 소품은 만들지 않는다. 1b에서 형식 2 생성기로 대체되고 지운다.
    /// 컨트롤러(ShipBlockoutController)가 Awake에서 바닥 그룹을 읽으므로 그보다 먼저 실행한다.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class NetworkGridPreview : MonoBehaviour
    {
        [field: Header("데이터")]
        [field: SerializeField]
        [field: Tooltip("문자 격자(.txt). 글자 뜻은 파일 머리 주석과 같다")]
        public TextAsset Grid { get; set; }

        [field: Header("그룹 (컨트롤러가 참조)")]
        [field: SerializeField] public Transform Floors { get; set; }
        [field: SerializeField] public Transform Marks { get; set; }
        [field: SerializeField] public Transform StructureParent { get; set; }

        [field: SerializeField]
        [field: Tooltip("외부 궤도 카메라가 도는 중심. 선체 경계 상자 가운데로 옮긴다")]
        public Transform OrbitCenter { get; set; }

        [field: SerializeField]
        [field: Tooltip("시작 위치(@ 칸)로 옮길 플레이어")]
        public CharacterController Player { get; set; }

        [field: Header("표시")]
        [field: SerializeField]
        [field: Tooltip("바닥과 판 재질의 원본. 색만 바꾼 복제본을 만든다")]
        public Material TintTemplate { get; set; }

        [field: SerializeField]
        [field: Tooltip("구조 칸 판의 윗면 높이(m). 바닥과 높이로 갈라 보이게 하는 값. 높을수록 통로가 기계 사이에 파인 길로 읽히지만 내부 뷰에서 뒤쪽 통로를 가린다")]
        public float StructureHeight { get; set; } = 0.35f;

        [field: SerializeField]
        [field: Tooltip("통로 바닥 색. 간선과 지선은 같은 색(1a)")]
        public Color CorridorColor { get; set; } = new(0.80f, 0.80f, 0.78f);

        [field: SerializeField]
        [field: Tooltip("문 칸 앞 통로 바닥에 긋는 줄 색")]
        public Color DoorMarkColor { get; set; } = new(1f, 0.95f, 0.55f);

        [field: SerializeField]
        [field: Tooltip("역할별 구조 칸 판 색. 순서는 StructureRole(Fill, Armor, Tank, Engine). 바닥보다 확실히 어둡게")]
        public Color[] StructureColors { get; set; } =
        {
            new(0.22f, 0.23f, 0.26f), new(0.13f, 0.13f, 0.15f), new(0.12f, 0.24f, 0.27f), new(0.32f, 0.18f, 0.10f)
        };

        const float RoomHeight = 3f;          // 구조 칸과 우주 쪽을 막는 보이지 않는 충돌체 높이(방 안 높이)
        const float DoorMarkWidth = 0.15f;    // 문 줄 폭(m)
        const float RoomSaturation = 0.35f;   // 자리 바닥 색의 채도. 낮을수록 탁함
        const float RoomValue = 0.55f;        // 자리 바닥 색의 밝기. 통로보다 어둡게

        static readonly Vector2Int[] Dirs = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };

        // 자리 크기 규격(짧은 변, 긴 변). 작은 방(2×2, 2×3)은 2026-11-24 사용자 결정으로 허용
        static readonly HashSet<(int, int)> RoomSizes = new() { (2, 2), (2, 3), (3, 3), (3, 4), (4, 4), (4, 5) };

        readonly List<Material> _materials = new();

        /// <summary>격자에서 읽은 배 한 척. 칸 좌표는 배 루트 기준(x = 열, z = 아래에서 센 줄).</summary>
        sealed class Ship
        {
            public readonly HashSet<Vector2Int> Corridor = new();
            public readonly Dictionary<Vector2Int, char> RoomOf = new();
            public readonly Dictionary<Vector2Int, StructureRole> RoleOf = new();
            public readonly List<(Vector2Int door, Vector2Int corridor, char room)> Doors = new();
            public Vector2Int Spawn;
            public bool InHull(Vector2Int c) => Corridor.Contains(c) || RoomOf.ContainsKey(c) || RoleOf.ContainsKey(c);
            public bool Walkable(Vector2Int c) => Corridor.Contains(c) || RoomOf.ContainsKey(c);
        }

        void Awake()
        {
            var ship = Parse(Grid);
            Validate(ship, Grid.name);
            Build(ship);
            Report(ship);
        }

        void OnDestroy()
        {
            foreach (var m in _materials) Destroy(m);
        }

        // 신뢰 경계: 사람이 손으로 쓴 문자 격자. 규칙을 어기면 조용히 고치지 않고 예외로 멈춘다
        static Ship Parse(TextAsset grid)
        {
            var rows = new List<string>();
            foreach (var raw in grid.text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0 || line[0] == ';') continue;
                rows.Add(line);
            }
            if (rows.Count == 0) throw new FormatException($"'{grid.name}'에 격자 줄이 없다");

            var ship = new Ship();
            int spawnCount = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Length != rows[0].Length)
                    throw new FormatException($"'{grid.name}' {i + 1}번째 격자 줄의 길이({rows[i].Length})가 첫 줄({rows[0].Length})과 다르다");
                int z = rows.Count - 1 - i;
                for (int x = 0; x < rows[i].Length; x++)
                {
                    var c = new Vector2Int(x, z);
                    char ch = rows[i][x];
                    switch (ch)
                    {
                        case '.': break;
                        case '#': ship.RoleOf[c] = StructureRole.Fill; break;
                        case '%': ship.RoleOf[c] = StructureRole.Armor; break;
                        case '~': ship.RoleOf[c] = StructureRole.Tank; break;
                        case '^': ship.RoleOf[c] = StructureRole.Engine; break;
                        case '+': ship.Corridor.Add(c); break;
                        case '@': ship.Corridor.Add(c); ship.Spawn = c; spawnCount++; break;
                        case >= 'A' and <= 'Z': ship.RoomOf[c] = ch; break;
                        case >= 'a' and <= 'z':
                            ship.RoomOf[c] = char.ToUpperInvariant(ch);
                            ship.Doors.Add((c, default, char.ToUpperInvariant(ch)));
                            break;
                        default:
                            throw new FormatException($"'{grid.name}' {i + 1}번째 격자 줄 {x + 1}번째 글자 '{ch}'는 알 수 없는 글자다");
                    }
                }
            }
            if (spawnCount != 1) throw new FormatException($"'{grid.name}'에 시작 칸 @가 {spawnCount}개 있다. 정확히 하나여야 한다");
            return ship;
        }

        // 1a 검사: 자리 모양과 크기, 문, 통로 연결, 통로 폭, 막다른 길.
        // 막다른 길 판정의 최종 방법은 1b에서 테스트와 함께 정한다(계획서 3.3절). 여기서는 끝 모양 두 가지(폭 1 끝, 폭 2 끝)만 본다
        static void Validate(Ship ship, string gridName)
        {
            // 자리: 같은 글자 칸이 경계 상자를 꽉 채워야 직사각형 하나다(떨어진 두 덩어리, ㄱ자는 여기서 걸림)
            var boxes = new SortedDictionary<char, (Vector2Int min, Vector2Int max)>();
            var counts = new Dictionary<char, int>();
            foreach (var (c, room) in ship.RoomOf)
            {
                boxes[room] = boxes.TryGetValue(room, out var b) ? (Vector2Int.Min(b.min, c), Vector2Int.Max(b.max, c)) : (c, c);
                counts[room] = counts.GetValueOrDefault(room) + 1;
            }
            foreach (var (room, (min, max)) in boxes)
            {
                int w = max.x - min.x + 1, h = max.y - min.y + 1;
                if (w * h != counts[room])
                    throw new InvalidOperationException($"'{gridName}' 자리 {room}가 직사각형 하나가 아니다(경계 상자 {w}×{h}, 칸 {counts[room]}개)");
                if (!RoomSizes.Contains((Mathf.Min(w, h), Mathf.Max(w, h))))
                    throw new InvalidOperationException($"'{gridName}' 자리 {room}의 크기 {w}×{h}는 규격(2×2, 2×3, 3×3~4×4, 4×5) 밖이다");
            }

            // 문: 문 칸은 통로 칸 하나와만 맞닿아야 문 변이 하나로 정해진다(계획서 4.5절, 자동으로 고르지 않음)
            var roomsWithDoor = new HashSet<char>();
            for (int i = 0; i < ship.Doors.Count; i++)
            {
                var (door, _, room) = ship.Doors[i];
                var touching = new List<Vector2Int>();
                foreach (var d in Dirs) if (ship.Corridor.Contains(door + d)) touching.Add(door + d);
                if (touching.Count != 1)
                    throw new InvalidOperationException($"'{gridName}' 자리 {room}의 문 칸 {door}이 통로 칸 {touching.Count}개와 맞닿는다. 정확히 하나여야 한다");
                ship.Doors[i] = (door, touching[0], room);
                roomsWithDoor.Add(room);
            }
            foreach (var room in boxes.Keys)
                if (!roomsWithDoor.Contains(room))
                    throw new InvalidOperationException($"'{gridName}' 자리 {room}에 문 칸(소문자)이 없다. 통로에서 들어갈 수 없다");

            // 통로 연결: 정렬 순서 첫 통로 칸에서 퍼져서 전부 닿아야 함(계획서 3.4절의 시작 칸 정의)
            var sorted = new List<Vector2Int>(ship.Corridor);
            sorted.Sort(CompareCells);
            var seen = new HashSet<Vector2Int> { sorted[0] };
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(sorted[0]);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                foreach (var d in Dirs)
                    if (ship.Corridor.Contains(c + d) && seen.Add(c + d)) queue.Enqueue(c + d);
            }
            if (seen.Count != ship.Corridor.Count)
            {
                var lost = sorted.Find(c => !seen.Contains(c));
                throw new InvalidOperationException($"'{gridName}' 통로가 끊겨 있다. {ship.Corridor.Count - seen.Count}칸이 나머지와 이어지지 않음(첫 칸 {lost})");
            }

            // 통로 폭: 3×3이 통째로 통로면 폭 3 이상. 간선은 2칸까지
            foreach (var c in sorted)
                if (IsFullBlock(ship.Corridor, c, 3, 3))
                    throw new InvalidOperationException($"'{gridName}' 통로가 {c}에서 폭 3 이상이다. 간선은 2칸, 지선은 1칸");

            // 막다른 길: 앵커(자리 문 앞 칸, 우주와 맞닿은 확장 끝점)가 아닌데 끝 모양인 칸
            var anchors = new HashSet<Vector2Int>();
            foreach (var (_, corridor, _) in ship.Doors) anchors.Add(corridor);
            foreach (var c in sorted)
                foreach (var d in Dirs)
                    if (!ship.InHull(c + d)) anchors.Add(c);

            foreach (var c in sorted)
            {
                if (anchors.Contains(c)) continue;
                int n = 0;
                foreach (var d in Dirs) if (ship.Corridor.Contains(c + d)) n++;
                if (n <= 1)
                    throw new InvalidOperationException($"'{gridName}' 폭 1 통로가 {c}에서 앵커 없이 끝난다(막다른 길)");

                // 폭 2 끝: c와 옆 칸 s가 나란히 앞(f)이 막혔고, 뒤로만 이어지고, 바깥 옆이 둘 다 막힌 모양
                foreach (var f in Dirs)
                {
                    var s = new Vector2Int(f.y, -f.x);
                    foreach (var side in new[] { s, -s })
                    {
                        var p = c + side;
                        if (!ship.Corridor.Contains(p) || anchors.Contains(p)) continue;
                        if (ship.Corridor.Contains(c + f) || ship.Corridor.Contains(p + f)) continue;
                        if (!ship.Corridor.Contains(c - f) || !ship.Corridor.Contains(p - f)) continue;
                        if (ship.Corridor.Contains(c - side) || ship.Corridor.Contains(p + side)) continue;
                        throw new InvalidOperationException($"'{gridName}' 폭 2 통로가 {c}, {p}에서 앵커 없이 끝난다(막다른 길)");
                    }
                }
            }
        }

        static bool IsFullBlock(HashSet<Vector2Int> set, Vector2Int origin, int w, int h)
        {
            for (int dx = 0; dx < w; dx++)
                for (int dz = 0; dz < h; dz++)
                    if (!set.Contains(origin + new Vector2Int(dx, dz))) return false;
            return true;
        }

        static int CompareCells(Vector2Int a, Vector2Int b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y);

        void Build(Ship ship)
        {
            var corridorMat = MakeMaterial("Corridor", CorridorColor);
            var markMat = MakeMaterial("DoorMark", DoorMarkColor);
            var roleMats = new Material[StructureColors.Length];
            for (int i = 0; i < roleMats.Length; i++) roleMats[i] = MakeMaterial($"Structure_{(StructureRole)i}", StructureColors[i]);
            var roomMats = new Dictionary<char, Material>();

            var cells = new List<Vector2Int>();
            foreach (var c in ship.Corridor) cells.Add(c);
            foreach (var c in ship.RoomOf.Keys) cells.Add(c);
            cells.Sort(CompareCells);
            foreach (var c in cells)
            {
                Material mat;
                if (ship.RoomOf.TryGetValue(c, out char room))
                {
                    if (!roomMats.TryGetValue(room, out mat))
                    {
                        // 글자마다 황금비 간격의 색상. 같은 격자면 같은 색
                        var color = Color.HSVToRGB((room - 'A') * 0.618034f % 1f, RoomSaturation, RoomValue);
                        mat = roomMats[room] = MakeMaterial($"Room_{room}", color);
                    }
                }
                else mat = corridorMat;

                // 컨트롤러는 이 그룹 자식의 월드 위치를 반내림해서 칸을 읽는다. 생성기(ShipGenerator)와 같은 모양과 위치 규칙
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Floor";
                go.transform.SetParent(Floors, false);
                go.transform.localPosition = new Vector3(c.x + 0.5f, -0.05f, c.y + 0.5f);
                go.transform.localScale = new Vector3(1f, 0.1f, 1f);
                go.GetComponent<Renderer>().sharedMaterial = mat;
            }

            foreach (var (door, corridor, room) in ship.Doors)
            {
                var d = corridor - door;
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"DoorMark({room})";
                Destroy(go.GetComponent<Collider>());   // 표시일 뿐 걸음에 걸리면 안 됨
                go.transform.SetParent(Marks, false);
                // 문 변에서 통로 쪽으로 줄 폭 절반만큼 들어간 자리
                go.transform.localPosition = new Vector3(door.x + 0.5f + d.x * (0.5f + DoorMarkWidth * 0.5f), 0.01f, door.y + 0.5f + d.y * (0.5f + DoorMarkWidth * 0.5f));
                go.transform.localScale = d.x != 0 ? new Vector3(DoorMarkWidth, 0.02f, 0.9f) : new Vector3(0.9f, 0.02f, DoorMarkWidth);
                go.GetComponent<Renderer>().sharedMaterial = markMat;
            }

            var structure = new List<Vector2Int>(ship.RoleOf.Keys);
            structure.Sort(CompareCells);
            var plates = new GameObject("StructurePlates").transform;
            plates.SetParent(StructureParent, false);
            foreach (var c in structure)
            {
                // 보이는 판은 낮지만 충돌체는 방 높이까지 세워 걸어 들어가지 못하게 함(계획서 8.4절 1a 행)
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"Structure({c.x},{c.y})";
                go.transform.SetParent(plates, false);
                go.transform.localPosition = new Vector3(c.x + 0.5f, StructureHeight * 0.5f, c.y + 0.5f);
                go.transform.localScale = new Vector3(1f, StructureHeight, 1f);
                var box = go.GetComponent<BoxCollider>();
                box.size = new Vector3(1f, RoomHeight / StructureHeight, 1f);
                box.center = new Vector3(0f, (RoomHeight * 0.5f - StructureHeight * 0.5f) / StructureHeight, 0f);
                go.GetComponent<Renderer>().sharedMaterial = roleMats[(int)ship.RoleOf[c]];
            }

            // 외피가 없는 1a에서는 우주와 맞닿은 거주 칸 변(확장 끝점, 창 면)으로 걸어 나갈 수 있으므로 우주 칸에 충돌체만 둔다
            var blockers = new GameObject("SpaceBlockers").transform;
            blockers.SetParent(StructureParent, false);
            var spaceCells = new SortedSet<Vector2Int>(Comparer<Vector2Int>.Create(CompareCells));
            foreach (var c in cells)
                foreach (var d in Dirs)
                    if (!ship.InHull(c + d)) spaceCells.Add(c + d);
            foreach (var c in spaceCells)
            {
                var go = new GameObject($"SpaceBlocker({c.x},{c.y})");
                go.transform.SetParent(blockers, false);
                go.transform.localPosition = new Vector3(c.x + 0.5f, RoomHeight * 0.5f, c.y + 0.5f);
                go.AddComponent<BoxCollider>().size = new Vector3(1f, RoomHeight, 1f);
            }

            int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
            var allCells = new List<Vector2Int>(structure);
            allCells.AddRange(cells);
            foreach (var c in allCells)
            {
                minX = Mathf.Min(minX, c.x); maxX = Mathf.Max(maxX, c.x);
                minZ = Mathf.Min(minZ, c.y); maxZ = Mathf.Max(maxZ, c.y);
            }
            OrbitCenter.position = transform.TransformPoint(new Vector3((minX + maxX + 1) * 0.5f, 1.5f, (minZ + maxZ + 1) * 0.5f));

            // 컨트롤러 Awake가 플레이어 위치로 카메라 리그를 맞추므로 그 전에 옮긴다.
            // 캐릭터 컨트롤러는 물리 쪽 위치를 따로 들고 있어서 변환만 바꾸면 다음 동기화 전까지 옛 위치에 남는다
            Player.transform.position = transform.TransformPoint(new Vector3(ship.Spawn.x + 0.5f, 0f, ship.Spawn.y + 0.5f));
            Physics.SyncTransforms();
        }

        Material MakeMaterial(string materialName, Color color)
        {
            var m = new Material(TintTemplate) { name = $"Preview_{materialName}" };
            m.SetColor("_BaseColor", color);
            _materials.Add(m);
            return m;
        }

        void Report(Ship ship)
        {
            int trunk = 0;
            foreach (var c in ship.Corridor)
            {
                // 2×2가 통째로 통로인 묶음에 들어가는 칸 = 간선, 나머지 = 지선(계획서 3.3절: 폭을 보고 계산)
                bool inBlock = false;
                for (int dx = -1; dx <= 0 && !inBlock; dx++)
                    for (int dz = -1; dz <= 0 && !inBlock; dz++)
                        inBlock = IsFullBlock(ship.Corridor, c + new Vector2Int(dx, dz), 2, 2);
                if (inBlock) trunk++;
            }
            var roles = new int[4];
            foreach (var r in ship.RoleOf.Values) roles[(int)r]++;
            var rooms = new HashSet<char>(ship.RoomOf.Values);
            int habitable = ship.Corridor.Count + ship.RoomOf.Count;
            int hull = habitable + ship.RoleOf.Count;
            Debug.Log($"[NetworkGridPreview] '{Grid.name}' 선체 {hull}칸 = 통로 {ship.Corridor.Count}(간선 {trunk}, 지선 {ship.Corridor.Count - trunk}) + 자리 {rooms.Count}개 {ship.RoomOf.Count}칸 + 구조 {ship.RoleOf.Count}" +
                      $"(채움 {roles[0]}, 장갑 {roles[1]}, 탱크 {roles[2]}, 엔진 {roles[3]}). 거주 비율 {habitable / (float)hull:P1}", this);
        }
    }
}
