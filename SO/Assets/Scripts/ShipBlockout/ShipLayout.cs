using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceOdyssey.Blockout
{
    /// <summary>
    /// 배 한 척의 배치 데이터 한 덩어리(설계 문서 0.5절). 생성기는 이것만 읽어 바닥, 벽, 가구, 외피, 구조층, 소품을 전부 만든다.
    /// 나중에 배치 패널이 이 데이터를 고치는 입력 장치가 된다. 좌표는 1m 칸 격자이고 +X가 뱃머리 방향이다.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceOdyssey/Blockout/Ship Layout")]
    public class ShipLayout : ScriptableObject
    {
        [Serializable]
        public class Room
        {
            public string name;
            public Vector2Int origin;   // 왼쪽 아래 칸
            public Vector2Int size;     // 칸 수

            [Tooltip("우주와 맞닿은 바깥 면에 창을 낸다. 창 면 앞에는 구조 칸을 채우지 않는다")]
            public bool windows;

            public Color floorColor = Color.gray;
        }

        /// <summary>문이 놓인 변. 두 칸은 서로 맞닿은 거주 칸이어야 한다.</summary>
        [Serializable]
        public class Door
        {
            public Vector2Int cellA;
            public Vector2Int cellB;
        }

        /// <summary>가구 자리(모듈이 생기기 전의 회색 상자).</summary>
        [Serializable]
        public class Furniture
        {
            public string name;
            public PrimitiveType shape;
            public Vector3 position;
            public Vector3 euler;
            public Vector3 scale = Vector3.one;
            public Color color = Color.gray;
            public bool emissive;
        }

        [Serializable]
        public class RoomLight
        {
            public string name;
            public Vector3 position;
            public float range = 8f;
            public Color color = Color.white;
        }

        /// <summary>배 시드. 형판 변형과 소품 선택이 이 값과 칸 좌표의 해시로 정해진다.</summary>
        [field: SerializeField]
        public int Seed { get; set; } = 1;

        [field: SerializeField]
        public List<Room> Rooms { get; set; } = new();

        [field: SerializeField]
        public List<Door> Doors { get; set; } = new();

        [field: SerializeField]
        public List<Furniture> FurnitureItems { get; set; } = new();

        [field: SerializeField]
        public List<RoomLight> Lights { get; set; } = new();

        /// <summary>구조 칸 목록(결정 1: 통째로 저장). 자동 채움은 이 목록을 새로 써 주는 도구이고, 생성기는 이 목록을 그대로 읽는다.</summary>
        [field: SerializeField]
        public List<StructureCell> StructureCells { get; set; } = new();

        static readonly Vector2Int[] Dirs = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };

        /// <summary>칸 → 방 번호(Rooms의 인덱스). 방이 겹치면 배치 데이터가 깨진 것이므로 예외를 던진다.</summary>
        public Dictionary<Vector2Int, int> RoomIndexByCell()
        {
            var map = new Dictionary<Vector2Int, int>();
            for (int i = 0; i < Rooms.Count; i++)
            {
                var r = Rooms[i];
                for (int x = 0; x < r.size.x; x++)
                    for (int z = 0; z < r.size.y; z++)
                    {
                        var c = r.origin + new Vector2Int(x, z);
                        if (map.TryGetValue(c, out int other))
                            throw new InvalidOperationException($"방 '{r.name}'과 '{Rooms[other].name}'이 칸 {c}에서 겹친다");
                        map[c] = i;
                    }
            }
            return map;
        }

        public HashSet<Vector2Int> HabitableCells() => new(RoomIndexByCell().Keys);

        /// <summary>창 면 목록: 창 있는 방의 칸 중 이웃이 거주 칸이 아닌 면(칸, 바깥 방향).</summary>
        public List<(Vector2Int cell, Vector2Int dir)> WindowFaces()
        {
            var roomOf = RoomIndexByCell();
            var faces = new List<(Vector2Int, Vector2Int)>();
            var cells = new List<Vector2Int>(roomOf.Keys);
            cells.Sort(CompareCells);
            foreach (var c in cells)
            {
                if (!Rooms[roomOf[c]].windows) continue;
                foreach (var d in Dirs)
                    if (!roomOf.ContainsKey(c + d)) faces.Add((c, d));
            }
            return faces;
        }

        /// <summary>문 변 집합. 문 두 칸이 맞닿은 거주 칸이 아니면 배치 데이터가 깨진 것이므로 예외를 던진다.</summary>
        public HashSet<EdgeKey> DoorEdges()
        {
            var roomOf = RoomIndexByCell();
            var set = new HashSet<EdgeKey>();
            foreach (var d in Doors)
            {
                var diff = d.cellB - d.cellA;
                if (Mathf.Abs(diff.x) + Mathf.Abs(diff.y) != 1 || !roomOf.ContainsKey(d.cellA) || !roomOf.ContainsKey(d.cellB))
                    throw new InvalidOperationException($"문 {d.cellA}-{d.cellB}가 맞닿은 두 거주 칸 사이가 아니다");
                set.Add(EdgeKey.Between(d.cellA, d.cellB));
            }
            return set;
        }

        /// <summary>좌표 정렬 기준(x 먼저, 그다음 z). 해시 집합 순회 순서에 기대지 않으려고 모든 계산이 이 순서를 쓴다.</summary>
        public static int CompareCells(Vector2Int a, Vector2Int b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y);
    }
}
