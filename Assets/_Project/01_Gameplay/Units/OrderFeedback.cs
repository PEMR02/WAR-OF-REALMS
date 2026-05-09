using UnityEngine;

namespace Project.Gameplay.Units
{
    public enum OrderFeedbackType
    {
        Move,
        Attack,
        Gather,
        Invalid
    }

    /// <summary>
    /// Marca visual temporal en el suelo al dar órdenes (mover, punto de rally).
    /// </summary>
    public static class OrderFeedback
    {
        const float DefaultDuration = 0.65f;
        const float DefaultSize = 1.9f;

        public static void Spawn(Vector3 worldPos) => Spawn(worldPos, OrderFeedbackType.Move);

        public static void Spawn(Vector3 worldPos, OrderFeedbackType type)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "OrderFeedback";
            go.transform.position = worldPos + Vector3.up * 0.02f;
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            float size = DefaultSize;
            float duration = DefaultDuration;
            Color color = new Color(0.2f, 0.85f, 0.35f, 0.7f);
            switch (type)
            {
                case OrderFeedbackType.Attack:
                    color = new Color(0.95f, 0.2f, 0.2f, 0.78f);
                    size = 2.1f;
                    duration = 0.75f;
                    break;
                case OrderFeedbackType.Gather:
                    color = new Color(0.95f, 0.85f, 0.22f, 0.78f);
                    size = 1.8f;
                    duration = 0.65f;
                    break;
                case OrderFeedbackType.Invalid:
                    color = new Color(0.95f, 0.2f, 0.2f, 0.55f);
                    size = 1.3f;
                    duration = 0.35f;
                    break;
                default:
                    break;
            }
            go.transform.localScale = Vector3.one * size;

            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);

            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
            if (shader != null)
            {
                var mat = new Material(shader);
                mat.color = color;
                go.GetComponent<Renderer>().sharedMaterial = mat;
            }

            var comp = go.AddComponent<OrderFeedbackMarker>();
            comp.duration = duration;
            comp.baseColor = color;
        }
    }

    public class OrderFeedbackMarker : MonoBehaviour
    {
        public float duration = 1.4f;
        public Color baseColor = new Color(0.2f, 0.85f, 0.35f, 0.7f);
        float _timer;
        Renderer _cachedRenderer;

        void Awake()
        {
            _cachedRenderer = GetComponent<Renderer>();
        }

        void Update()
        {
            _timer += Time.deltaTime;
            if (_timer >= duration)
                Destroy(gameObject);
            else if (_cachedRenderer != null && _cachedRenderer.material != null)
            {
                float a = baseColor.a * (1f - _timer / duration);
                _cachedRenderer.material.color = new Color(baseColor.r, baseColor.g, baseColor.b, a);
            }
        }
    }
}
