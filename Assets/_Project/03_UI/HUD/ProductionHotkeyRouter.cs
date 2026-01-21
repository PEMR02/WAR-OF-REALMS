using UnityEngine;
using UnityEngine.InputSystem;

namespace Project.UI
{
    /// <summary>
    /// Router de hotkeys para entrenar unidades (teclas 1-9)
    /// </summary>
    public class ProductionHotkeyRouter : MonoBehaviour
    {
        [Header("Refs")]
        public ProductionHUD productionHUD;

        void Awake()
        {
            if (productionHUD == null)
                productionHUD = FindFirstObjectByType<ProductionHUD>();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || productionHUD == null) return;

            // Leer dígito presionado (1-9)
            int digit = ReadDigitPressed(kb);
            if (digit == -1) return;

            // Entrenar unidad
            productionHUD.TrainUnit(digit);
        }

        int ReadDigitPressed(Keyboard kb)
        {
            if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) return 1;
            if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) return 2;
            if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) return 3;
            if (kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame) return 4;
            if (kb.digit5Key.wasPressedThisFrame || kb.numpad5Key.wasPressedThisFrame) return 5;
            if (kb.digit6Key.wasPressedThisFrame || kb.numpad6Key.wasPressedThisFrame) return 6;
            if (kb.digit7Key.wasPressedThisFrame || kb.numpad7Key.wasPressedThisFrame) return 7;
            if (kb.digit8Key.wasPressedThisFrame || kb.numpad8Key.wasPressedThisFrame) return 8;
            if (kb.digit9Key.wasPressedThisFrame || kb.numpad9Key.wasPressedThisFrame) return 9;
            return -1;
        }
    }
}
