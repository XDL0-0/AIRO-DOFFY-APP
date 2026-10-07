using UnityEngine;

namespace Doffy.UI
{
    /// <summary>
    /// Keeps camera layout groups outside the retired canvas hierarchy so every view
    /// can own a root canvas, while retaining the legacy container's placement and scale.
    /// </summary>
    public sealed class CameraPanelContainer : MonoBehaviour
    {
        [SerializeField] private Transform floatingRoot;

        /// <summary>Returns a canvas-free layout parent, preserving existing group world poses.</summary>
        public static Transform Ensure(Transform container)
        {
            if (container == null) return null;
            Canvas canvas = container.GetComponentInParent<Canvas>(true)?.rootCanvas;
            if (canvas == null) return container;

            var owner = canvas.GetComponent<CameraPanelContainer>();
            if (owner == null) owner = canvas.gameObject.AddComponent<CameraPanelContainer>();
            Transform root = owner.EnsureRoot(canvas.transform);
            if (container == canvas.transform) return root;
            container.SetParent(root, true);
            return container;
        }

        private Transform EnsureRoot(Transform legacyRoot)
        {
            if (floatingRoot != null) return floatingRoot;
            floatingRoot = new GameObject("Floating camera panels").transform;
            floatingRoot.SetParent(legacyRoot.parent, false);
            floatingRoot.localPosition = legacyRoot.localPosition;
            floatingRoot.localRotation = legacyRoot.localRotation;
            floatingRoot.localScale = legacyRoot.localScale;
            return floatingRoot;
        }

        private void OnDestroy()
        {
            if (floatingRoot != null) Destroy(floatingRoot.gameObject);
        }
    }
}
