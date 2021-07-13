using System;
using UnityEngine;
using Object = System.Object;

namespace AdvancedPathfinder.UI
{
    public class Highlighter : MonoBehaviour
    {
        internal MeshFilter MeshFilter;
        internal MeshRenderer MeshRenderer;
        internal bool FreeForUse => gameObject != null && !gameObject.activeSelf;
        internal Action<Highlighter> Destroyed;
        internal object AttachedObject; 

        private void OnDestroy()
        {
            Destroyed(this);
        }
    }
}