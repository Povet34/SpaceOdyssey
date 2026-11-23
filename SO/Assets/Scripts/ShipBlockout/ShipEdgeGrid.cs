using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceOdyssey.Blockout
{
    public enum EdgeKind { None, Wall, Door }

    /// <summary>
    /// 칸과 칸 사이의 변(가장자리) 하나. axis 0은 (x,z)와 (x+1,z) 사이, axis 1은 (x,z)와 (x,z+1) 사이다.
    /// 변은 두 칸이 함께 쓰므로 한 번만 저장된다.
    /// </summary>
    public readonly struct EdgeKey : IEquatable<EdgeKey>
    {
        public readonly int x, z, axis;
        public EdgeKey(int x, int z, int axis) { this.x = x; this.z = z; this.axis = axis; }

        public Vector2Int CellA => new Vector2Int(x, z);
        public Vector2Int CellB => axis == 0 ? new Vector2Int(x + 1, z) : new Vector2Int(x, z + 1);

        /// <summary>변의 한가운데(바닥 높이)의 월드 좌표.</summary>
        public Vector3 Center => axis == 0 ? new Vector3(x + 1f, 0f, z + 0.5f) : new Vector3(x + 0.5f, 0f, z + 1f);

        public static EdgeKey Between(Vector2Int a, Vector2Int b)
        {
            if (b.x == a.x + 1 && b.y == a.y) return new EdgeKey(a.x, a.y, 0);
            if (a.x == b.x + 1 && a.y == b.y) return new EdgeKey(b.x, b.y, 0);
            if (b.y == a.y + 1 && b.x == a.x) return new EdgeKey(a.x, a.y, 1);
            return new EdgeKey(b.x, b.y, 1);
        }

        public bool Equals(EdgeKey o) => x == o.x && z == o.z && axis == o.axis;
        public override bool Equals(object obj) => obj is EdgeKey o && Equals(o);
        public override int GetHashCode() => (x * 73856093) ^ (z * 19349663) ^ (axis * 83492791);
        public override string ToString() => $"Edge({x},{z},{axis})";
    }

    /// <summary>
    /// 선체 칸 집합과 칸 사이 변의 상태(없음, 벽, 문)를 저장하고, 벽과 문으로 둘러싸인 칸 덩어리(방)를 계산한다.
    /// 설계 문서 0.4절의 데이터 모델이다. 우주와 맞닿은 변은 선체가 소유하므로 여기에 저장하지 않는다.
    /// </summary>
    public class ShipEdgeGrid
    {
        public readonly HashSet<Vector2Int> Cells;
        readonly Dictionary<EdgeKey, EdgeKind> _edges = new Dictionary<EdgeKey, EdgeKind>();

        public Dictionary<Vector2Int, int> RoomOf { get; private set; } = new Dictionary<Vector2Int, int>();
        public List<int> RoomSizes { get; private set; } = new List<int>();
        public int RoomCount => RoomSizes.Count;

        static readonly Vector2Int[] Dirs = { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };

        public ShipEdgeGrid(HashSet<Vector2Int> cells) { Cells = cells; }

        public bool IsInterior(EdgeKey k) => Cells.Contains(k.CellA) && Cells.Contains(k.CellB);

        public EdgeKind Get(EdgeKey k) => _edges.TryGetValue(k, out var v) ? v : EdgeKind.None;

        /// <summary>변 상태를 바꾼다. 선체 안쪽 변이 아니거나 이미 같은 상태면 false.</summary>
        public bool Set(EdgeKey k, EdgeKind kind)
        {
            if (!IsInterior(k) || Get(k) == kind) return false;
            if (kind == EdgeKind.None) _edges.Remove(k); else _edges[k] = kind;
            return true;
        }

        public IEnumerable<KeyValuePair<EdgeKey, EdgeKind>> Edges => _edges;

        /// <summary>벽이나 문으로 막히지 않은 변으로 이어진 칸끼리 한 방으로 묶는다. 문은 방의 경계다.</summary>
        public void ComputeRooms()
        {
            var roomOf = new Dictionary<Vector2Int, int>();
            var sizes = new List<int>();
            var stack = new Stack<Vector2Int>();
            foreach (var start in Cells)
            {
                if (roomOf.ContainsKey(start)) continue;
                int id = sizes.Count, size = 0;
                roomOf[start] = id; stack.Push(start);
                while (stack.Count > 0)
                {
                    var c = stack.Pop(); size++;
                    foreach (var d in Dirs)
                    {
                        var n = c + d;
                        if (!Cells.Contains(n) || roomOf.ContainsKey(n)) continue;
                        if (Get(EdgeKey.Between(c, n)) != EdgeKind.None) continue;
                        roomOf[n] = id; stack.Push(n);
                    }
                }
                sizes.Add(size);
            }
            RoomOf = roomOf;
            RoomSizes = sizes;
        }

        /// <summary>start에서 걸어서 갈 수 있는 칸들(벽만 막고 문은 지나간다).</summary>
        public HashSet<Vector2Int> Reachable(Vector2Int start)
        {
            var seen = new HashSet<Vector2Int>();
            if (!Cells.Contains(start)) return seen;
            var stack = new Stack<Vector2Int>();
            seen.Add(start); stack.Push(start);
            while (stack.Count > 0)
            {
                var c = stack.Pop();
                foreach (var d in Dirs)
                {
                    var n = c + d;
                    if (!Cells.Contains(n) || seen.Contains(n)) continue;
                    if (Get(EdgeKey.Between(c, n)) == EdgeKind.Wall) continue;
                    seen.Add(n); stack.Push(n);
                }
            }
            return seen;
        }

        /// <summary>start에서 걸어갈 수 없는 방의 수.</summary>
        public int UnreachableRoomCount(Vector2Int start)
        {
            var reach = Reachable(start);
            var bad = new HashSet<int>();
            foreach (var kv in RoomOf) if (!reach.Contains(kv.Key)) bad.Add(kv.Value);
            return bad.Count;
        }
    }
}
