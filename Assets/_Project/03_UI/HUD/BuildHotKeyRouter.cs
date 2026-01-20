using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

public class BuildHotkeyRouter : MonoBehaviour
{
    public BuildModeController build;

    void Awake()
    {
        if (build == null) build = FindFirstObjectByType<BuildModeController>();
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || build == null) return;

        // NO verificamos IsPointerOverGameObject() aquí
        // Las teclas deben funcionar SIEMPRE, incluso con cursor sobre HUD

        // Esc = cancelar / volver atrás
        if (kb.escapeKey.wasPressedThisFrame)
        {
            Debug.Log($"BuildHotkeyRouter: ESC presionado - Estado actual: {build.state}");
            build.Cancel();
            Debug.Log($"BuildHotkeyRouter: Después de Cancel() - Nuevo estado: {build.state}");
            return;
        }

        int digit = ReadDigitPressed(kb);
        if (digit == -1) return;

        Debug.Log($"BuildHotkeyRouter: Tecla {digit} presionada - Estado actual: {build.state}");

        // Si está Idle, al presionar un número entramos al root automáticamente
        if (build.state == BuildState.Idle)
        {
            Debug.Log($"BuildHotkeyRouter: Estado Idle, entrando a BuildRoot");
            build.EnterBuildRoot();
        }

        // Root => el número elige categoría (1..4)
        if (build.state == BuildState.BuildRoot)
        {
            if (digit == 1) { Debug.Log("BuildHotkeyRouter: Entrando a categoría Econ"); build.EnterCategory(BuildCategory.Econ); }
            else if (digit == 2) { Debug.Log("BuildHotkeyRouter: Entrando a categoría Military"); build.EnterCategory(BuildCategory.Military); }
            else if (digit == 3) { Debug.Log("BuildHotkeyRouter: Entrando a categoría Defenses"); build.EnterCategory(BuildCategory.Defenses); }
            else if (digit == 4) { Debug.Log("BuildHotkeyRouter: Entrando a categoría Special"); build.EnterCategory(BuildCategory.Special); }
            return;
        }

        // Category => el número elige edificio (1..9)
        if (build.state == BuildState.Category)
        {
            Debug.Log($"BuildHotkeyRouter: Seleccionando slot {digit} en categoría {build.currentCategory}");
            build.PickSlot(digit); // 1..9
            return;
        }

        // Placing => cambiar edificio directamente (AoE2 style)
        if (build.state == BuildState.Placing)
        {
            if (build.currentCategory != null)
            {
                Debug.Log($"BuildHotkeyRouter: Cambiando edificio mientras Placing - slot {digit}");
                build.PickSlot(digit); // Esto cancelará el placing actual y pondrá el nuevo
            }
            else
            {
                Debug.LogWarning("BuildHotkeyRouter: En Placing pero currentCategory es null");
            }
        }
    }

    static int ReadDigitPressed(Keyboard kb)
    {
        if (kb.digit1Key.wasPressedThisFrame) return 1;
        if (kb.digit2Key.wasPressedThisFrame) return 2;
        if (kb.digit3Key.wasPressedThisFrame) return 3;
        if (kb.digit4Key.wasPressedThisFrame) return 4;
        if (kb.digit5Key.wasPressedThisFrame) return 5;
        if (kb.digit6Key.wasPressedThisFrame) return 6;
        if (kb.digit7Key.wasPressedThisFrame) return 7;
        if (kb.digit8Key.wasPressedThisFrame) return 8;
        if (kb.digit9Key.wasPressedThisFrame) return 9;
        return -1;
    }
}
