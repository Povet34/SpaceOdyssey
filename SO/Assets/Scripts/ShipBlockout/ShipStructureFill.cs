using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceOdyssey.Blockout
{
    /// <summary>
    /// 거주 칸에서 구조 칸 목록을 계산하는 자동 채움(계획서 3.6절). Unity 장면 객체에 의존하지 않는 순수 계산이다.
    /// 순서: 기본 띠 → 움푹한 곳 메우기 → 창 면 예외 → 형판 찍기 → 대칭 → 기존 칸 이어받기. 거주 칸은 어느 단계에서도 구조 칸이 되지 않는다.
    /// 같은 입력이면 같은 결과가 나오도록 모든 단계가 집합 연산이거나 정렬된 순서로 돈다.
    /// </summary>
    public static class ShipStructureFill
    {
        public const string BandSource = "Band";
        public const string CloseSource = "Close";

        static readonly Vector2Int[] Dirs4 = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
        static readonly Vector2Int[] Dirs8 =
        {
            Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down,
            new(1, 1), new(1, -1), new(-1, 1), new(-1, -1)
        };

        /// <summary>대칭 거울의 기준. 칸 z는 MirrorSum - z로 비친다. 연속 좌표의 중심선은 MirrorSum/2 + 0.5.</summary>
        public static int MirrorSum(IEnumerable<Vector2Int> habitable)
        {
            int minZ = int.MaxValue, maxZ = int.MinValue;
            foreach (var c in habitable) { minZ = Mathf.Min(minZ, c.y); maxZ = Mathf.Max(maxZ, c.y); }
            if (minZ == int.MaxValue) throw new ArgumentException("거주 칸이 하나도 없다");
            return minZ + maxZ;
        }

        public static List<StructureCell> Compute(
            IEnumerable<Vector2Int> habitable,
            IEnumerable<(Vector2Int cell, Vector2Int dir)> windowFaces,
            ShipStructureProfile profile,
            IReadOnlyList<StructureCell> previous)
        {
            var hab = new HashSet<Vector2Int>(habitable);
            int mirrorSum = MirrorSum(hab);
            int habMinX = int.MaxValue;
            foreach (var c in hab) habMinX = Mathf.Min(habMinX, c.x);

            var roles = new Dictionary<Vector2Int, (StructureRole role, string source)>();

            // 1. 기본 띠: 거주 칸에서 두께 안쪽의 빈 칸
            foreach (var c in Band(hab, profile.BandThickness, profile.Metric == ShipStructureProfile.BandMetric.Chamfered ? Dirs4 : Dirs8))
                roles[c] = (StructureRole.Fill, BandSource);

            // 2. 움푹한 곳 메우기: 넓혔다가 다시 좁히기(닫기)
            if (profile.CloseRadius > 0)
            {
                var solid = new HashSet<Vector2Int>(hab);
                solid.UnionWith(roles.Keys);
                foreach (var c in Close(solid, profile.CloseRadius))
                    if (!solid.Contains(c)) roles[c] = (StructureRole.Fill, CloseSource);
            }

            // 3. 창 면 예외: 창 앞으로 비워 둘 칸. 대칭이면 반대쪽도 비워서 대칭 단계가 다시 채우지 않게 한다
            foreach (var c in WindowClearance(hab, windowFaces, profile.WindowClearDepth, profile.Symmetry, mirrorSum))
                roles.Remove(c);

            // 4. 윤곽 형판: 창 면 예외 뒤에 찍으므로 형판은 창 앞을 덮을 수 있다(형판 설계자의 책임)
            foreach (var stamp in profile.Stamps)
            {
                if (!stamp.enabled) continue;
                var current = new HashSet<Vector2Int>(hab);
                current.UnionWith(roles.Keys);
                foreach (var c in StampCells(stamp, current, hab, mirrorSum, habMinX))
                    if (!hab.Contains(c)) roles[c] = (stamp.role, stamp.name);
            }

            // 5. 대칭: 거울 칸이 비었으면 복사, 둘 다 있으면 우선순위 높은 역할로 맞춤
            if (profile.Symmetry) Mirror(roles, hab, mirrorSum);

            // 6. 기존 칸 이어받기: 이전에도 있던 칸은 내구도를 유지, 새 칸은 가득 참. 사라진 칸의 손상은 버림(결정 2)
            var prevDurability = new Dictionary<Vector2Int, float>();
            if (previous != null)
                foreach (var p in previous) prevDurability[p.cell] = p.durability;

            var keys = new List<Vector2Int>(roles.Keys);
            keys.Sort(ShipLayout.CompareCells);
            var result = new List<StructureCell>(keys.Count);
            foreach (var c in keys)
            {
                var (role, source) = roles[c];
                result.Add(new StructureCell
                {
                    cell = c, role = role, source = source,
                    durability = prevDurability.TryGetValue(c, out float d) ? d : 1f
                });
            }
            return result;
        }

        /// <summary>정렬된 구조 칸 목록의 비교값(개수와 좌표, 역할의 해시). 결정론 확인용.</summary>
        public static uint Fingerprint(IReadOnlyList<StructureCell> cells)
        {
            unchecked
            {
                uint h = 2166136261u;
                foreach (var c in cells)
                {
                    h = (h ^ (uint)c.cell.x) * 16777619u;
                    h = (h ^ (uint)c.cell.y) * 16777619u;
                    h = (h ^ (uint)c.role) * 16777619u;
                }
                return h;
            }
        }

        static HashSet<Vector2Int> Band(HashSet<Vector2Int> hab, int thickness, Vector2Int[] dirs)
        {
            var dist = new Dictionary<Vector2Int, int>();
            var queue = new Queue<Vector2Int>();
            foreach (var c in hab) { dist[c] = 0; queue.Enqueue(c); }
            var band = new HashSet<Vector2Int>();
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                int d = dist[c];
                if (d >= thickness) continue;
                foreach (var dir in dirs)
                {
                    var n = c + dir;
                    if (dist.ContainsKey(n)) continue;
                    dist[n] = d + 1;
                    band.Add(n);
                    queue.Enqueue(n);
                }
            }
            return band;
        }

        // 정사각 구조 요소로 넓힌 뒤 다시 좁힌다. 폭이 2r 이하인 홈과 구멍이 메워진다.
        static HashSet<Vector2Int> Close(HashSet<Vector2Int> solid, int r)
        {
            var dilated = new HashSet<Vector2Int>();
            foreach (var c in solid)
                for (int dx = -r; dx <= r; dx++)
                    for (int dz = -r; dz <= r; dz++)
                        dilated.Add(c + new Vector2Int(dx, dz));

            var closed = new HashSet<Vector2Int>();
            foreach (var c in dilated)
            {
                bool all = true;
                for (int dx = -r; dx <= r && all; dx++)
                    for (int dz = -r; dz <= r && all; dz++)
                        all = dilated.Contains(c + new Vector2Int(dx, dz));
                if (all) closed.Add(c);
            }
            return closed;
        }

        static HashSet<Vector2Int> WindowClearance(HashSet<Vector2Int> hab, IEnumerable<(Vector2Int cell, Vector2Int dir)> faces, int depth, bool symmetric, int mirrorSum)
        {
            var clear = new HashSet<Vector2Int>();
            foreach (var (cell, dir) in faces)
                for (int k = 1; k <= depth; k++)
                {
                    var c = cell + dir * k;
                    if (hab.Contains(c)) break;
                    clear.Add(c);
                    if (symmetric) clear.Add(new Vector2Int(c.x, mirrorSum - c.y));
                }
            return clear;
        }

        static List<Vector2Int> StampCells(ShipStructureProfile.Stamp s, HashSet<Vector2Int> current, HashSet<Vector2Int> hab, int mirrorSum, int habMinX)
        {
            var cells = new List<Vector2Int>();
            float axisZ = mirrorSum * 0.5f + 0.5f;   // 연속 좌표의 중심선
            int len = s.length;

            if (s.anchor == ShipStructureProfile.StampAnchor.Side)
            {
                float centerX = habMinX + s.offset;
                // 기준 가장자리: 형판 폭 안에서 지금 선체의 가장 바깥(+z) 칸
                int baseZ = int.MinValue;
                foreach (var c in current)
                    if (Mathf.Abs(c.x + 0.5f - centerX) < s.width * 0.5f) baseZ = Mathf.Max(baseZ, c.y);
                if (baseZ == int.MinValue) foreach (var c in hab) baseZ = Mathf.Max(baseZ, c.y);

                for (int t = -s.overlap; t < len; t++)
                {
                    float w = WidthAt(s, t, len);
                    float shift = t <= 0 || len <= 1 ? 0f : -s.sweep * (float)t / (len - 1);
                    int z = baseZ + 1 + t;
                    for (int x = Mathf.FloorToInt(centerX - s.width - Mathf.Abs(s.sweep)) - 1; x <= Mathf.CeilToInt(centerX + s.width + Mathf.Abs(s.sweep)) + 1; x++)
                        if (Mathf.Abs(x + 0.5f - (centerX + shift)) < w * 0.5f) cells.Add(new Vector2Int(x, z));
                }
                return cells;
            }

            bool rear = s.anchor == ShipStructureProfile.StampAnchor.Rear;
            float centerZ = axisZ + s.offset;
            // 기준 가장자리: 형판 폭 안에서 지금 선체의 가장 뒤(또는 앞) 칸
            int baseX = rear ? int.MaxValue : int.MinValue;
            foreach (var c in current)
                if (Mathf.Abs(c.y + 0.5f - centerZ) < s.width * 0.5f)
                    baseX = rear ? Mathf.Min(baseX, c.x) : Mathf.Max(baseX, c.x);
            if (baseX == int.MaxValue || baseX == int.MinValue)
                foreach (var c in hab) baseX = rear ? Mathf.Min(baseX, c.x) : Mathf.Max(baseX, c.x);

            for (int t = -s.overlap; t < len; t++)
            {
                float w = WidthAt(s, t, len);
                int x = rear ? baseX - 1 - t : baseX + 1 + t;
                for (int z = Mathf.FloorToInt(centerZ - s.width) - 1; z <= Mathf.CeilToInt(centerZ + s.width) + 1; z++)
                    if (Mathf.Abs(z + 0.5f - centerZ) < w * 0.5f) cells.Add(new Vector2Int(x, z));
            }
            return cells;
        }

        // t는 기준 가장자리에서 바깥으로 몇 번째 줄인가(음수는 안쪽 겹침, 항상 원래 폭)
        static float WidthAt(ShipStructureProfile.Stamp s, int t, int len)
        {
            if (t < 0) return s.width;
            return s.shape switch
            {
                ShipStructureProfile.StampShape.Taper => len <= 1 ? s.width : Mathf.Lerp(s.width, s.tipWidth, (float)t / (len - 1)),
                ShipStructureProfile.StampShape.Dome => s.width * Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((t + 0.5f) / len, 2f))),
                _ => s.width,
            };
        }

        static void Mirror(Dictionary<Vector2Int, (StructureRole role, string source)> roles, HashSet<Vector2Int> hab, int mirrorSum)
        {
            var keys = new List<Vector2Int>(roles.Keys);
            keys.Sort(ShipLayout.CompareCells);
            foreach (var c in keys)
            {
                var m = new Vector2Int(c.x, mirrorSum - c.y);
                if (m == c || hab.Contains(m)) continue;
                if (!roles.TryGetValue(m, out var other))
                {
                    roles[m] = roles[c];
                    continue;
                }
                var mine = roles[c];
                if (other.role == mine.role) continue;
                var win = other.role > mine.role ? other : mine;
                roles[c] = win;
                roles[m] = win;
            }
        }
    }
}
