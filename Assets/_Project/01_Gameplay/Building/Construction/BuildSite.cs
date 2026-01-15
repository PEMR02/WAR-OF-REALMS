using System.Collections.Generic;
using UnityEngine;
using System.Collections;

namespace Project.Gameplay.Buildings
{
    public class BuildSite : MonoBehaviour
    {
        [Header("Config")]
        public BuildingSO buildingSO;
        public GameObject finalPrefab;
        public float buildTime = 10f;     // segundos con 1 aldeano
        public float refundOnCancel = 0.75f;

        [Header("Runtime")]
        [Range(0f, 1f)] public float progress01;

        readonly HashSet<Project.Gameplay.Units.Builder> _builders = new();
        bool _completed;

        public bool IsCompleted => progress01 >= 1f;

        public void Register(Project.Gameplay.Units.Builder b) => _builders.Add(b);
        public void Unregister(Project.Gameplay.Units.Builder b) => _builders.Remove(b);

		void Start()
		{
			// Espera 1 frame para dar tiempo a BuildingPlacer de configurar
			StartCoroutine(ValidateAfterOneFrame());
		}

		IEnumerator ValidateAfterOneFrame()
		{
			yield return null;

			if (buildingSO == null || finalPrefab == null)
			{
				Debug.LogWarning($"BuildSite inválido destruido: {name} (no configurado)");
				Destroy(gameObject);
			}
		}

        public void AddWorkSeconds(float workSeconds)
        {
            if (_completed) return;
            if (buildTime <= 0.01f) buildTime = 0.01f;

            progress01 = Mathf.Clamp01(progress01 + (workSeconds / buildTime));
            if (progress01 < 1f) return;

            Complete();
        }

        void Complete()
        {
            _completed = true;

            if (finalPrefab != null)
                Instantiate(finalPrefab, transform.position, transform.rotation);
            else
                Debug.LogWarning($"BuildSite: finalPrefab null | name={name} | id={GetInstanceID()} | so={(buildingSO?buildingSO.id:"null")}");
				

            // ✅ copiar antes de iterar (evita modificar la colección durante foreach)
            var copy = new List<Project.Gameplay.Units.Builder>(_builders);
            _builders.Clear();

            for (int i = 0; i < copy.Count; i++)
            {
                var b = copy[i];
                if (b != null) b.ClearBuildTargetIfThis(this);
            }
            Destroy(gameObject);
        }
    }
}
