using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
namespace AiKodexShapE
{
    public class CanvasController : MonoBehaviour
    {
        public Button launcher;
        void Start()
        {
            launcher.onClick.AddListener(TaskOnClick);
        }
        void TaskOnClick()
        {
            EditorApplication.ExecuteMenuItem("Window/Shap-E");
        }
    }
}
