using UnityEngine;

namespace Project.Gameplay.Units
{
    /// <summary>
    /// Muestra un icono sobre la cabeza del aldeano cuando está ocioso (sin recolectar ni construir).
    /// Añadir este componente al prefab del Aldeano.
    /// </summary>
    public class IdleVillagerIcon : MonoBehaviour
    {
        [Header("Posición")]
        [Tooltip("Altura sobre el pivote del personaje.")]
        public float heightOffset = 2f;

        [Header("Apariencia")]
        [Tooltip("Color del icono (por defecto amarillo visible).")]
        public Color iconColor = new Color(1f, 0.85f, 0.2f, 0.95f);
        [Tooltip("Tamaño en metros.")]
        public float size = 0.5f;

        [Header("Actualización")]
        [Tooltip("Intervalo para comprobar si está ocioso (segundos).")]
        public float checkInterval = 0.2f;

        VillagerGatherer _gatherer;
        Builder _builder;
        GameObject _iconRoot;
        float _timer;

        void Awake()
        {
            _gatherer = GetComponent<VillagerGatherer>();
            _builder = GetComponent<Builder>();
            if (_gatherer == null)
            {
                enabled = false;
                return;
            }

            CreateIcon();
        }

        void CreateIcon()
        {
            _iconRoot = new GameObject("IdleIcon");
            _iconRoot.transform.SetParent(transform);
            _iconRoot.transform.localPosition = new Vector3(0f, heightOffset, 0f);
            _iconRoot.transform.localScale = Vector3.one * size;
            _iconRoot.transform.localRotation = Quaternion.identity;

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Quad";
            quad.transform.SetParent(_iconRoot.transform, false);
            quad.transform.localPosition = Vector3.zero;
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = Vector3.one;

            var col = quad.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
            if (shader != null)
            {
                var mat = new Material(shader);
                mat.color = iconColor;
                quad.GetComponent<Renderer>().sharedMaterial = mat;
            }

            _iconRoot.SetActive(false);
        }

        void Update()
        {
            if (_iconRoot == null) return;

            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                _timer = checkInterval;
                bool idle = _gatherer != null && _gatherer.IsIdle
                    && (_builder == null || !_builder.HasBuildTarget);
                _iconRoot.SetActive(idle);
            }

            // Billboard: mirar a la cámara (solo rotación Y para que no se tuerza)
            if (_iconRoot.activeSelf && Camera.main != null)
            {
                Vector3 toCam = Camera.main.transform.position - _iconRoot.transform.position;
                toCam.y = 0f;
                if (toCam.sqrMagnitude > 0.001f)
                    _iconRoot.transform.rotation = Quaternion.LookRotation(toCam.normalized, Vector3.up);
            }
        }
    }
}
