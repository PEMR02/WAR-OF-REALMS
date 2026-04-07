using System;
using UnityEngine;
using Project.Gameplay.Resources;

namespace Project.Gameplay.Players
{
    public class PlayerResources : MonoBehaviour
    {
        /// <summary>
        /// HUD y economía del jugador humano en escaramuza: el primer <see cref="PlayerResources"/> que no
        /// viva en un Town Center de IA (<c>TownCenter_PlayerN</c>). Evita que <c>FindFirstObjectByType</c>
        /// devuelva el banco de la IA cuando hay varias instancias.
        /// </summary>
        public static PlayerResources FindPrimaryHumanSkirmish()
        {
            var all = FindObjectsByType<PlayerResources>(FindObjectsSortMode.None);
            PlayerResources fallback = null;
            for (int i = 0; i < all.Length; i++)
            {
                var pr = all[i];
                if (pr == null) continue;
                if (pr.gameObject.name.StartsWith("TownCenter_Player", StringComparison.Ordinal))
                    continue;
                if (pr.gameObject.name == "Player_01")
                    return pr;
                fallback ??= pr;
            }
            return fallback;
        }

        /// <summary>Se dispara cuando cualquier recurso cambia de valor.</summary>
        public event System.Action OnResourceChanged;

        [Header("Resources")]
        public int wood = 200;
        public int stone = 200;
        public int gold = 100;
        public int food = 200;

        [Header("Debug")]
        public bool debugLogs = false;

        /// <summary>
        /// Agrega recursos al jugador
        /// </summary>
        public void Add(ResourceKind kind, int amount)
        {
            if (amount <= 0) return;

            switch (kind)
            {
                case ResourceKind.Wood:
                    wood += amount;
                    break;
                case ResourceKind.Stone:
                    stone += amount;
                    break;
                case ResourceKind.Gold:
                    gold += amount;
                    break;
                case ResourceKind.Food:
                    food += amount;
                    break;
            }

            if (debugLogs) Debug.Log($"[PlayerResources] +{amount} {kind} | Total: {Get(kind)}");
            OnResourceChanged?.Invoke();
        }

        /// <summary>
        /// Resta recursos al jugador (para compras/construcción)
        /// </summary>
        public void Subtract(ResourceKind kind, int amount)
        {
            if (amount <= 0) return;

            switch (kind)
            {
                case ResourceKind.Wood:
                    wood = Mathf.Max(0, wood - amount);
                    break;
                case ResourceKind.Stone:
                    stone = Mathf.Max(0, stone - amount);
                    break;
                case ResourceKind.Gold:
                    gold = Mathf.Max(0, gold - amount);
                    break;
                case ResourceKind.Food:
                    food = Mathf.Max(0, food - amount);
                    break;
            }

            if (debugLogs) Debug.Log($"[PlayerResources] -{amount} {kind} | Restante: {Get(kind)}");
            OnResourceChanged?.Invoke();
        }

        /// <summary>
        /// Verifica si el jugador tiene suficientes recursos
        /// </summary>
        public bool Has(ResourceKind kind, int amount)
        {
            return Get(kind) >= amount;
        }

        /// <summary>
        /// Obtiene la cantidad actual de un recurso
        /// </summary>
        public int Get(ResourceKind kind)
        {
            return kind switch
            {
                ResourceKind.Wood => wood,
                ResourceKind.Stone => stone,
                ResourceKind.Gold => gold,
                ResourceKind.Food => food,
                _ => 0
            };
        }

        /// <summary>
        /// Establece directamente un valor de recurso (útil para debugging)
        /// </summary>
        public void Set(ResourceKind kind, int amount)
        {
            amount = Mathf.Max(0, amount);

            switch (kind)
            {
                case ResourceKind.Wood:
                    wood = amount;
                    break;
                case ResourceKind.Stone:
                    stone = amount;
                    break;
                case ResourceKind.Gold:
                    gold = amount;
                    break;
                case ResourceKind.Food:
                    food = amount;
                    break;
            }
        }
    }
}