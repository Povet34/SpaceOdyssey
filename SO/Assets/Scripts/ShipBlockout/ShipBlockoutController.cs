using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SpaceOdyssey.Blockout
{
    /// <summary>
    /// ShipBlockout 씬용 임시 컨트롤러.
    /// 뚜껑(외피와 지붕)을 즉시 열고 닫고(카메라는 움직이지 않는다), WASD 이동과 Q/E 화면 회전을 맡는다.
    /// 설계 문서(Docs/ShipBuildDesign.md)의 내용을 확인하려는 프로토타입이며 본 구현이 아니다.
    /// </summary>
    [DisallowMultipleComponent]
    public class ShipBlockoutController : MonoBehaviour
    {
        [Header("Ship (Ship_Blockout 아래 그룹)")]
        public Transform floors;
        public Transform[] wallGroups;
        public Transform exterior;
        public GameObject[] hideAtStart;

        [Header("Camera (Cinemachine)")]
        public CinemachineBrain brain;
        public Transform interiorRig;
        public CinemachineCamera interiorCam;
        public CinemachineCamera exteriorCam;      // 뚜껑을 닫으면 켜지는 궤도 카메라
        public CinemachineOrbitalFollow orbital;

        [Header("Player")]
        public CharacterController player;
        public Transform playerVisual;
        public float moveSpeed = 3.5f;

        [Header("Tuning")]
        public float interiorDistance = 70f;
        public float interiorPitch = 40f;
        public float startYaw = 45f;
        public float shadowDistance = 110f;

        class Wall
        {
            public Renderer main;
            public Renderer low;
            public bool hideWhenLowered;
            public Vector3 normal;
            public bool shared;
            public GameObject root;
            public string partName;
            public Vector3 position;
        }

        /// <summary>방 사이(양쪽에 칸이 있는) 벽 조각의 정보. 변 편집기가 이 정보로 변 데이터를 만든다.</summary>
        public struct WallPiece
        {
            public GameObject go;
            public string partName;
            public Vector3 position;
            public Vector3 normal;   // 이 조각이 속한 칸에서 바깥(이웃 쪽)을 향하는 방향
        }

        public bool LidClosed => _lidClosed;

        /// <summary>Q/E로 정해진 화면 방향(도). 보간 중인 값이 아니라 목표값이라 항상 45도 + 90도의 배수다.</summary>
        public float ViewYaw => _targetYaw;

        /// <summary>뚜껑을 열고 닫거나 화면 방향이 바뀐 뒤 불린다. 구조층 단면 높이를 다시 정할 때 쓴다.</summary>
        public event System.Action ViewChanged;

        readonly List<Wall> _walls = new List<Wall>();
        Renderer[] _exteriorRenderers;
        bool _lidClosed;
        float _yaw, _targetYaw, _yawVel, _verticalSpeed;
        UniversalRenderPipelineAsset _urp;
        float _prevShadowDistance;

        void Awake()
        {
            BuildWalls();
            _exteriorRenderers = exterior.GetComponentsInChildren<Renderer>(true);
            foreach (var g in hideAtStart) if (g) g.SetActive(false);

            // 전환 애니메이션 없이 즉시 컷
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
            var camT = interiorCam.transform;
            camT.localRotation = Quaternion.Euler(interiorPitch, 0f, 0f);
            camT.localPosition = Quaternion.Euler(interiorPitch, 0f, 0f) * Vector3.back * interiorDistance;

            _yaw = _targetYaw = startYaw;
            interiorRig.SetPositionAndRotation(player.transform.position + Vector3.up, Quaternion.Euler(0f, _yaw, 0f));

            _urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (_urp != null)
            {
                _prevShadowDistance = _urp.shadowDistance;
                _urp.shadowDistance = shadowDistance;
            }

            SetLid(false);
        }

        void OnDestroy()
        {
            if (_urp != null) _urp.shadowDistance = _prevShadowDistance;
        }

        // 벽 준비: 카메라 방향에 따라 낮출 수 있도록 방향과 낮은 복제본을 만든다
        void BuildWalls()
        {
            var cells = new HashSet<Vector2Int>();
            foreach (Transform t in floors)
                cells.Add(new Vector2Int(Mathf.FloorToInt(t.position.x), Mathf.FloorToInt(t.position.z)));

            foreach (var group in wallGroups)
            {
                foreach (Transform t in group)
                {
                    var r = t.GetComponent<Renderer>();
                    if (r == null) continue;

                    Vector3 s = t.localScale, p = t.position;
                    bool thinInX = s.x < s.z;
                    float c = thinInX ? p.x : p.z;
                    float sign = (c - Mathf.Floor(c)) > 0.5f ? 1f : -1f;
                    Vector3 n = thinInX ? new Vector3(sign, 0f, 0f) : new Vector3(0f, 0f, sign);
                    Vector3 cellPos = p - n * 0.45f;
                    var cell = new Vector2Int(Mathf.FloorToInt(cellPos.x), Mathf.FloorToInt(cellPos.z));
                    var neighbour = cell + new Vector2Int(Mathf.RoundToInt(n.x), Mathf.RoundToInt(n.z));

                    var w = new Wall
                    {
                        main = r, normal = n, shared = cells.Contains(neighbour),
                        root = t.gameObject, partName = t.name, position = p
                    };
                    if (t.name == "Wall" || t.name == "WinSill") w.low = MakeLowTwin(t, r);
                    else w.hideWhenLowered = true; // 문 상인방, 창 윗부분
                    _walls.Add(w);
                }
            }
        }

        // 뚜껑을 열었을 때 보일 낮은 벽(높이 0.3m). 원래 조각은 충돌체로 그대로 남는다.
        Renderer MakeLowTwin(Transform t, Renderer r)
        {
            const float lowH = 0.3f;
            var twin = GameObject.CreatePrimitive(PrimitiveType.Cube);
            twin.name = "LowTwin";
            Destroy(twin.GetComponent<Collider>());
            twin.transform.SetParent(t, false);
            twin.transform.localScale = new Vector3(1f, lowH / t.localScale.y, 1f);
            twin.transform.localPosition = new Vector3(0f, (lowH * 0.5f - t.position.y) / t.localScale.y, 0f);
            var tr = twin.GetComponent<Renderer>();
            tr.sharedMaterial = r.sharedMaterial;
            tr.enabled = false;
            return tr;
        }

        /// <summary>방 사이 벽 조각을 목록에서 빼서 돌려준다(오브젝트는 지우지 않는다). 변 편집기가 처음에 한 번 쓴다.</summary>
        public List<WallPiece> ExtractSharedWalls()
        {
            var list = new List<WallPiece>();
            for (int i = _walls.Count - 1; i >= 0; i--)
            {
                var w = _walls[i];
                if (!w.shared) continue;
                list.Add(new WallPiece { go = w.root, partName = w.partName, position = w.position, normal = w.normal });
                _walls.RemoveAt(i);
            }
            return list;
        }

        /// <summary>새로 놓은 벽 조각을 등록한다. 뚜껑을 열면 낮아지고(벽) 또는 숨는다(문 상인방).</summary>
        public void RegisterWall(GameObject go, bool hideWhenLowered)
        {
            var r = go.GetComponent<Renderer>();
            var w = new Wall { main = r, shared = true, root = go, partName = go.name, position = go.transform.position, hideWhenLowered = hideWhenLowered };
            if (!hideWhenLowered) w.low = MakeLowTwin(go.transform, r);
            _walls.Add(w);
            ApplyWalls();
        }

        public void UnregisterWall(GameObject go)
        {
            _walls.RemoveAll(w => w.root == go);
        }

        public void RefreshWalls() => ApplyWalls();

        // 뚜껑 닫기/열기. 애니메이션 없이 즉시 바뀐다.
        // 열면 외피와 지붕은 보이지 않지만 그림자는 계속 드리우고, 방 사이 벽과 카메라 쪽 벽은 낮아진다.
        void SetLid(bool closed)
        {
            _lidClosed = closed;
            exteriorCam.enabled = closed;   // 닫으면 궤도 카메라, 열면 내부 카메라
            var mode = closed ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;
            foreach (var r in _exteriorRenderers) if (r) r.shadowCastingMode = mode;
            ApplyWalls();
            ViewChanged?.Invoke();
        }

        void ApplyWalls()
        {
            Vector3 camForward = Quaternion.Euler(0f, _targetYaw, 0f) * Vector3.forward;
            foreach (var w in _walls)
            {
                bool lowered = !_lidClosed && (w.shared || Vector3.Dot(w.normal, camForward) < -0.01f);
                w.main.enabled = !lowered;
                if (w.low != null) w.low.enabled = lowered;
            }
        }

        public void ToggleLid() => SetLid(!_lidClosed);

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb != null && kb.tabKey.wasPressedThisFrame) ToggleLid();

            if (_lidClosed) { UpdateOrbit(mouse); return; }

            if (kb != null)
            {
                if (kb.qKey.wasPressedThisFrame) { _targetYaw -= 90f; ApplyWalls(); ViewChanged?.Invoke(); }
                if (kb.eKey.wasPressedThisFrame) { _targetYaw += 90f; ApplyWalls(); ViewChanged?.Invoke(); }
            }
            _yaw = Mathf.SmoothDampAngle(_yaw, _targetYaw, ref _yawVel, 0.25f);

            Vector3 input = Vector3.zero;
            if (kb != null)
            {
                if (kb.wKey.isPressed) input.z += 1f;
                if (kb.sKey.isPressed) input.z -= 1f;
                if (kb.dKey.isPressed) input.x += 1f;
                if (kb.aKey.isPressed) input.x -= 1f;
            }
            input = Vector3.ClampMagnitude(input, 1f);
            Vector3 move = Quaternion.Euler(0f, _yaw, 0f) * input * moveSpeed;

            _verticalSpeed = player.isGrounded ? -1f : _verticalSpeed - 9.8f * Time.deltaTime;
            move.y = _verticalSpeed;
            player.Move(move * Time.deltaTime);

            if (input.sqrMagnitude > 0.01f && playerVisual != null)
            {
                var look = Quaternion.LookRotation(new Vector3(move.x, 0f, move.z));
                playerVisual.rotation = Quaternion.Slerp(playerVisual.rotation, look, 1f - Mathf.Exp(-14f * Time.deltaTime));
            }

            Vector3 want = player.transform.position + Vector3.up;
            interiorRig.position = Vector3.Lerp(interiorRig.position, want, 1f - Mathf.Exp(-8f * Time.deltaTime));
            interiorRig.rotation = Quaternion.Euler(0f, _yaw, 0f);

            if (mouse != null)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    float fov = interiorCam.Lens.FieldOfView - Mathf.Sign(scroll) * 1.2f;
                    interiorCam.Lens.FieldOfView = Mathf.Clamp(fov, 5f, 20f);
                }
            }
        }

        // 뚜껑을 닫은 동안: 우클릭 드래그로 배 둘레를 돌고 휠로 거리를 바꾼다
        void UpdateOrbit(Mouse mouse)
        {
            if (mouse == null) return;
            if (mouse.rightButton.isPressed)
            {
                Vector2 d = mouse.delta.ReadValue();
                orbital.HorizontalAxis.Value = Mathf.Repeat(orbital.HorizontalAxis.Value + d.x * 0.2f + 180f, 360f) - 180f;
                orbital.VerticalAxis.Value = Mathf.Clamp(orbital.VerticalAxis.Value - d.y * 0.2f, -15f, 80f);
            }
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
                orbital.Radius = Mathf.Clamp(orbital.Radius - Mathf.Sign(scroll) * 3f, 15f, 90f);
        }

        void OnGUI()
        {
            float s = Mathf.Max(1f, Screen.height / 720f);
            var button = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(18 * s) };
            var label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(14 * s) };
            label.normal.textColor = Color.white;

            string text = _lidClosed ? "뚜껑 열기 (Tab)" : "뚜껑 닫기 (Tab)";
            if (GUI.Button(new Rect(12 * s, 12 * s, 220 * s, 40 * s), text, button)) ToggleLid();
            string help = _lidClosed
                ? "마우스 우클릭 드래그: 배 둘레 회전   휠: 거리"
                : "WASD: 이동   Q / E: 화면 90도 회전   휠: 줌";
            GUI.Label(new Rect(12 * s, 58 * s, 900 * s, 30 * s), help, label);
        }
    }
}
