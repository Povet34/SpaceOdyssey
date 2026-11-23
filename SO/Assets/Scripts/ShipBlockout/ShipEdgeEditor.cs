using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpaceOdyssey.Blockout
{
    /// <summary>
    /// 칸막이 배치 프로토타입(설계 문서 0.4절). 방 사이의 벽과 문을 칸의 변에 즉시 배치하고 지운다.
    /// 시작할 때 씬에 있는 방 사이 벽 조각을 변 데이터로 읽어 들인 뒤 새 조각으로 교체한다.
    /// 편집 내용은 Play를 끝내면 사라진다(저장은 아직 없다).
    /// </summary>
    [DefaultExecutionOrder(10)]
    public class ShipEdgeEditor : MonoBehaviour
    {
        public ShipBlockoutController controller;

        enum Tool { Wall, Door, Erase }

        class Placed
        {
            public GameObject root;
            public GameObject registered;   // 컨트롤러에 등록한 조각(벽 본체 또는 문 상인방)
        }

        ShipEdgeGrid _grid;
        readonly Dictionary<EdgeKey, Placed> _placed = new Dictionary<EdgeKey, Placed>();
        Transform _wallsParent, _overlayParent;
        Material _wallMat, _frameMat, _unlit;
        GameObject _cursor;
        Renderer _cursorRenderer;
        readonly Dictionary<Vector2Int, Renderer> _overlay = new Dictionary<Vector2Int, Renderer>();
        MaterialPropertyBlock _mpb;

        bool _buildMode;
        bool _showOverlay;
        Tool _tool = Tool.Wall;
        EdgeKey _lastApplied;
        bool _hasLast;
        int _unreachable;

        void Start()
        {
            var cells = new HashSet<Vector2Int>();
            foreach (Transform t in controller.floors)
                cells.Add(new Vector2Int(Mathf.FloorToInt(t.position.x), Mathf.FloorToInt(t.position.z)));
            _grid = new ShipEdgeGrid(cells);

            _wallsParent = new GameObject("EdgeWalls").transform;
            _wallsParent.SetParent(controller.floors.parent, false);
            _overlayParent = new GameObject("RoomOverlay").transform;
            _overlayParent.SetParent(controller.floors.parent, false);
            _overlayParent.gameObject.SetActive(false);
            _mpb = new MaterialPropertyBlock();

            // 씬에 있던 방 사이 벽 조각을 변 데이터로 바꾼다
            foreach (var p in controller.ExtractSharedWalls())
            {
                var rend = p.go.GetComponent<Renderer>();
                bool isDoor = p.partName == "DoorLintel";
                if (isDoor && _frameMat == null) _frameMat = rend.sharedMaterial;
                else if (!isDoor && _wallMat == null) _wallMat = rend.sharedMaterial;

                Vector3 cellPos = p.position - p.normal * 0.45f;
                var cell = new Vector2Int(Mathf.FloorToInt(cellPos.x), Mathf.FloorToInt(cellPos.z));
                var nb = cell + new Vector2Int(Mathf.RoundToInt(p.normal.x), Mathf.RoundToInt(p.normal.z));
                var key = EdgeKey.Between(cell, nb);
                if (isDoor) _grid.Set(key, EdgeKind.Door);
                else if (_grid.Get(key) != EdgeKind.Door) _grid.Set(key, EdgeKind.Wall);
                Destroy(p.go);
            }

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (_wallMat == null) { _wallMat = new Material(lit); _wallMat.SetColor("_BaseColor", new Color(0.85f, 0.84f, 0.8f)); }
            if (_frameMat == null) { _frameMat = new Material(lit); _frameMat.SetColor("_BaseColor", new Color(0.95f, 0.65f, 0.15f)); }
            _unlit = new Material(Shader.Find("Universal Render Pipeline/Unlit"));

            foreach (var kv in new List<KeyValuePair<EdgeKey, EdgeKind>>(_grid.Edges)) Rebuild(kv.Key);
            _grid.ComputeRooms();
            UpdateStats();

            _cursor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _cursor.name = "EdgeCursor";
            Destroy(_cursor.GetComponent<Collider>());
            _cursorRenderer = _cursor.GetComponent<Renderer>();
            _cursorRenderer.sharedMaterial = new Material(_unlit);
            _cursorRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _cursor.SetActive(false);
        }

        // ---- 변 하나의 조각을 상태에 맞게 다시 만든다 ----
        void Rebuild(EdgeKey key)
        {
            if (_placed.TryGetValue(key, out var old))
            {
                if (old.registered != null) controller.UnregisterWall(old.registered);
                Destroy(old.root);
                _placed.Remove(key);
            }

            var kind = _grid.Get(key);
            if (kind == EdgeKind.None) return;

            bool alongZ = key.axis == 0;   // axis 0 변은 z 방향으로 뻗은 벽이다
            Vector3 center = key.Center;
            var placed = new Placed();

            if (kind == EdgeKind.Wall)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "EdgeWall";
                go.transform.SetParent(_wallsParent, false);
                go.transform.position = center + Vector3.up * 1.5f;
                go.transform.localScale = alongZ ? new Vector3(0.1f, 3f, 1f) : new Vector3(1f, 3f, 0.1f);
                go.GetComponent<Renderer>().sharedMaterial = _wallMat;
                placed.root = placed.registered = go;
                controller.RegisterWall(go, false);
            }
            else
            {
                var root = new GameObject("EdgeDoor");
                root.transform.SetParent(_wallsParent, false);
                root.transform.position = center;

                var lintel = GameObject.CreatePrimitive(PrimitiveType.Cube);
                lintel.name = "DoorLintel";
                lintel.transform.SetParent(root.transform, false);
                lintel.transform.localPosition = new Vector3(0f, 2.5f, 0f);
                lintel.transform.localScale = alongZ ? new Vector3(0.1f, 1f, 1f) : new Vector3(1f, 1f, 0.1f);
                lintel.GetComponent<Renderer>().sharedMaterial = _frameMat;

                var sill = GameObject.CreatePrimitive(PrimitiveType.Cube);
                sill.name = "DoorSill";
                Destroy(sill.GetComponent<Collider>());
                sill.transform.SetParent(root.transform, false);
                sill.transform.localPosition = new Vector3(0f, 0.01f, 0f);
                sill.transform.localScale = alongZ ? new Vector3(0.5f, 0.02f, 1f) : new Vector3(1f, 0.02f, 0.5f);
                sill.GetComponent<Renderer>().sharedMaterial = _frameMat;

                placed.root = root;
                placed.registered = lintel;
                controller.RegisterWall(lintel, true);
            }
            _placed[key] = placed;
        }

        void Apply(EdgeKey key)
        {
            var kind = _tool == Tool.Wall ? EdgeKind.Wall : _tool == Tool.Door ? EdgeKind.Door : EdgeKind.None;
            if (!_grid.Set(key, kind)) return;
            Rebuild(key);
            _grid.ComputeRooms();
            UpdateStats();
            if (_showOverlay) PaintOverlay();
            controller.RefreshWalls();
        }

        // ---- 마우스가 가리키는 변 ----
        bool TryGetHover(Mouse mouse, out EdgeKey key)
        {
            key = default;
            var cam = Camera.main;
            if (cam == null || mouse == null) return false;
            var ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            if (!new Plane(Vector3.up, 0f).Raycast(ray, out float t)) return false;
            Vector3 p = ray.GetPoint(t);
            float rx = Mathf.Round(p.x), rz = Mathf.Round(p.z);
            float dx = Mathf.Abs(p.x - rx), dz = Mathf.Abs(p.z - rz);
            if (Mathf.Min(dx, dz) > 0.35f) return false;
            key = dx < dz
                ? new EdgeKey((int)rx - 1, Mathf.FloorToInt(p.z), 0)
                : new EdgeKey(Mathf.FloorToInt(p.x), (int)rz - 1, 1);
            return true;
        }

        bool OverHud(Mouse mouse)
        {
            float s = Mathf.Max(1f, Screen.height / 720f);
            Vector2 m = mouse.position.ReadValue();
            return m.x < 560f * s && (Screen.height - m.y) < 240f * s;
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (controller.LidClosed) { _cursor.SetActive(false); return; }

            if (kb != null)
            {
                if (kb.bKey.wasPressedThisFrame) _buildMode = !_buildMode;
                if (kb.vKey.wasPressedThisFrame) { _showOverlay = !_showOverlay; _overlayParent.gameObject.SetActive(_showOverlay); if (_showOverlay) PaintOverlay(); }
                if (_buildMode)
                {
                    if (kb.digit1Key.wasPressedThisFrame) _tool = Tool.Wall;
                    if (kb.digit2Key.wasPressedThisFrame) _tool = Tool.Door;
                    if (kb.digit3Key.wasPressedThisFrame) _tool = Tool.Erase;
                }
            }

            if (!_buildMode || mouse == null || OverHud(mouse) || !TryGetHover(mouse, out var key))
            {
                _cursor.SetActive(false);
                if (mouse != null && !mouse.leftButton.isPressed) _hasLast = false;
                return;
            }

            bool valid = _grid.IsInterior(key);
            _cursor.SetActive(true);
            bool alongZ = key.axis == 0;
            _cursor.transform.position = key.Center + Vector3.up * 0.05f;
            _cursor.transform.localScale = alongZ ? new Vector3(0.16f, 0.06f, 1f) : new Vector3(1f, 0.06f, 0.16f);
            Color c = !valid ? new Color(0.9f, 0.2f, 0.2f)
                : _tool == Tool.Wall ? new Color(0.3f, 0.9f, 0.9f)
                : _tool == Tool.Door ? new Color(1f, 0.6f, 0.1f)
                : new Color(1f, 0.95f, 0.3f);
            _cursorRenderer.sharedMaterial.SetColor("_BaseColor", c);

            if (!valid) return;
            if (mouse.leftButton.wasPressedThisFrame || (mouse.leftButton.isPressed && (!_hasLast || !_lastApplied.Equals(key))))
            {
                Apply(key);
                _lastApplied = key; _hasLast = true;
            }
            if (!mouse.leftButton.isPressed) _hasLast = false;
        }

        // ---- 방 색 보기 ----
        void PaintOverlay()
        {
            foreach (var cell in _grid.Cells)
            {
                if (!_overlay.TryGetValue(cell, out var r))
                {
                    var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    q.name = "RoomTint";
                    Destroy(q.GetComponent<Collider>());
                    q.transform.SetParent(_overlayParent, false);
                    q.transform.position = new Vector3(cell.x + 0.5f, 0.03f, cell.y + 0.5f);
                    q.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                    r = q.GetComponent<Renderer>();
                    r.sharedMaterial = _unlit;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    _overlay[cell] = r;
                }
                r.sharedMaterial = RoomMaterial(_grid.RoomOf[cell]);
            }
        }

        readonly Dictionary<int, Material> _roomMats = new Dictionary<int, Material>();

        Material RoomMaterial(int id)
        {
            if (!_roomMats.TryGetValue(id, out var m))
            {
                m = new Material(_unlit);
                m.SetColor("_BaseColor", Color.HSVToRGB((id * 0.61803f) % 1f, 0.45f, 0.85f));
                _roomMats[id] = m;
            }
            return m;
        }

        Vector2Int PlayerCell()
        {
            var p = controller.player.transform.position;
            return new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.z));
        }

        void UpdateStats() => _unreachable = _grid.UnreachableRoomCount(PlayerCell());

        void OnGUI()
        {
            if (_grid == null || controller.LidClosed) return;
            float s = Mathf.Max(1f, Screen.height / 720f);
            var label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(14 * s) };
            label.normal.textColor = Color.white;

            float y = 92f * s;
            string toolName = _tool == Tool.Wall ? "벽" : _tool == Tool.Door ? "문" : "철거";
            GUI.Label(new Rect(12 * s, y, 900 * s, 24 * s),
                _buildMode ? $"[B] 건설 모드 켬 - [1] 벽  [2] 문  [3] 철거 (현재: {toolName}), 클릭하거나 끌어서 배치" : "[B] 건설 모드 (벽과 문 배치)", label);
            y += 22f * s;

            var pc = PlayerCell();
            string here = _grid.RoomOf.TryGetValue(pc, out int id) ? $"{_grid.RoomSizes[id]}칸" : "-";
            int largest = 0; foreach (var sz in _grid.RoomSizes) largest = Mathf.Max(largest, sz);
            GUI.Label(new Rect(12 * s, y, 900 * s, 24 * s), $"방 {_grid.RoomCount}개 | 현재 방 {here} | 가장 큰 방 {largest}칸   [V] 방 색 보기", label);
            y += 22f * s;

            if (_unreachable > 0)
            {
                label.normal.textColor = new Color(1f, 0.45f, 0.4f);
                GUI.Label(new Rect(12 * s, y, 900 * s, 24 * s), $"주의: 지금 위치에서 걸어갈 수 없는 방이 {_unreachable}개 있다 (문이 없다)", label);
            }
        }
    }
}
