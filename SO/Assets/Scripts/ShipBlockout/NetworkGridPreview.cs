using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpaceOdyssey.Blockout
{
    /// <summary>
    /// 블록 격자 N1/N1b(Docs/ShipBlockGridPlan.md 6장 N1, 10장)의 판정용 미리보기.
    /// 프리셋 텍스트와 블록 종류 텍스트를 ShipNetworkRules에 넘기고, 돌려받은 배를 그리기만 한다(파서와 검사는 규칙 클래스에 있음).
    /// 블록별 색조의 가압 판과 설비 판, 고립 가압구역 표시, 접합 선, 칸 격자선(G 키로 켜고 끔, 10.4절), 못 들어가게 막는 보이지 않는 충돌체를 깐다.
    /// 벽, 문 조각, 차단막 띠, 외피, 모듈은 만들지 않는다.
    /// 모든 위치와 크기는 칸 크기(CellSize)를 곱한 미터 값이다(계획서 4.1절).
    /// 컨트롤러(ShipBlockoutController)가 Awake에서 플레이어 위치를 읽으므로 그보다 먼저 실행한다.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class NetworkGridPreview : MonoBehaviour
    {
        [field: Header("데이터")]
        [field: SerializeField]
        [field: Tooltip("프리셋 텍스트(.txt). 줄 문법은 계획서 2.3절")]
        public TextAsset Preset { get; set; }

        [field: SerializeField]
        [field: Tooltip("블록 종류 문자 격자(.txt, 종류마다 가로×세로, 한 변 6칸 이하) 목록. 프리셋이 에셋 이름으로 찾는다(계획서 2.5절, 10.2절)")]
        public TextAsset[] BlockTypes { get; set; }

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
        [field: Tooltip("칸 한 변의 길이(m). 키우면 방과 통로가 넓어지고 같은 걷는 속도에서 방 사이 이동 시간이 길어진다(계획서 4.1절)")]
        public float CellSize { get; set; } = 2f;

        [field: SerializeField]
        [field: Tooltip("바닥과 판 재질의 원본. 색만 바꾼 복제본을 만든다")]
        public Material TintTemplate { get; set; }

        [field: SerializeField]
        [field: Tooltip("설비 칸 판의 윗면 높이(m). 높을수록 가압 칸이 기계 사이에 파인 길로 읽히지만 내부 뷰에서 뒤쪽을 가린다")]
        public float StructureHeight { get; set; } = 0.35f;

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
        [field: Tooltip("고립 가압구역(시작 칸에서 닿지 않는 가압 칸) 바닥 색. 블록 색조와 확실히 다르게")]
        public Color IsolatedColor { get; set; } = new(0.95f, 0.12f, 0.08f);

        [field: SerializeField]
        [field: Tooltip("칸 경계 얇은 선 색. 흰 촬영 배경과 밝은 가압 판, 어두운 설비 판 모두에서 읽히게 중간 밝기의 진한 색으로")]
        public Color GridColor { get; set; } = new(0.10f, 0.45f, 0.95f);

        [field: SerializeField]
        [field: Tooltip("5칸마다 긋는 굵은 선(큰 눈금) 색")]
        public Color GridMajorColor { get; set; } = new(0.02f, 0.18f, 0.70f);

        const float RoomHeight = 3f;          // 설비 칸과 우주 쪽을 막는 보이지 않는 충돌체 높이(방 안 높이, m)
        const float SeamWidth = 0.12f;        // 접합 변 선 폭(m)
        const float PressurizedSaturation = 0.35f, PressurizedValue = 0.9f;   // 가압 판: 블록 색조를 밝게
        const float StructureTint = 0.35f;    // 설비 판 역할 색에 블록 색조를 섞는 비율. 높을수록 블록 구분이 강하고 역할 구분이 약함
        const float GridWidth = 0.04f, GridMajorWidth = 0.12f;   // 격자선 폭(m): 얇은 선, 5칸마다 굵은 선
        const int GridMajorEvery = 5, GridMargin = 2;            // 큰 눈금 간격(칸), 배 경계 상자 바깥으로 더 그리는 칸 수(10.4절)
        const float GridLift = 0.015f;        // 칸 윗면 위로 띄우는 높이(m). 접합 선(윗면 위 0.02)보다 낮아 접합 선이 위에 보임

        static readonly Vector2Int[] Dirs = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };

        readonly List<Material> _materials = new();
        readonly List<Mesh> _meshes = new();
        GameObject _grid;
        GUIStyle _labelStyle;

        void Awake()
        {
            try { Init(); }
            catch
            {
                // 초기화에 실패하면 격자(_grid)가 없는 채로 OnGUI와 Update가 계속 돌아 2차 NRE로 원래 예외를 덮는다.
                // 컴포넌트를 끄고 원래 예외는 그대로 던진다(삼키지 않음)
                enabled = false;
                throw;
            }
        }

        void Init()
        {
            var blockTypes = new List<(string, string)>();
            for (int i = 0; i < BlockTypes.Length; i++)
            {
                // 인스펙터 연결이 끊긴 칸을 이름 없는 종류로 넘기지 않고 어느 칸인지 드러낸다
                if (BlockTypes[i] == null) throw new MissingReferenceException($"{name}: BlockTypes {i}번째 칸이 비어 있다");
                blockTypes.Add((BlockTypes[i].name, BlockTypes[i].text));
            }
            var ship = ShipNetworkRules.Build(Preset.name, Preset.text, blockTypes);
            Build(ship);
            Debug.Log(ShipNetworkRules.Describe(ship), this);
        }

        void OnDestroy()
        {
            foreach (var m in _materials) Destroy(m);
            foreach (var m in _meshes) Destroy(m);
        }

        void Update()
        {
            var kb = Keyboard.current;
            // 키보드가 없는 기기에서는 토글 입력이 없을 뿐이다(컨트롤러의 입력 처리와 같은 방식)
            if (kb != null && kb.gKey.wasPressedThisFrame) _grid.SetActive(!_grid.activeSelf);
        }

        void OnGUI()
        {
            // 컨트롤러 안내 글자(왼쪽 위)와 겹치지 않게 왼쪽 아래 구석에 같은 크기 규칙으로 쓴다
            float s = Mathf.Max(1f, Screen.height / 720f);
            // 스타일은 OnGUI 안에서만 만들 수 있어서 처음 한 번 만들어 재사용한다(매 호출 할당 방지)
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label);
                _labelStyle.normal.textColor = Color.white;
            }
            _labelStyle.fontSize = Mathf.RoundToInt(14 * s);
            var label = _labelStyle;
            float width = Player.radius * 2f * Player.transform.lossyScale.x;
            GUI.Label(new Rect(12 * s, Screen.height - 34 * s, 900 * s, 26 * s),
                $"칸 한 변 = {CellSize:0.##} m, 방 높이 {RoomHeight:0.##} m, 캐릭터 폭 {width:0.##} m   [G] 칸 격자선 {(_grid.activeSelf ? "끄기" : "켜기")}", label);
        }

        static float Hue(ShipNetworkRules.HullBlock hullBlock) => hullBlock.Index * 0.618034f % 1f;   // 블록마다 황금비 간격의 색상

        // 칸 (x, z)의 가운데를 이 오브젝트 기준 미터 좌표로
        Vector3 CellCenter(Vector2Int c, float y) => new((c.x + 0.5f) * CellSize, y, (c.y + 0.5f) * CellSize);

        void Build(ShipNetworkRules.Ship ship)
        {
            var isolated = new HashSet<Vector2Int>();
            foreach (var zone in ship.IsolatedZones) isolated.UnionWith(zone);

            var mats = new Dictionary<(int hullBlock, int key), Material>();
            Material HullBlockMaterial(ShipNetworkRules.HullBlock hullBlock, Vector2Int c)
            {
                // 키: 0 가압, 1 고립 가압, 2 이상 설비 역할
                int key = ship.KindOf[c] == CellKind.Structure ? 2 + (int)ship.RoleOf[c] : isolated.Contains(c) ? 1 : 0;
                if (mats.TryGetValue((hullBlock.Index, key), out var m)) return m;
                float h = Hue(hullBlock);
                var color = key switch
                {
                    0 => Color.HSVToRGB(h, PressurizedSaturation, PressurizedValue),
                    1 => IsolatedColor,
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
                if (ship.KindOf[c] == CellKind.Pressurized)
                {
                    go.name = isolated.Contains(c) ? "FloorIsolated" : "Floor";
                    go.transform.SetParent(Floors, false);
                    go.transform.localPosition = CellCenter(c, -0.05f);
                    go.transform.localScale = new Vector3(CellSize, 0.1f, CellSize);
                }
                else
                {
                    // 보이는 판은 낮지만 충돌체는 방 높이까지 세워 걸어 들어가지 못하게 함. 충돌체 크기는 판 스케일 기준 로컬 값
                    go.name = $"Structure({c.x},{c.y})";
                    go.transform.SetParent(plates, false);
                    go.transform.localPosition = CellCenter(c, StructureHeight * 0.5f);
                    go.transform.localScale = new Vector3(CellSize, StructureHeight, CellSize);
                    var box = go.GetComponent<BoxCollider>();
                    box.size = new Vector3(1f, RoomHeight / StructureHeight, 1f);
                    box.center = new Vector3(0f, (RoomHeight * 0.5f - StructureHeight * 0.5f) / StructureHeight, 0f);
                }
            }

            // 접합 선: 두 블록이 맞닿는 변을 따라 두 칸 중 높은 윗면 위에 긋는다(설비 판 옆이면 판 윗면 높이).
            // EdgeKey.Center는 칸 1m 기준이라 칸 크기를 곱한다(바닥 높이 0이라 y는 그대로)
            var seamMat = MakeMaterial("Seam", SeamColor);
            foreach (var joint in ship.Joints.Values)
                foreach (var e in joint.Edges)
                {
                    float top = Mathf.Max(Top(ship, e.CellA), Top(ship, e.CellB));
                    var go = MakeMark($"Seam({joint.Id})", seamMat);
                    go.transform.localPosition = e.Center * CellSize + Vector3.up * (top + 0.02f);
                    go.transform.localScale = e.axis == 0 ? new Vector3(SeamWidth, 0.03f, CellSize) : new Vector3(CellSize, 0.03f, SeamWidth);
                }

            // 외피가 없으므로 우주와 맞닿은 가압 칸 변으로 걸어 나가지 못하게 우주 칸에 충돌체만 둔다
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
                go.transform.localPosition = CellCenter(c, RoomHeight * 0.5f);
                go.AddComponent<BoxCollider>().size = new Vector3(CellSize, RoomHeight, CellSize);
            }

            int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
            foreach (var c in all)
            {
                minX = Mathf.Min(minX, c.x); maxX = Mathf.Max(maxX, c.x);
                minZ = Mathf.Min(minZ, c.y); maxZ = Mathf.Max(maxZ, c.y);
            }
            BuildGrid(ship, minX - GridMargin, maxX + GridMargin, minZ - GridMargin, maxZ + GridMargin);

            OrbitCenter.position = transform.TransformPoint(new Vector3((minX + maxX + 1) * 0.5f * CellSize, 1.5f, (minZ + maxZ + 1) * 0.5f * CellSize));

            // 컨트롤러 Awake가 플레이어 위치로 카메라 리그를 맞추므로 그 전에 옮긴다.
            // 캐릭터 컨트롤러는 물리 쪽 위치를 따로 들고 있어서 변환만 바꾸면 다음 동기화 전까지 옛 위치에 남는다
            Player.transform.position = transform.TransformPoint(CellCenter(ship.Start, 0f));
            Physics.SyncTransforms();
        }

        float Top(ShipNetworkRules.Ship ship, Vector2Int c) => ship.KindOf[c] == CellKind.Structure ? StructureHeight : 0f;

        // 배 밖 칸(빈 곳)은 바닥 높이 0으로 친다
        float TopOrFloor(ShipNetworkRules.Ship ship, Vector2Int c) => ship.KindOf.ContainsKey(c) ? Top(ship, c) : 0f;

        // 칸 격자선(10.4절): 칸 경계마다 변 하나 길이의 납작한 띠를 두 칸 중 높은 윗면 위에 깐다.
        // 가압 바닥과 설비 판 윗면 높이가 달라서 한 높이에 그리면 위에서 볼 때 시차로 칸과 선이 어긋나기 때문이다.
        // 오브젝트 수를 줄이려고 얇은 선과 굵은 선을 각각 메시 하나로 합치고, 충돌체는 만들지 않는다(걸음을 막지 않음)
        void BuildGrid(ShipNetworkRules.Ship ship, int x0, int x1, int z0, int z1)
        {
            _grid = new GameObject("CellGrid");
            _grid.transform.SetParent(Marks, false);
            var minor = (verts: new List<Vector3>(), tris: new List<int>());
            var major = (verts: new List<Vector3>(), tris: new List<int>());

            void Strip((List<Vector3> verts, List<int> tris) m, Vector3 from, Vector3 to, float halfWidth)
            {
                var side = Vector3.Cross(Vector3.up, (to - from).normalized) * halfWidth;
                int i = m.verts.Count;
                m.verts.Add(from - side); m.verts.Add(to - side); m.verts.Add(to + side); m.verts.Add(from + side);
                m.tris.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            bool IsMajor(int b) => (b % GridMajorEvery + GridMajorEvery) % GridMajorEvery == 0;

            // x = bx 인 세로 경계: 왼쪽 칸 (bx-1, z)와 오른쪽 칸 (bx, z) 사이 변
            for (int bx = x0; bx <= x1 + 1; bx++)
                for (int z = z0; z <= z1; z++)
                {
                    float y = Mathf.Max(TopOrFloor(ship, new Vector2Int(bx - 1, z)), TopOrFloor(ship, new Vector2Int(bx, z))) + GridLift;
                    bool big = IsMajor(bx);
                    Strip(big ? major : minor, new Vector3(bx * CellSize, y, z * CellSize), new Vector3(bx * CellSize, y, (z + 1) * CellSize),
                        (big ? GridMajorWidth : GridWidth) * 0.5f);
                }
            // z = bz 인 가로 경계: 아래 칸 (x, bz-1)와 위 칸 (x, bz) 사이 변
            for (int bz = z0; bz <= z1 + 1; bz++)
                for (int x = x0; x <= x1; x++)
                {
                    float y = Mathf.Max(TopOrFloor(ship, new Vector2Int(x, bz - 1)), TopOrFloor(ship, new Vector2Int(x, bz))) + GridLift;
                    bool big = IsMajor(bz);
                    Strip(big ? major : minor, new Vector3(x * CellSize, y, bz * CellSize), new Vector3((x + 1) * CellSize, y, bz * CellSize),
                        (big ? GridMajorWidth : GridWidth) * 0.5f);
                }

            AddGridMesh("GridMinor", minor.verts, minor.tris, MakeMaterial("GridMinor", GridColor));
            AddGridMesh("GridMajor", major.verts, major.tris, MakeMaterial("GridMajor", GridMajorColor));
        }

        void AddGridMesh(string meshName, List<Vector3> verts, List<int> tris, Material mat)
        {
            // 큰 배에서 정점 수가 16비트 색인 한도를 넘을 수 있어 32비트 색인을 쓴다
            var mesh = new Mesh { name = meshName, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            _meshes.Add(mesh);
            var go = new GameObject(meshName);
            go.transform.SetParent(_grid.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

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
