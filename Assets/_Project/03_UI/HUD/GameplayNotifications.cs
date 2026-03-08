using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Project.UI
{
    /// <summary>
    /// Sistema de notificaciones tipo toast: mensajes breves en pantalla (ej. "Unidad creada", "Recursos insuficientes").
    /// Usar <see cref="Show(string, float)"/> desde cualquier script.
    /// </summary>
    public class GameplayNotifications : MonoBehaviour
    {
        public static GameplayNotifications Instance { get; private set; }

        [Header("UI")]
        public RectTransform container;
        public TextMeshProUGUI messageText;
        [Tooltip("Segundos que se muestra cada mensaje antes de empezar a desvanecer.")]
        public float displayDuration = 2.5f;
        [Tooltip("Segundos de fade out.")]
        public float fadeOutDuration = 0.5f;

        [Header("Cola")]
        [Tooltip("Si true, encola mensajes y los muestra uno tras otro. Si false, cada nuevo mensaje reemplaza al actual.")]
        public bool queueMessages = true;
        [Tooltip("Máximo de mensajes en cola (0 = ilimitado).")]
        public int maxQueueSize = 5;

        private readonly Queue<string> _queue = new Queue<string>();
        private float _timer;
        private bool _fadingOut;
        private CanvasGroup _canvasGroup;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // container y messageText son opcionales; no romper si son null al inicio
            if (messageText != null && messageText.gameObject.activeInHierarchy)
                messageText.text = "";

            _canvasGroup = container != null ? container.GetComponent<CanvasGroup>() : null;
            if (_canvasGroup == null && container != null)
                _canvasGroup = container.gameObject.AddComponent<CanvasGroup>();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (messageText == null) return;
            if (!messageText.gameObject.activeInHierarchy) return;

            _timer -= Time.deltaTime;

            if (_fadingOut)
            {
                if (_canvasGroup != null)
                    _canvasGroup.alpha = Mathf.Clamp01(_timer / fadeOutDuration);
                if (_timer <= 0f)
                {
                    _fadingOut = false;
                    ShowNext();
                }
                return;
            }

            if (_timer <= 0f)
            {
                _fadingOut = true;
                _timer = fadeOutDuration;
            }
        }

        void ShowNext()
        {
            if (_canvasGroup != null) _canvasGroup.alpha = 1f;

            if (messageText == null)
            {
                if (container != null) container.gameObject.SetActive(false);
                return;
            }

            if (_queue.Count > 0)
            {
                string msg = _queue.Dequeue();
                messageText.text = msg;
                messageText.gameObject.SetActive(true);
                if (container != null) container.gameObject.SetActive(true);
                _timer = displayDuration;
            }
            else
            {
                messageText.text = "";
                messageText.gameObject.SetActive(false);
                if (container != null) container.gameObject.SetActive(false);
            }
        }

        /// <summary>Muestra un mensaje (toast). Duración opcional en segundos.</summary>
        public void ShowMessage(string message, float duration = -1f)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (messageText == null) return; // container/messageText opcionales; no romper si son null

            float d = duration > 0f ? duration : displayDuration;

            if (queueMessages)
            {
                if (maxQueueSize > 0 && _queue.Count >= maxQueueSize)
                    return;
                _queue.Enqueue(message);
                if (string.IsNullOrEmpty(messageText.text) || !messageText.gameObject.activeInHierarchy)
                {
                    messageText.text = message;
                    messageText.gameObject.SetActive(true);
                    if (container != null) container.gameObject.SetActive(true);
                    _timer = d;
                    _fadingOut = false;
                }
                return;
            }

            messageText.text = message;
            messageText.gameObject.SetActive(true);
            if (container != null) container.gameObject.SetActive(true);
            _timer = d;
            _fadingOut = false;
            if (_canvasGroup != null) _canvasGroup.alpha = 1f;
        }

        /// <summary>API estática: muestra notificación (usa Instance; si no existe no hace nada).</summary>
        public static void Show(string message, float duration = -1f)
        {
            if (Instance == null) return;
            Instance.ShowMessage(message, duration);
        }
    }
}
