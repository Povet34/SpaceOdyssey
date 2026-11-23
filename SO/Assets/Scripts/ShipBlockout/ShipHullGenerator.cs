using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceOdyssey.Blockout
{
    /// <summary>
    /// 방 배치(칸 목록)를 보고 외피 프리팹을 붙이는 생성기. 설계 문서 3장의 "조각 키트" 방식의 프로토타입이다.
    /// 칸의 변마다 "이웃이 비어 있으면 벽 조각", 꼭짓점마다 "양쪽이 비어 있으면 모서리 조각"을 놓고,
    /// 모든 칸에 지붕과 밑판 타일을 놓는다. Inspector의 컨텍스트 메뉴(⋮)에서 Generate Hull을 실행한다.
    /// </summary>
    public class ShipHullGenerator : MonoBehaviour
    {
        [Serializable]
        public class RoomDef
        {
            public string name;
            public Vector2Int origin;          // 왼쪽 아래 칸
            public Vector2Int size;            // 칸 수
            public bool windows;               // 바깥 벽에 창을 낸다
            public bool windowsEverywhere;     // 창을 모든 칸에 낸다(아니면 한 칸 건너 하나)
            public GameObject roofCap;         // 방 지붕 덮개 프리팹(선택)
        }

        [Serializable]
        public class PlacedPart
        {
            public string name;
            public GameObject prefab;
            public Vector3 position;
            public Vector3 eulerAngles;
        }

        [Header("조각 키트")]
        public GameObject wall;
        public GameObject wallWindow;
        public GameObject cornerOut;
        public GameObject roof;
        public GameObject belly;

        [Header("배치")]
        public List<RoomDef> rooms = new List<RoomDef>();
        public List<PlacedPart> extraParts = new List<PlacedPart>();   // 엔진, 뱃머리, 용골처럼 칸 규격의 큰 부품

        [Tooltip("엔진처럼 칸을 차지하지만 방은 아닌 곳(왼쪽 아래 칸, 2×2). 바깥 벽을 만들지 않는다.")]
        public List<Vector2Int> solidBlocks = new List<Vector2Int>();
        public Vector2Int solidBlockSize = new Vector2Int(2, 2);

        const string GeneratedName = "HullGenerated";

        static readonly Vector2Int[] Dirs = { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };

        [ContextMenu("Generate Hull")]
        public void Generate()
        {
            Clear();
            var parent = new GameObject(GeneratedName).transform;
            parent.SetParent(transform, false);

            var roomOf = new Dictionary<Vector2Int, RoomDef>();
            foreach (var r in rooms)
                for (int x = 0; x < r.size.x; x++)
                    for (int z = 0; z < r.size.y; z++)
                        roomOf[r.origin + new Vector2Int(x, z)] = r;
            var solid = new HashSet<Vector2Int>();
            foreach (var b in solidBlocks)
                for (int x = 0; x < solidBlockSize.x; x++)
                    for (int z = 0; z < solidBlockSize.y; z++)
                        solid.Add(b + new Vector2Int(x, z));

            Func<Vector2Int, bool> occupied = c => roomOf.ContainsKey(c) || solid.Contains(c);

            foreach (var kv in roomOf)
            {
                var cell = kv.Key; var room = kv.Value;
                var center = new Vector3(cell.x + 0.5f, 0f, cell.y + 0.5f);
                Place(roof, center, Quaternion.identity, parent);
                Place(belly, center, Quaternion.identity, parent);

                foreach (var d in Dirs)
                {
                    if (occupied(cell + d)) continue;
                    bool window = room.windows && (room.windowsEverywhere || (cell.x + cell.y) % 2 == 0);
                    var pos = center + new Vector3(d.x * 0.5f, 0f, d.y * 0.5f);
                    Place(window ? wallWindow : wall, pos, Quaternion.LookRotation(new Vector3(d.x, 0f, d.y)), parent);
                }

                // 볼록 모서리: 가로, 세로 두 이웃이 모두 비어 있는 꼭짓점. 프리팹은 (+x, +z) 모서리 기준이다.
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        if (occupied(cell + new Vector2Int(sx, 0)) || occupied(cell + new Vector2Int(0, sz))) continue;
                        float yaw = sx > 0 ? (sz > 0 ? 0f : 90f) : (sz > 0 ? -90f : 180f);
                        var pos = center + new Vector3(sx * 0.5f, 0f, sz * 0.5f);
                        Place(cornerOut, pos, Quaternion.Euler(0f, yaw, 0f), parent);
                    }
            }

            foreach (var r in rooms)
                if (r.roofCap != null)
                    Place(r.roofCap, new Vector3(r.origin.x + r.size.x * 0.5f, 0f, r.origin.y + r.size.y * 0.5f), Quaternion.identity, parent);

            foreach (var p in extraParts)
                if (p.prefab != null) Place(p.prefab, p.position, Quaternion.Euler(p.eulerAngles), parent);
        }

        [ContextMenu("Clear Hull")]
        public void Clear()
        {
            var old = transform.Find(GeneratedName);
            if (old == null) return;
            if (Application.isPlaying) Destroy(old.gameObject);
            else DestroyImmediate(old.gameObject);
        }

        static void Place(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent)
        {
            if (prefab == null) return;
#if UNITY_EDITOR
            var go = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.SetPositionAndRotation(position, rotation);
#else
            Instantiate(prefab, position, rotation, parent);
#endif
        }
    }
}
