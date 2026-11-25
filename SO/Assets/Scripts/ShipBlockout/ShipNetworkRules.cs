using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace SpaceOdyssey.Blockout
{
    /// <summary>블록 칸 지도의 칸 종류. 가압 칸이 가압구역(걷는 칸), 설비 칸이 설비구역이다.</summary>
    public enum CellKind { Pressurized, Structure }

    /// <summary>
    /// 양쪽이 가압 칸인 접합 변의 상태(옛 계획서 2.5절). N1은 전부 열린 변이다(계획서 8.1절 B).
    /// Wall은 접합 변 덮어쓰기(후속 단계)가 생기면 쓰이고 N1에는 이 값을 만드는 경로가 없다. 도달 계산이 3.1절 정의("벽이 아니면 이어짐")를 그대로 구현하려고 Wall 분기를 둔다.
    /// </summary>
    public enum SeamEdge { Open, Wall }

    /// <summary>
    /// 블록 격자 N1(Docs/ShipBlockGridPlan.md 6장 N1)의 규칙 계산. 장면 객체에 의존하지 않는다.
    /// 입력은 문자열(프리셋 텍스트, 종류 이름별 블록 종류 텍스트), 출력은 칸 좌표와 종류의 평범한 C# 자료다.
    /// 블록 종류 파서(2.5절), 프리셋 파서(2.3절), 회전 배치(2.4절), 붙이기 규칙 1~3(3.2절), 접합부 계산과 접합 변 기본값,
    /// 시작 칸 기준 도달과 고립 가압구역(3.1절), 콘솔용 통계가 여기 있다.
    /// 입력은 사람이 손으로 쓴 텍스트(신뢰 경계)라서 규칙을 어기면 조용히 고치지 않고 예외로 멈춘다.
    /// </summary>
    public static class ShipNetworkRules
    {
        /// <summary>블록 종류의 회전 전 크기(3열×5행, 계획서 2.4절).</summary>
        public const int TypeWidth = 3, TypeHeight = 5;

        /// <summary>블록 종류 하나. 좌표는 회전 전 자기 좌표 (u, v)(맨 아래 줄 첫 글자가 (0,0)).</summary>
        public sealed class BlockType
        {
            public readonly string Name;
            public readonly Dictionary<Vector2Int, CellKind> Cells = new();
            public readonly Dictionary<Vector2Int, StructureRole> Roles = new();
            public BlockType(string name) { Name = name; }
        }

        /// <summary>배에 놓인 블록. 칸은 종류 칸을 Rotation만큼 돌린 뒤 Origin만큼 옮긴 것이다(2.4절).</summary>
        public sealed class HullBlock
        {
            public string Id;
            public BlockType Type;
            public Vector2Int Origin;   // 회전한 경계 상자의 왼쪽 아래 칸(배 좌표)
            public int Rotation;        // 0, 90, 180, 270도 시계 방향
            public int PresetLine;
            public int Index;           // 프리셋에 적힌 순서. 표시 색을 고를 때만 쓴다
        }

        /// <summary>블록 식별자 쌍 하나. 접합 변은 두 칸이 서로 다른 블록인 인접 칸 쌍의 변 전부(옛 계획서 2.5절).</summary>
        public sealed class Joint
        {
            public string Id;
            public HullBlock A, B;
            public readonly List<EdgeKey> Edges = new();
            /// <summary>양쪽이 가압 칸인 접합 변만 기록한다. 열린 변도 빠짐없이 적는다.</summary>
            public readonly Dictionary<EdgeKey, SeamEdge> States = new();
        }

        /// <summary>블록을 배 좌표로 옮겨 합친 배 한 척.</summary>
        public sealed class Ship
        {
            public string PresetName;
            public readonly List<HullBlock> HullBlocks = new();
            public readonly Dictionary<Vector2Int, HullBlock> OwnerOf = new();
            public readonly Dictionary<Vector2Int, CellKind> KindOf = new();
            public readonly Dictionary<Vector2Int, StructureRole> RoleOf = new();
            public readonly SortedDictionary<string, Joint> Joints = new(StringComparer.Ordinal);
            public Vector2Int Start;
            /// <summary>시작 칸에서 도달 가능한 가압 칸(3.1절).</summary>
            public readonly HashSet<Vector2Int> Reachable = new();
            /// <summary>시작 칸에서 도달할 수 없는 가압 칸 덩어리들(고립 가압구역, 3.1절). 칸은 정렬되어 있다.</summary>
            public readonly List<List<Vector2Int>> IsolatedZones = new();
            public bool IsPressurized(Vector2Int c) => KindOf.TryGetValue(c, out var k) && k == CellKind.Pressurized;
        }

        static readonly Vector2Int[] Dirs = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };

        public static int CompareCells(Vector2Int a, Vector2Int b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y);

        // ───────────────────────── 배 만들기 ─────────────────────────

        /// <summary>
        /// 프리셋에서 배를 만든다: 블록 종류 읽기 → 프리셋 읽기 → 회전해서 배 좌표로 합치기(겹침 검사) → 맞닿음 검사 →
        /// 시작 칸 검사 → 접합부 계산 → 접합 변 기본값 채우기 → 시작 칸 기준 도달과 고립 가압구역.
        /// </summary>
        public static Ship Build(string presetName, string presetText, IReadOnlyList<(string name, string text)> blockTypes)
        {
            var types = new Dictionary<string, BlockType>(StringComparer.Ordinal);
            foreach (var (name, text) in blockTypes)
            {
                if (types.ContainsKey(name))
                    throw new ArgumentException($"이름이 같은 블록 종류가 둘 있다: '{name}'. 프리셋이 종류를 파일 이름으로 찾으므로 유일해야 한다");
                types[name] = ParseBlockType(name, text);
            }

            var preset = ParsePreset(presetName, presetText, types);
            var ship = new Ship { PresetName = presetName };
            foreach (var (id, typeName, origin, rotation, line) in preset.HullBlocks)
                Place(ship, new HullBlock { Id = id, Type = types[typeName], Origin = origin, Rotation = rotation, PresetLine = line, Index = ship.HullBlocks.Count });

            CheckContiguous(ship);

            ship.Start = preset.Start;
            if (!ship.KindOf.TryGetValue(preset.Start, out var startKind))
                throw new InvalidOperationException($"프리셋 '{presetName}' {preset.StartLine}번째 줄: 시작 칸 {preset.Start}이 배 밖이다");
            if (startKind != CellKind.Pressurized)
                throw new InvalidOperationException($"프리셋 '{presetName}' {preset.StartLine}번째 줄: 시작 칸 {preset.Start}이 블록 '{ship.OwnerOf[preset.Start].Id}'의 설비 칸이다. 가압 칸이어야 한다");

            ComputeJoints(ship);
            FillJointDefaults(ship);
            ComputeReach(ship);
            return ship;
        }

        /// <summary>회전 전 자기 좌표 (u, v)를 회전한 경계 상자 안 좌표로 바꾼다. 90도 시계 방향 한 번이 (u, v)→(v, w−1−u)(2.4절).</summary>
        public static Vector2Int Rotate(Vector2Int c, int rotation)
        {
            // 공개 메서드라 파서를 거치지 않은 호출도 들어올 수 있다. 범위 밖 회전을 0으로 조용히 돌려주면 틀린 배가 만들어진다
            if (rotation < 0 || rotation > 270 || rotation % 90 != 0)
                throw new ArgumentOutOfRangeException(nameof(rotation), rotation, "회전은 0, 90, 180, 270 중 하나여야 한다(계획서 2.4절)");
            int w = TypeWidth, h = TypeHeight;
            for (int i = 0; i < rotation / 90; i++)
            {
                c = new Vector2Int(c.y, w - 1 - c.x);
                (w, h) = (h, w);
            }
            return c;
        }

        static void Place(Ship ship, HullBlock hullBlock)
        {
            var t = hullBlock.Type;
            var cells = new List<Vector2Int>(t.Cells.Keys);
            cells.Sort(CompareCells);
            foreach (var local in cells)
            {
                var c = Rotate(local, hullBlock.Rotation) + hullBlock.Origin;
                if (ship.OwnerOf.TryGetValue(c, out var other))
                    throw new InvalidOperationException($"프리셋 '{ship.PresetName}' {hullBlock.PresetLine}번째 줄: 블록 겹침. 배 칸 {c}에 블록 '{other.Id}'({other.PresetLine}번째 줄)와 '{hullBlock.Id}'가 둘 다 있다");
                ship.OwnerOf[c] = hullBlock;
                ship.KindOf[c] = t.Cells[local];
                if (t.Roles.TryGetValue(local, out var role)) ship.RoleOf[c] = role;
            }
            ship.HullBlocks.Add(hullBlock);
        }

        // 붙이기 규칙 3(3.2절): 배 전체가 칸의 변 맞닿음(대각선 아님)으로 한 덩어리여야 함. 줄 순서와 무관하게 첫 블록에서 퍼진다
        static void CheckContiguous(Ship ship)
        {
            var first = ship.HullBlocks[0];
            var seed = Rotate(Vector2Int.zero, first.Rotation) + first.Origin;
            var seen = new HashSet<Vector2Int> { seed };
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(seed);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                foreach (var d in Dirs)
                    if (ship.OwnerOf.ContainsKey(c + d) && seen.Add(c + d)) queue.Enqueue(c + d);
            }
            if (seen.Count == ship.OwnerOf.Count) return;
            // 블록 하나의 칸은 꽉 찬 직사각형이라 한 칸이 안 닿으면 그 블록 전체가 안 닿는다
            foreach (var hullBlock in ship.HullBlocks)
                if (!seen.Contains(Rotate(Vector2Int.zero, hullBlock.Rotation) + hullBlock.Origin))
                    throw new InvalidOperationException($"프리셋 '{ship.PresetName}' {hullBlock.PresetLine}번째 줄: 블록 '{hullBlock.Id}'가 첫 블록 '{first.Id}'의 덩어리와 변으로 맞닿지 않는다. 배는 칸의 변 맞닿음으로 한 덩어리여야 한다(계획서 3.2절 규칙 3)");
            // 위 주석의 논리상 여기까지 오면 불변식이 깨진 것이다. 조용히 끝내지 않고 드러낸다
            throw new InvalidOperationException($"프리셋 '{ship.PresetName}': 닿지 않은 칸이 있는데 그 칸이 속한 블록을 찾지 못했다(불변식 위반)");
        }

        static string JointId(HullBlock p, HullBlock q) =>
            string.CompareOrdinal(p.Id, q.Id) < 0 ? $"{p.Id}+{q.Id}" : $"{q.Id}+{p.Id}";

        // 접합부 = 블록 식별자의 순서 없는 쌍마다 하나. 식별자는 서수 비교로 정렬한 "작은쪽+큰쪽"
        static void ComputeJoints(Ship ship)
        {
            var cells = new List<Vector2Int>(ship.OwnerOf.Keys);
            cells.Sort(CompareCells);
            foreach (var c in cells)
            {
                var own = ship.OwnerOf[c];
                foreach (var d in new[] { Vector2Int.right, Vector2Int.up })
                {
                    if (!ship.OwnerOf.TryGetValue(c + d, out var other) || other == own) continue;
                    bool ownFirst = string.CompareOrdinal(own.Id, other.Id) < 0;
                    var (a, b) = ownFirst ? (own, other) : (other, own);
                    var id = $"{a.Id}+{b.Id}";
                    if (!ship.Joints.TryGetValue(id, out var joint))
                        ship.Joints[id] = joint = new Joint { Id = id, A = a, B = b };
                    joint.Edges.Add(EdgeKey.Between(c, c + d));
                }
            }
        }

        /// <summary>
        /// 접합 변 기본값 채우기: 양쪽이 가압 칸이고 기록이 없는 접합 변을 열린 변으로 명시 기록한다(계획서 2.2절, 8.1절 B).
        /// 이미 기록이 있는 변은 건드리지 않는다.
        /// </summary>
        static void FillJointDefaults(Ship ship)
        {
            foreach (var joint in ship.Joints.Values)
                foreach (var e in joint.Edges)
                    if (ship.IsPressurized(e.CellA) && ship.IsPressurized(e.CellB) && !joint.States.ContainsKey(e))
                        joint.States[e] = SeamEdge.Open;
        }

        // 도달(3.1절): 같은 블록의 가압 2×2는 항상 이어지고, 블록 사이는 접합 변이 벽이 아니면 이어진다(차단막은 지나감).
        // 시작 칸에서 닿지 않는 가압 칸 덩어리는 예외가 아니라 고립 가압구역으로 돌려준다. 붙이기는 연결을 강제하지 않는 것이 계획서 3.2절의 규칙이다
        static void ComputeReach(Ship ship)
        {
            ship.Reachable.UnionWith(Flood(ship, ship.Start));
            var rest = new List<Vector2Int>();
            foreach (var (c, k) in ship.KindOf)
                if (k == CellKind.Pressurized && !ship.Reachable.Contains(c)) rest.Add(c);
            rest.Sort(CompareCells);
            var assigned = new HashSet<Vector2Int>();
            foreach (var c in rest)
            {
                if (assigned.Contains(c)) continue;
                var zone = Flood(ship, c);
                assigned.UnionWith(zone);
                zone.Sort(CompareCells);
                ship.IsolatedZones.Add(zone);
            }
        }

        static List<Vector2Int> Flood(Ship ship, Vector2Int seed)
        {
            var seen = new HashSet<Vector2Int> { seed };
            var order = new List<Vector2Int> { seed };
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(seed);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                foreach (var d in Dirs)
                {
                    var n = c + d;
                    if (!ship.IsPressurized(n) || seen.Contains(n)) continue;
                    var (p, q) = (ship.OwnerOf[c], ship.OwnerOf[n]);
                    // 블록 사이 가압↔가압 변은 기본값 채우기가 모두 기록했으므로 기록이 없으면 KeyNotFoundException으로 드러난다
                    if (p != q && ship.Joints[JointId(p, q)].States[EdgeKey.Between(c, n)] == SeamEdge.Wall) continue;
                    seen.Add(n);
                    order.Add(n);
                    queue.Enqueue(n);
                }
            }
            return order;
        }

        // ───────────────────────── 프리셋 ─────────────────────────

        sealed class Preset
        {
            public readonly List<(string id, string type, Vector2Int origin, int rotation, int line)> HullBlocks = new();
            public Vector2Int Start;
            public int StartLine;
        }

        // 줄 문법은 계획서 2.3절. 오류 줄 번호는 주석과 빈 줄을 세는 파일 줄 번호다
        static Preset ParsePreset(string presetName, string text, Dictionary<string, BlockType> types)
        {
            var preset = new Preset();
            var ids = new Dictionary<string, int>(StringComparer.Ordinal);
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                int lineNo = i + 1;
                var line = lines[i].TrimEnd('\r');
                if (line.Trim().Length == 0 || line[0] == ';') continue;
                var w = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                string at = $"프리셋 '{presetName}' {lineNo}번째 줄";
                switch (w[0])
                {
                    case "block":
                        if (w.Length != 6) throw new FormatException($"{at}: block 줄은 'block <식별자> <종류이름> <x> <z> <회전>' 여섯 단어여야 한다(지금 {w.Length}개)");
                        if (ids.TryGetValue(w[1], out int prev)) throw new FormatException($"{at}: 블록 식별자 '{w[1]}'가 {prev}번째 줄과 겹친다");
                        // 접합부 식별자가 "작은쪽+큰쪽"이라 '+'가 식별자에 있으면 서로 다른 두 쌍이 같은 접합부 이름이 되어 변이 섞임
                        if (w[1].Contains('+')) throw new FormatException($"{at}: 블록 식별자 '{w[1]}'에 '+'가 있다. 접합부 식별자가 '작은쪽+큰쪽'이라 '+'를 쓸 수 없다");
                        if (!types.ContainsKey(w[2])) throw new FormatException($"{at}: 알 수 없는 종류 이름 '{w[2]}'. 블록 종류 목록에 그 이름의 텍스트가 없다");
                        int rotation = ParseInt(w[5], at);
                        if (rotation is not (0 or 90 or 180 or 270))
                            throw new FormatException($"{at}: 회전 '{w[5]}'는 0, 90, 180, 270 중 하나가 아니다(계획서 2.3절)");
                        ids[w[1]] = lineNo;
                        preset.HullBlocks.Add((w[1], w[2], new Vector2Int(ParseInt(w[3], at), ParseInt(w[4], at)), rotation, lineNo));
                        break;
                    case "start":
                        if (w.Length != 3) throw new FormatException($"{at}: start 줄은 'start <x> <z>' 세 단어여야 한다(지금 {w.Length}개)");
                        if (preset.StartLine != 0) throw new FormatException($"{at}: start 줄이 {preset.StartLine}번째 줄에 이미 있다. 정확히 하나여야 한다");
                        preset.Start = new Vector2Int(ParseInt(w[1], at), ParseInt(w[2], at));
                        preset.StartLine = lineNo;
                        break;
                    default:
                        // module 줄은 N3, 접합 변 덮어쓰기 줄은 그 뒤 단계에서 생긴다. N1에서는 알 수 없는 줄 종류다
                        throw new FormatException($"{at}: 알 수 없는 줄 종류 '{w[0]}'(N1은 block, start만 받는다)");
                }
            }
            if (preset.HullBlocks.Count == 0) throw new FormatException($"프리셋 '{presetName}'에 block 줄이 없다");
            if (preset.StartLine == 0) throw new FormatException($"프리셋 '{presetName}'에 start 줄이 없다. 정확히 하나여야 한다");
            return preset;
        }

        static int ParseInt(string word, string at) =>
            int.TryParse(word, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int v)
                ? v
                : throw new FormatException($"{at}: '{word}'는 정수가 아니다");

        // ───────────────────────── 블록 종류 ─────────────────────────

        // 글자 표와 검사는 계획서 2.5절 그대로. 표 밖의 글자('.' 포함)는 전부 예외
        static BlockType ParseBlockType(string name, string text)
        {
            var rows = new List<(string row, int fileLine)>();
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');
                if (line.Trim().Length == 0 || line[0] == ';') continue;
                rows.Add((line, i + 1));
            }
            if (rows.Count != TypeHeight)
                throw new FormatException(rows.Count > TypeHeight
                    ? $"블록 종류 '{name}' {rows[TypeHeight].fileLine}번째 줄: 격자 줄이 {rows.Count}개다. {TypeHeight}줄이어야 한다(3×5)"
                    : rows.Count == 0
                        ? $"블록 종류 '{name}': 격자 줄이 하나도 없다. {TypeHeight}줄이어야 한다(3×5)"
                        : $"블록 종류 '{name}': 격자 줄이 {rows.Count}개다(마지막 격자 줄 {rows[^1].fileLine}번째 줄). {TypeHeight}줄이어야 한다(3×5)");

            var t = new BlockType(name);
            var fileLineOfV = new int[TypeHeight];
            for (int i = 0; i < rows.Count; i++)
            {
                var (row, fileLine) = rows[i];
                if (row.Length != TypeWidth)
                    throw new FormatException($"블록 종류 '{name}' {fileLine}번째 줄: 길이가 {row.Length}다. {TypeWidth}글자여야 한다(3×5)");
                int v = TypeHeight - 1 - i;   // 첫 줄이 위쪽(+z)
                fileLineOfV[v] = fileLine;
                for (int u = 0; u < row.Length; u++)
                {
                    var c = new Vector2Int(u, v);
                    switch (row[u])
                    {
                        case 'o': t.Cells[c] = CellKind.Pressurized; break;
                        case '#': AddStructure(t, c, StructureRole.Fill); break;
                        case '%': AddStructure(t, c, StructureRole.Armor); break;
                        case '~': AddStructure(t, c, StructureRole.Tank); break;
                        case '^': AddStructure(t, c, StructureRole.Engine); break;
                        default:
                            throw new FormatException($"블록 종류 '{name}' {fileLine}번째 줄 {u + 1}번째 글자 '{row[u]}'는 알 수 없는 글자다(계획서 2.5절 글자 표 o # % ~ ^ 밖)");
                    }
                }
            }

            // 가압 칸은 정확히 2×2 직사각형 하나여야 한다(2.5절)
            var pressurized = new List<Vector2Int>();
            foreach (var (c, k) in t.Cells) if (k == CellKind.Pressurized) pressurized.Add(c);
            pressurized.Sort(CompareCells);
            if (pressurized.Count == 0)
                throw new FormatException($"블록 종류 '{name}' {rows[0].fileLine}~{rows[^1].fileLine}번째 줄: 가압 칸 'o'가 없다. 정확히 2×2 하나여야 한다");
            var min = pressurized[0];
            var max = pressurized[0];
            foreach (var c in pressurized) { min = Vector2Int.Min(min, c); max = Vector2Int.Max(max, c); }
            if (pressurized.Count != 4 || max - min != Vector2Int.one)
                throw new FormatException($"블록 종류 '{name}' {fileLineOfV[max.y]}번째 줄 {min.x + 1}번째 글자부터: 가압 칸 'o' {pressurized.Count}개의 경계 상자가 {max.x - min.x + 1}×{max.y - min.y + 1}다. 정확히 2×2 직사각형 하나여야 한다");
            return t;
        }

        static void AddStructure(BlockType t, Vector2Int c, StructureRole role)
        {
            t.Cells[c] = CellKind.Structure;
            t.Roles[c] = role;
        }

        // ───────────────────────── 통계 ─────────────────────────

        /// <summary>콘솔용 통계(6장 N1 수용 기준의 값들): 블록 수, 접합부 수, 접합 변 수(가압↔가압 따로), 고립 가압구역 수, 가압 비율.</summary>
        public static string Describe(Ship ship)
        {
            int pressurized = 0;
            foreach (var k in ship.KindOf.Values) if (k == CellKind.Pressurized) pressurized++;
            int edges = 0, ppEdges = 0;
            foreach (var joint in ship.Joints.Values) { edges += joint.Edges.Count; ppEdges += joint.States.Count; }

            var sb = new StringBuilder();
            sb.AppendLine($"[ShipNetworkRules] 프리셋 '{ship.PresetName}': 블록 {ship.HullBlocks.Count}개, 접합부 {ship.Joints.Count}개, 접합 변 {edges}개(가압↔가압 {ppEdges}개), 고립 가압구역 {ship.IsolatedZones.Count}개, 선체 {ship.KindOf.Count}칸, 가압 비율 {pressurized / (float)ship.KindOf.Count:P1}");
            foreach (var hullBlock in ship.HullBlocks)
                sb.AppendLine($"  블록 {hullBlock.Id}(종류 {hullBlock.Type.Name}, 위치 {hullBlock.Origin}, 회전 {hullBlock.Rotation})");
            foreach (var joint in ship.Joints.Values)
                sb.AppendLine($"  접합부 {joint.Id}: 접합 변 {joint.Edges.Count}(가압↔가압 {joint.States.Count})");
            for (int i = 0; i < ship.IsolatedZones.Count; i++)
            {
                var zone = ship.IsolatedZones[i];
                sb.AppendLine($"  고립 가압구역 {i + 1}: {zone.Count}칸, 첫 칸 {zone[0]}(블록 {ship.OwnerOf[zone[0]].Id})");
            }
            return sb.ToString().TrimEnd();
        }
    }
}
