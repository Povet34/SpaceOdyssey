using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// 장소 표식. 배의 한 지점에 붙고, 가까이 가야 글과 눈금이 보이며, 상호작용 키(F)로 그 장소의 일을 한다.
    /// 무엇을 보여 주고 무엇을 하는지는 ProtoSeedState가 정한다. 여기는 거리와 그리기만 맡는다.
    /// </summary>
    public class ProtoSpot : MonoBehaviour
    {
        public enum Kind
        {
            Bed,
            Shelf,
            Pod,
            Planter,
            Airlock,
            Bridge,
        }

        [field: SerializeField]
        public Kind Role { get; set; }

        /// <summary>표식 제목(화면에 쓰는 장소 이름).</summary>
        [field: SerializeField]
        public string Title { get; set; }

        /// <summary>이 거리 안에 들어와야 표식의 글과 눈금이 보인다(미터). 판정 (가)가 이 값에 달림.</summary>
        [field: SerializeField]
        public float VisibleRange { get; set; } = 4f;

        /// <summary>이 거리 안에서 F를 누르면 이 장소의 일을 한다(미터).</summary>
        [field: SerializeField]
        public float UseRange { get; set; } = 2f;

        /// <summary>표식 글상자를 띄울 지점의 높이(미터).</summary>
        [field: SerializeField]
        public float LabelHeight { get; set; } = 2f;

        ProtoSeedState _seed;
        Camera _cam;

        void Awake()
        {
            _seed = GetComponentInParent<ProtoSeedState>();
        }

        void Start()
        {
            _cam = Camera.main;
        }

        public float FlatDistance(Vector3 p)
        {
            var d = p - transform.position;
            d.y = 0f;
            return d.magnitude;
        }

        void OnGUI()
        {
            if (_seed.Focus != this) return;

            GUI.depth = 0;
            float s = ProtoGui.S;
            float w = 380f * s;
            float pad = 10f * s;
            float titleH = 24f * s;
            float innerW = w - pad * 2f;

            string action = _seed.ActionText(Role);
            float actionH = action == null ? 0f : ProtoGui.TextHeight(action, 15, innerW) + 6f * s;
            float bodyH = _seed.MarkerBody(this, new Rect(0f, 0f, innerW, 0f), false);
            if (bodyH <= 0f && action == null) return;

            float h = pad * 2f + titleH + bodyH + actionH;
            Vector3 sp = _cam.WorldToScreenPoint(transform.position + Vector3.up * LabelHeight);
            // 카메라 뒤쪽 지점은 화면에 투영되지 않으므로 그리지 않는다(오류를 숨기는 것이 아니라 화면 밖 판정).
            if (sp.z <= 0f) return;

            float x = Mathf.Clamp(sp.x - w * 0.5f, 8f * s, Screen.width - w - 8f * s);
            float y = Mathf.Clamp(Screen.height - sp.y - h, 8f * s, Screen.height - h - 8f * s);

            ProtoGui.Fill(new Rect(x, y, w, h), ProtoGui.Dark);
            ProtoGui.Fill(new Rect(x, y, 4f * s, h), ProtoGui.Accent);
            ProtoGui.Text(new Rect(x + pad + 4f * s, y + pad, innerW, titleH), Title, 17, new Color(0.6f, 0.9f, 1f));
            float by = y + pad + titleH;
            _seed.MarkerBody(this, new Rect(x + pad + 4f * s, by, innerW - 4f * s, 0f), true);
            if (action != null)
            {
                ProtoGui.Text(new Rect(x + pad + 4f * s, by + bodyH + 4f * s, innerW - 4f * s, actionH), action, 15, ProtoGui.Accent, TextAnchor.UpperLeft, true);
            }
        }

        void OnDrawGizmos()
        {
            // 씬 뷰에서 위치와 거리를 보려는 것. 게임 화면에는 안 나온다.
            Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, UseRange);
            Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
            Gizmos.DrawWireSphere(transform.position, VisibleRange);
        }
    }
}
