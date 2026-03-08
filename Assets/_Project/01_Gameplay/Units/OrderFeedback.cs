using UnityEngine;

namespace Project.Gameplay.Units
{
    /// <summary>
    /// Marca visual temporal en el suelo al dar órdenes (mover, punto de rally).
    /// </summary>
    public static class OrderFeedback
    {
        const float Duration = 1.4f;
        const float Size = 2.5f;

        public static void Spawn(Vector3 worldPos)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "OrderFeedback";
            go.transform.position = worldPos + Vector3.up * 0.02f;
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = Vector3.one * Size;

            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);

            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
            if (shader != null)
            {
                var mat = new Material(shader);
                mat.color = new Color(0.2f, 0.85f, 0.35f, 0.7f);
                go.GetComponent<Renderer>().sharedMaterial = mat;
            }

            var comp = go.AddComponent<OrderFeedbackMarker>();
            comp.duration = Duration;
        }
    }

    public class OrderFeedbackMarker : MonoBehaviour
    {
        public float duration = 1.4f;
        float _timer;

        void Update()
        {
            _timer += Time.deltaTime;
            if (_timer >= duration)
                Destroy(gameObject);
            else
            {
                var r = GetComponent<Renderer>();
                if (r != null && r.material != null)
                {
                    float a = 0.7f * (1f - _timer / duration);
                    r.material.color = new Color(0.2f, 0.85f, 0.35f, a);
                }
            }
        }
    }
}
