using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace SpaceOdyssey.Blockout
{
    /// <summary>블록 칸 지도의 칸 종류. 통로 칸과 방칸이 가압구역, 설비 칸이 설비구역이다.</summary>
    public enum CellKind { Corridor, Room, Structure }

    /// <summary>양쪽이 가압 칸인 접합 변의 상태(계획서 2.5절 표의 접합 변 열).</summary>
    public enum SeamEdge { Open, Wall, Barrier }

    /// <summary>
    /// 통로 그물 A1(Docs/ShipLayoutNetworkPlan.md 11절 A1)의 규칙 계산. 장면 객체에 의존하지 않는다.
    /// 입력은 문자열(프리셋 텍스트, 형판 이름별 텍스트), 출력은 칸 좌표와 종류의 평범한 C# 자료다.
    /// 형판 파서(2.7절), 프리셋 파서(2.6절), 형판 검사, 블록 합치기, 접합부 계산과 접합 변 기본값 채우기(2.5절),
    /// 배 전체 검사, 콘솔용 통계가 여기 있다. B1a에서 입력이 형식 2 데이터 타입으로 바뀐다.
    /// 입력은 사람이 손으로 쓴 텍스트(신뢰 경계)라서 규칙을 어기면 조용히 고치지 않고 예외로 멈춘다.
    /// </summary>
    public static class ShipNetworkRules
    {
        /// <summary>형판 하나를 읽고 검사한 결과. 좌표는 형판 자기 좌표(맨 아래 줄 첫 글자가 (0,0)).</summary>
        public sealed class Template
        {
            public readonly string Name;
            public readonly Dictionary<Vector2Int, CellKind> Cells = new();
            public readonly Dictionary<Vector2Int, StructureRole> Roles = new();
            /// <summary>통로 중간 차단막(차단막 글자). 블록 내부 변이다.</summary>
            public readonly HashSet<EdgeKey> Barriers = new();
            /// <summary>방 구획(직사각형 방칸 덩어리)과 그 문 방칸, 문 앞 통로 칸.</summary>
            public readonly List<RoomBlock> Blocks = new();
            public readonly List<string> Warnings = new();
            internal readonly List<Vector2Int> DoorCells = new();
            internal readonly Dictionary<int, int> FileLineOfZ = new();
            internal int CorridorCount;
            internal bool Validated;
            public Template(string name) { Name = name; }
        }

        public sealed class RoomBlock
        {
            public Vector2Int Min, Max;
            public readonly List<(Vector2Int door, Vector2Int front)> Doors = new();
        }

        /// <summary>배에 놓인 블록. 칸은 형판 칸을 Offset만큼 옮긴 것이다(회전 없음).</summary>
        public sealed class HullBlock
        {
            public string Id;
            public Template Template;
            public Vector2Int Offset;
            public int PresetLine;
            public int Index;   // 프리셋에 적힌 순서. 표시 색을 고를 때만 쓴다
        }

        /// <summary>블록 식별자 쌍 하나. 접합 변은 두 칸이 서로 다른 블록인 인접 칸 쌍의 변 전부(2.5절).</summary>
        public sealed class Joint
        {
            public string Id;
            public HullBlock A, B;
            public readonly List<EdgeKey> Edges = new();
            /// <summary>양쪽이 가압 칸인 접합 변만 기록한다. 열린 변도 빠짐없이 적는다(2.5절).</summary>
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
            public readonly List<(Vector2Int door, Vector2Int front)> Doors = new();
            /// <summary>통로 중간 차단막(블록 내부 변, 배 좌표). 접합부 차단막은 Joints에 있다.</summary>
            public readonly HashSet<EdgeKey> InnerBarriers = new();
            public readonly SortedDictionary<string, Joint> Joints = new(StringComparer.Ordinal);
            public readonly List<string> Warnings = new();
            public Vector2Int Start;
            public bool IsPressurized(Vector2Int c) => KindOf.TryGetValue(c, out var k) && k != CellKind.Structure;
        }

        static readonly Vector2Int[] Dirs = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };

        // 방 구획 크기 규격(짧은 변, 긴 변). 계획서 4.2절. 작은 방(2×2, 2×3)은 2026-11-24 사용자 결정
        static readonly HashSet<(int, int)> RoomSizes = new() { (2, 2), (2, 3), (3, 3), (3, 4), (4, 4), (4, 5) };

        public static int CompareCells(Vector2Int a, Vector2Int b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y);

        // ───────────────────────── 배 만들기 ─────────────────────────

        /// <summary>
        /// 프리셋에서 배를 만든다: 프리셋 읽기 → 형판 읽기와 검사 → 배 좌표로 합치기(겹침 검사) → 접합부 계산 →
        /// 접합 변 기본값 채우기 → 배 전체 검사(2.6절 순서).
        /// </summary>
        public static Ship Build(string presetName, string presetText, IReadOnlyList<(string name, string text)> templates)
        {
            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (name, text) in templates)
                if (!texts.TryAdd(name, text))
                    throw new ArgumentException($"이름이 같은 형판이 둘 있다: '{name}'. 프리셋이 형판을 파일 이름으로 찾으므로 유일해야 한다");

            var preset = ParsePreset(presetName, presetText, texts);
            var ship = new Ship { PresetName = presetName };
            var parsed = new Dictionary<string, Template>(StringComparer.Ordinal);
            foreach (var (id, templateName, offset, line) in preset.HullBlocks)
            {
                if (!parsed.TryGetValue(templateName, out var t))
                    parsed[templateName] = t = ParseTemplate(templateName, texts[templateName]);
                // 검사의 통로 연결은 정렬 첫 통로 칸에서 시작하므로 통로 0칸이면 그보다 먼저 멈춘다(계획서 2.8절)
                if (t.CorridorCount == 0)
                    throw new InvalidOperationException($"프리셋 '{presetName}' {line}번째 줄: 블록 '{id}'(형판 '{t.Name}')에 통로 칸이 없음. 통로 없는 블록은 A1에서 허용하지 않는다(계획서 2.8절)");
                if (!t.Validated)
                {
                    ValidateTemplate(t);
                    ship.Warnings.AddRange(t.Warnings);
                }

                var hullBlock = new HullBlock { Id = id, Template = t, Offset = offset, PresetLine = line, Index = ship.HullBlocks.Count };
                Place(ship, hullBlock);
            }

            ship.Start = preset.Start;
            if (!ship.KindOf.TryGetValue(preset.Start, out var startKind) || startKind != CellKind.Corridor)
                throw new InvalidOperationException($"프리셋 '{presetName}' {preset.StartLine}번째 줄: 시작 칸 {preset.Start}이 통로 칸이 아니다");

            ComputeJoints(ship);
            FillJointDefaults(ship);
            ValidateShip(ship);
            return ship;
        }

        static void Place(Ship ship, HullBlock hullBlock)
        {
            var t = hullBlock.Template;
            var cells = new List<Vector2Int>(t.Cells.Keys);
            cells.Sort(CompareCells);
            foreach (var local in cells)
            {
                var c = local + hullBlock.Offset;
                if (ship.OwnerOf.TryGetValue(c, out var other))
                    throw new InvalidOperationException($"프리셋 '{ship.PresetName}' {hullBlock.PresetLine}번째 줄: 블록 겹침. 배 칸 {c}에 블록 '{other.Id}'와 '{hullBlock.Id}'가 둘 다 있다");
                ship.OwnerOf[c] = hullBlock;
                ship.KindOf[c] = t.Cells[local];
                if (t.Roles.TryGetValue(local, out var role)) ship.RoleOf[c] = role;
            }
            foreach (var block in t.Blocks)
                foreach (var (door, front) in block.Doors)
                    ship.Doors.Add((door + hullBlock.Offset, front + hullBlock.Offset));
            foreach (var k in t.Barriers)
                ship.InnerBarriers.Add(new EdgeKey(k.x + hullBlock.Offset.x, k.z + hullBlock.Offset.y, k.axis));
            ship.HullBlocks.Add(hullBlock);
        }

        // 접합부 = 블록 식별자의 순서 없는 쌍마다 하나. 식별자는 서수 비교로 정렬한 "작은쪽+큰쪽"(2.5절)
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
        /// 접합 변 기본값 채우기(2.5절 쓰기 경로 4): 양쪽이 가압 칸이고 기록이 없는 접합 변에 표의 기본값을 쓴다.
        /// 이미 기록이 있는 변은 건드리지 않는다. 통로↔통로 차단막, 통로↔방칸 벽, 방칸↔방칸 열린 변(명시 기록).
        /// </summary>
        public static void FillJointDefaults(Ship ship)
        {
            foreach (var joint in ship.Joints.Values)
                foreach (var e in joint.Edges)
                {
                    if (!ship.IsPressurized(e.CellA) || !ship.IsPressurized(e.CellB) || joint.States.ContainsKey(e)) continue;
                    var ka = ship.KindOf[e.CellA];
                    var kb = ship.KindOf[e.CellB];
                    joint.States[e] = ka == CellKind.Corridor && kb == CellKind.Corridor ? SeamEdge.Barrier
                        : ka == CellKind.Room && kb == CellKind.Room ? SeamEdge.Open
                        : SeamEdge.Wall;
                }
        }

        static void ValidateShip(Ship ship)
        {
            string where = $"배(프리셋 '{ship.PresetName}')";
            foreach (var joint in ship.Joints.Values)
                foreach (var e in joint.Edges)
                    if (ship.IsPressurized(e.CellA) && ship.IsPressurized(e.CellB) && !joint.States.ContainsKey(e))
                        throw new InvalidOperationException($"{where} 접합부 {joint.Id}의 가압↔가압 접합 변 {e}에 기록이 없다(기본값 채우기를 거치지 않음)");

            var corridor = new HashSet<Vector2Int>();
            foreach (var (c, k) in ship.KindOf) if (k == CellKind.Corridor) corridor.Add(c);
            var fronts = new HashSet<Vector2Int>();
            foreach (var (_, front) in ship.Doors) fronts.Add(front);
            // 배 전체에서 다시 돌림: 블록을 붙이면 경계에 있던 통로 끝이 더는 우주에 닿지 않아 앵커 자격을 잃는다(3.3절)
            CheckCorridors(where, corridor, c => ship.OwnerOf.ContainsKey(c), fronts);
        }

        // ───────────────────────── 프리셋 ─────────────────────────

        sealed class Preset
        {
            public readonly List<(string id, string template, Vector2Int offset, int line)> HullBlocks = new();
            public Vector2Int Start;
            public int StartLine;
        }

        // 줄 문법은 계획서 2.6절. 오류 줄 번호는 주석과 빈 줄을 세는 파일 줄 번호다
        static Preset ParsePreset(string presetName, string text, Dictionary<string, string> templates)
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
                        if (w.Length != 5) throw new FormatException($"{at}: block 줄은 'block <식별자> <형판이름> <x> <z>' 다섯 단어여야 한다(지금 {w.Length}개)");
                        if (ids.TryGetValue(w[1], out int prev)) throw new FormatException($"{at}: 블록 식별자 '{w[1]}'가 {prev}번째 줄과 겹친다");
                        // 접합부 식별자가 "작은쪽+큰쪽"이라 '+'가 식별자에 있으면 서로 다른 두 쌍이 같은 접합부 이름이 되어 변이 섞임(리뷰 지적 1)
                        if (w[1].Contains('+')) throw new FormatException($"{at}: 블록 식별자 '{w[1]}'에 '+'가 있다. 접합부 식별자가 '작은쪽+큰쪽'이라 '+'를 쓸 수 없다");
                        if (!templates.ContainsKey(w[2])) throw new FormatException($"{at}: 알 수 없는 형판 이름 '{w[2]}'. 형판 목록에 그 이름의 텍스트가 없다");
                        ids[w[1]] = lineNo;
                        preset.HullBlocks.Add((w[1], w[2], new Vector2Int(ParseInt(w[3], at), ParseInt(w[4], at)), lineNo));
                        break;
                    case "start":
                        if (w.Length != 3) throw new FormatException($"{at}: start 줄은 'start <x> <z>' 세 단어여야 한다(지금 {w.Length}개)");
                        if (preset.StartLine != 0) throw new FormatException($"{at}: start 줄이 {preset.StartLine}번째 줄에 이미 있다. 정확히 하나여야 한다");
                        preset.Start = new Vector2Int(ParseInt(w[1], at), ParseInt(w[2], at));
                        preset.StartLine = lineNo;
                        break;
                    default:
                        // module 줄은 A2, 접합 변 덮어쓰기 줄은 B1a에서 생긴다. A1에서는 알 수 없는 줄 종류다
                        throw new FormatException($"{at}: 알 수 없는 줄 종류 '{w[0]}'(A1은 block, start만 받는다)");
                }
            }
            if (preset.HullBlocks.Count == 0) throw new FormatException($"프리셋 '{presetName}'에 block 줄이 없다");
            if (preset.StartLine == 0) throw new FormatException($"프리셋 '{presetName}'에 start 줄이 없다. 정확히 하나여야 한다");
            return preset;
        }

        static int ParseInt(string word, string at) =>
            int.TryParse(word, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int v)
                ? v
                : throw new FormatException($"{at}: '{word}'는 정수 좌표가 아니다");

        // ───────────────────────── 형판 ─────────────────────────

        // 글자 대응표는 계획서 2.7절 그대로. 표 밖의 글자는 전부 예외(A0 격자의 대문자 포함)
        static Template ParseTemplate(string name, string text)
        {
            var rows = new List<(string row, int fileLine)>();
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');
                if (line.Length == 0 || line[0] == ';') continue;
                rows.Add((line, i + 1));
            }
            if (rows.Count == 0) throw new FormatException($"형판 '{name}'에 격자 줄이 없다");

            var t = new Template(name);
            int width = rows[0].row.Length;
            for (int i = 0; i < rows.Count; i++)
            {
                var (row, fileLine) = rows[i];
                if (row.Length != width)
                    throw new FormatException($"형판 '{name}' {fileLine}번째 줄의 길이({row.Length})가 첫 격자 줄({width})과 다르다");
                int z = rows.Count - 1 - i;
                t.FileLineOfZ[z] = fileLine;
                for (int x = 0; x < row.Length; x++)
                {
                    var c = new Vector2Int(x, z);
                    char ch = row[x];
                    switch (ch)
                    {
                        case '.': break;
                        case '#': AddStructure(t, c, StructureRole.Fill); break;
                        case '%': AddStructure(t, c, StructureRole.Armor); break;
                        case '~': AddStructure(t, c, StructureRole.Tank); break;
                        case '^': AddStructure(t, c, StructureRole.Engine); break;
                        case '+': t.Cells[c] = CellKind.Corridor; break;
                        case '|': t.Cells[c] = CellKind.Corridor; t.Barriers.Add(new EdgeKey(x, z, 0)); break;
                        case '-': t.Cells[c] = CellKind.Corridor; t.Barriers.Add(new EdgeKey(x, z, 1)); break;
                        case 'o': t.Cells[c] = CellKind.Room; break;
                        case 'd': t.Cells[c] = CellKind.Room; t.DoorCells.Add(c); break;
                        case '@':
                            throw new FormatException($"형판 '{name}' {fileLine}번째 줄 {x + 1}번째 글자 '@': 형판에는 시작 칸을 두지 않는다. 시작 칸은 프리셋의 start 줄이 정한다(계획서 2.7절)");
                        default:
                            throw new FormatException($"형판 '{name}' {fileLine}번째 줄 {x + 1}번째 글자 '{ch}'는 알 수 없는 글자다(계획서 2.7절 글자 대응표 밖)");
                    }
                    if (ch is '+' or '|' or '-') t.CorridorCount++;
                }
            }
            return t;
        }

        static void AddStructure(Template t, Vector2Int c, StructureRole role)
        {
            t.Cells[c] = CellKind.Structure;
            t.Roles[c] = role;
        }

        // 형판 검사: A0 검사(방 구획 모양과 규격, 문, 통로 연결, 폭, 막다른 길) + 2.7절 구멍 처리(차단막 글자, 문 둘 구획 경고)
        static void ValidateTemplate(Template t)
        {
            string Where(Vector2Int c) => $"형판 '{t.Name}' {t.FileLineOfZ[c.y]}번째 줄 {c.x + 1}번째 칸";

            // 방 구획: 4방향으로 이어진 방칸 덩어리 하나가 경계 상자를 꽉 채워야 직사각형이다
            var doorSet = new HashSet<Vector2Int>(t.DoorCells);
            var roomCells = new List<Vector2Int>();
            foreach (var (c, k) in t.Cells) if (k == CellKind.Room) roomCells.Add(c);
            roomCells.Sort(CompareCells);
            var seen = new HashSet<Vector2Int>();
            foreach (var start in roomCells)
            {
                if (!seen.Add(start)) continue;
                var block = new RoomBlock { Min = start, Max = start };
                var members = new List<Vector2Int>();
                var stack = new Stack<Vector2Int>();
                stack.Push(start);
                while (stack.Count > 0)
                {
                    var c = stack.Pop();
                    members.Add(c);
                    block.Min = Vector2Int.Min(block.Min, c);
                    block.Max = Vector2Int.Max(block.Max, c);
                    foreach (var d in Dirs)
                        if (t.Cells.TryGetValue(c + d, out var k) && k == CellKind.Room && seen.Add(c + d)) stack.Push(c + d);
                }
                int w = block.Max.x - block.Min.x + 1, h = block.Max.y - block.Min.y + 1;
                if (w * h != members.Count)
                    throw new InvalidOperationException($"{Where(block.Min)}부터의 방 구획이 직사각형이 아니다(경계 상자 {w}×{h}, 방칸 {members.Count}개)");
                if (!RoomSizes.Contains((Mathf.Min(w, h), Mathf.Max(w, h))))
                    throw new InvalidOperationException($"{Where(block.Min)}부터의 방 구획 크기 {w}×{h}는 규격(2×2, 2×3, 3×3~4×4, 4×5) 밖이다");

                members.Sort(CompareCells);
                foreach (var door in members)
                {
                    if (!doorSet.Contains(door)) continue;
                    // 문 방칸은 통로 칸 하나와만 맞닿아야 문 변이 하나로 정해진다(2.7절, 자동으로 고르지 않음)
                    var touching = new List<Vector2Int>();
                    foreach (var d in Dirs)
                        if (t.Cells.TryGetValue(door + d, out var k) && k == CellKind.Corridor) touching.Add(door + d);
                    if (touching.Count != 1)
                        throw new InvalidOperationException($"{Where(door)}의 문 방칸 'd'가 통로 칸 {touching.Count}개와 맞닿는다. 정확히 하나여야 한다");
                    block.Doors.Add((door, touching[0]));
                }
                if (block.Doors.Count == 0)
                    throw new InvalidOperationException($"{Where(block.Min)}부터의 방 구획 {w}×{h}에 문 방칸 'd'가 없다. 통로에서 들어갈 수 없다");
                // 맞붙여 그린 두 구획이 규격 안 직사각형이 되면 한 구획으로 통과하므로 문 개수로 알린다(2.7절 구멍 가)
                if (block.Doors.Count > 1)
                    t.Warnings.Add($"{Where(block.Min)}부터의 방 구획 {w}×{h}에 문이 {block.Doors.Count}개 있다(문 칸 {string.Join(", ", block.Doors.ConvertAll(x => x.door.ToString()))}). 맞붙여 그린 두 구획이 하나로 읽혔을 수 있다");
                t.Blocks.Add(block);
            }

            // 차단막 글자가 가리키는 변 너머가 같은 블록의 통로 칸이어야 한다(2.7절 구멍 다). 접합부 차단막은 자동이라 블록 밖도 안 됨
            foreach (var k in t.Barriers)
                if (!t.Cells.TryGetValue(k.CellB, out var kind) || kind != CellKind.Corridor)
                    throw new InvalidOperationException($"{Where(k.CellA)}의 차단막 글자가 가리키는 변 너머 칸 {k.CellB}이 같은 블록의 통로 칸이 아니다");

            var corridor = new HashSet<Vector2Int>();
            foreach (var (c, k) in t.Cells) if (k == CellKind.Corridor) corridor.Add(c);
            var fronts = new HashSet<Vector2Int>();
            foreach (var b in t.Blocks) foreach (var (_, front) in b.Doors) fronts.Add(front);
            // 블록 하나의 통로는 그 안에서 한 덩어리여야 한다(떼어 낸 블록도 혼자서 말이 되는 가방, 2.8절)
            CheckCorridors($"형판 '{t.Name}'", corridor, c => t.Cells.ContainsKey(c), fronts);
            t.Validated = true;
        }

        // ───────────────────────── 통로 검사 (형판과 배 전체 공용) ─────────────────────────

        // 통로 연결, 폭, 막다른 길. inHull 밖에 닿은 통로 칸은 확장 끝점이라 앵커다(3.5절)
        static void CheckCorridors(string where, HashSet<Vector2Int> corridor, Func<Vector2Int, bool> inHull, HashSet<Vector2Int> doorFronts)
        {
            var sorted = new List<Vector2Int>(corridor);
            sorted.Sort(CompareCells);

            // 통로 연결: 정렬 순서 첫 통로 칸에서 퍼져서 전부 닿아야 함(3.4절의 시작 칸 정의). 차단막은 문이라 지나간다
            var seen = new HashSet<Vector2Int> { sorted[0] };
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(sorted[0]);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                foreach (var d in Dirs)
                    if (corridor.Contains(c + d) && seen.Add(c + d)) queue.Enqueue(c + d);
            }
            if (seen.Count != corridor.Count)
            {
                var lost = sorted.Find(c => !seen.Contains(c));
                throw new InvalidOperationException($"{where}: 통로가 끊겨 있다. {corridor.Count - seen.Count}칸이 나머지와 이어지지 않음(첫 칸 {lost})");
            }

            // 통로 폭: 3×3이 통째로 통로면 폭 3 이상. 간선은 2칸까지(3.2절)
            foreach (var c in sorted)
                if (IsFullBlock(corridor, c, 3))
                    throw new InvalidOperationException($"{where}: 통로가 {c}에서 폭 3 이상이다. 간선은 2칸, 지선은 1칸");

            var anchors = new HashSet<Vector2Int>(doorFronts);
            foreach (var c in sorted)
                foreach (var d in Dirs)
                    if (!inHull(c + d)) anchors.Add(c);
            var ends = DeadEnds(corridor, sorted, anchors);
            if (ends.Count > 0)
            {
                var (a, b) = ends[0];
                throw new InvalidOperationException(a == b
                    ? $"{where}: 폭 1 통로가 {a}에서 앵커 없이 끝난다(막다른 길)"
                    : $"{where}: 폭 2 통로가 {a}, {b}에서 앵커 없이 끝난다(막다른 길)");
            }
        }

        // 막다른 길 판정은 A0 근사를 그대로 옮긴 것이며 B1a에서 정식 판정으로 바꾼다(계획서 3.3절. 고리 모양 막다른 가지를 못 잡음).
        // 끝 모양 두 가지만 본다: 통로 이웃이 하나 이하인 폭 1 끝(a == b로 돌려줌), 나란한 두 칸이 같은 쪽으로 막히고 뒤로만 이어지는 폭 2 끝
        static List<(Vector2Int a, Vector2Int b)> DeadEnds(HashSet<Vector2Int> corridor, List<Vector2Int> sorted, HashSet<Vector2Int> anchors)
        {
            var ends = new List<(Vector2Int, Vector2Int)>();
            foreach (var c in sorted)
            {
                if (anchors.Contains(c)) continue;
                int n = 0;
                foreach (var d in Dirs) if (corridor.Contains(c + d)) n++;
                if (n <= 1) { ends.Add((c, c)); continue; }

                // 폭 2 끝: c와 옆 칸 p가 나란히 앞(f)이 막혔고, 뒤로만 이어지고, 바깥 옆이 둘 다 막힌 모양
                foreach (var f in Dirs)
                {
                    var s = new Vector2Int(f.y, -f.x);
                    foreach (var side in new[] { s, -s })
                    {
                        var p = c + side;
                        if (!corridor.Contains(p) || anchors.Contains(p)) continue;
                        if (corridor.Contains(c + f) || corridor.Contains(p + f)) continue;
                        if (!corridor.Contains(c - f) || !corridor.Contains(p - f)) continue;
                        if (corridor.Contains(c - side) || corridor.Contains(p + side)) continue;
                        ends.Add((c, p));
                    }
                }
            }
            return ends;
        }

        static bool IsFullBlock(HashSet<Vector2Int> set, Vector2Int origin, int size)
        {
            for (int dx = 0; dx < size; dx++)
                for (int dz = 0; dz < size; dz++)
                    if (!set.Contains(origin + new Vector2Int(dx, dz))) return false;
            return true;
        }

        /// <summary>2×2가 통째로 통로인 묶음에 들어가는 칸 = 간선(3.3절: 폭을 보고 계산).</summary>
        public static bool IsTrunk(HashSet<Vector2Int> corridor, Vector2Int c)
        {
            for (int dx = -1; dx <= 0; dx++)
                for (int dz = -1; dz <= 0; dz++)
                {
                    var o = c + new Vector2Int(dx, dz);
                    if (corridor.Contains(o) && corridor.Contains(o + Vector2Int.right) &&
                        corridor.Contains(o + Vector2Int.up) && corridor.Contains(o + Vector2Int.one)) return true;
                }
            return false;
        }

        // ───────────────────────── 통계 ─────────────────────────

        /// <summary>콘솔용 통계(11절 A1 수용 기준의 값들). 판정은 하지 않고 통과선만 같이 적는다.</summary>
        public static string Describe(Ship ship)
        {
            var corridor = new HashSet<Vector2Int>();
            int pressurized = 0;
            foreach (var (c, k) in ship.KindOf)
            {
                if (k == CellKind.Corridor) corridor.Add(c);
                if (k != CellKind.Structure) pressurized++;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"[ShipNetworkRules] 프리셋 '{ship.PresetName}': 블록 {ship.HullBlocks.Count}개, 접합부 {ship.Joints.Count}개, 선체 {ship.KindOf.Count}칸, 가압 비율 {pressurized / (float)ship.KindOf.Count:P1}");

            int trunkDirect = 0;
            foreach (var hullBlock in ship.HullBlocks)
            {
                int n0 = 0, n1 = 0, n2 = 0;
                foreach (var k in hullBlock.Template.Cells.Values)
                    if (k == CellKind.Corridor) n0++; else if (k == CellKind.Room) n1++; else n2++;
                sb.AppendLine($"  블록 {hullBlock.Id}(형판 {hullBlock.Template.Name}, 위치 {hullBlock.Offset}): 통로 {n0}, 방칸 {n1}(방 구획 {hullBlock.Template.Blocks.Count}개), 설비 {n2}");
                foreach (var block in hullBlock.Template.Blocks)
                    if (block.Doors.Exists(x => IsTrunk(corridor, x.front + hullBlock.Offset))) trunkDirect++;
            }

            int jointBarriers = 0;
            foreach (var joint in ship.Joints.Values)
            {
                int cc = 0, cr = 0, rr = 0, barrier = 0;
                foreach (var (e, state) in joint.States)
                {
                    var ka = ship.KindOf[e.CellA];
                    var kb = ship.KindOf[e.CellB];
                    if (ka == CellKind.Corridor && kb == CellKind.Corridor) cc++;
                    else if (ka == CellKind.Room && kb == CellKind.Room) rr++;
                    else cr++;
                    if (state == SeamEdge.Barrier) barrier++;
                }
                jointBarriers += barrier;
                sb.AppendLine($"  접합부 {joint.Id}: 접합 변 {joint.Edges.Count}(가압↔가압 {joint.States.Count} = 통로↔통로 {cc}, 통로↔방칸 {cr}, 방칸↔방칸 {rr}), 차단막 {barrier}");
            }
            sb.AppendLine($"  차단막 변: 접합부 {jointBarriers} + 통로 중간 {ship.InnerBarriers.Count} = {jointBarriers + ship.InnerBarriers.Count}");
            sb.AppendLine($"  간선 직결 방 구획: {trunkDirect} (통과선 3 이하)");

            // 방 문 앞이 아닌 통로 끝 모양 칸 중 우주에 닿는 칸. 우주 닿음을 앵커로 치지 않고 끝 모양만 본다.
            // 막다른 길 근사(DeadEnds의 끝 모양 두 가지)에 기대므로 이 통계도 근사임. B1a에서 막다른 길 판정과 같이 정식으로 교체함
            var sorted = new List<Vector2Int>(corridor);
            sorted.Sort(CompareCells);
            var fronts = new HashSet<Vector2Int>();
            foreach (var (_, front) in ship.Doors) fronts.Add(front);
            var endCells = new HashSet<Vector2Int>();
            foreach (var (a, b) in DeadEnds(corridor, sorted, fronts)) { endCells.Add(a); endCells.Add(b); }
            int spaceEnds = 0;
            foreach (var c in endCells)
                foreach (var d in Dirs)
                    if (!ship.OwnerOf.ContainsKey(c + d)) { spaceEnds++; break; }
            sb.Append($"  문 없이 우주에 닿는 통로 끝 칸: {spaceEnds} (통과선 0)");
            return sb.ToString();
        }
    }
}
