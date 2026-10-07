using System;
using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using UnityEngine;

namespace Doffy.UI
{
    /// <summary>
    /// Adds a Meta Interaction SDK ray surface and optional direct hand-poke surface
    /// to the supplied world-space UI rectangle.
    /// </summary>
    public sealed class WristCanvasSurface : MonoBehaviour, IGameObjectFilter
    {
        private const float ColliderDepth = 0.01f;

        private RectTransform rect;
        private WristInteractionSource source;
        private PointableCanvas pointableCanvas;
        private Transform surfaceTransform;
        private BoxCollider boxCollider;
        private BoundsClipper pokeBounds;
        private PokeInteractable pokeInteractable;
        private bool allowPoke;
        private bool recordingOnly;
        private Func<bool> interactionAvailable;
        private bool hasExternalRaySurface;
        private Rect lastRect;
        private bool hasLastRect;

        public RayInteractable Ray { get; private set; }

        /// <summary>
        /// Adds or reuses this helper on <paramref name="rect"/> and creates its interaction
        /// geometry in the rectangle's local z=0 plane. A caller may position the rectangle itself.
        /// </summary>
        public static WristCanvasSurface Create(RectTransform rect, PointableCanvas pointable,
            WristInteractionSource source, bool allowPoke = true, bool recordingOnly = false,
            Func<bool> availability = null)
        {
            if (rect == null)
                throw new System.ArgumentNullException(nameof(rect));
            if (pointable == null)
                throw new System.ArgumentNullException(nameof(pointable));

            WristCanvasSurface helper = rect.GetComponent<WristCanvasSurface>();
            if (helper == null)
                helper = rect.gameObject.AddComponent<WristCanvasSurface>();

            helper.Initialize(rect, pointable, source == null ? WristInteractionSource.Ensure() : source,
                allowPoke, recordingOnly, availability);
            return helper;
        }

        /// <summary>
        /// Replaces the rectangular ray surface with an arbitrary SDK surface, such as an annulus.
        /// The rectangular collider stays disabled so it cannot fill holes in the supplied shape.
        /// </summary>
        public void SetRaySurface(ISurface surface)
        {
            if (surface == null)
                throw new System.ArgumentNullException(nameof(surface));
            if (Ray == null)
                throw new System.InvalidOperationException("Create the wrist canvas surface before setting its ray surface.");

            // Replacing a surface can invalidate a current target. Disable first so Meta's
            // Interactable lifecycle cancels any selection before the new shape is injected.
            Ray.enabled = false;
            Ray.InjectSurface(surface);
            hasExternalRaySurface = true;
            if (boxCollider != null)
                boxCollider.enabled = false;
            ApplyInteractionPolicy();
        }

        private void Initialize(RectTransform targetRect, PointableCanvas pointable,
            WristInteractionSource interactionSource, bool shouldAllowPoke, bool shouldBeRecordingOnly,
            Func<bool> availability)
        {
            if (Ray != null)
            {
                if (rect != targetRect)
                    throw new System.InvalidOperationException("A WristCanvasSurface can only be attached to its original rectangle.");
                bool filtersChanged = source != interactionSource || recordingOnly != shouldBeRecordingOnly ||
                    interactionAvailable != availability;
                bool canvasChanged = pointableCanvas != pointable;
                if (filtersChanged || canvasChanged)
                {
                    Ray.enabled = false;
                    if (pokeInteractable != null) pokeInteractable.enabled = false;
                    source = interactionSource;
                    recordingOnly = shouldBeRecordingOnly;
                    interactionAvailable = availability;
                    pointableCanvas = pointable;
                    Ray.InjectOptionalPointableElement(pointable);
                    if (pokeInteractable != null) pokeInteractable.InjectOptionalPointableElement(pointable);
                    InjectInteractorFilters(pointable);
                }
                allowPoke = shouldAllowPoke;
                ApplyInteractionPolicy();
                return;
            }

            rect = targetRect;
            source = interactionSource;
            pointableCanvas = pointable;
            allowPoke = shouldAllowPoke;
            recordingOnly = shouldBeRecordingOnly;
            interactionAvailable = availability;

            var surfaceObject = new GameObject("Wrist interaction surface");
            surfaceObject.SetActive(false);
            surfaceObject.transform.SetParent(rect, false);
            surfaceTransform = surfaceObject.transform;

            boxCollider = surfaceObject.AddComponent<BoxCollider>();
            ColliderSurface colliderSurface = surfaceObject.AddComponent<ColliderSurface>();
            colliderSurface.InjectCollider(boxCollider);

            Ray = surfaceObject.AddComponent<RayInteractable>();
            Ray.enabled = false;
            Ray.InjectSurface(colliderSurface);
            Ray.InjectOptionalPointableElement(pointable);
            InjectInteractorFilters(pointable);

            if (allowPoke)
            {
                PlaneSurface plane = surfaceObject.AddComponent<PlaneSurface>();
                plane.Facing = PlaneSurface.NormalFacing.Backward;
                pokeBounds = surfaceObject.AddComponent<BoundsClipper>();
                ClippedPlaneSurface patch = surfaceObject.AddComponent<ClippedPlaneSurface>();
                patch.InjectAllClippedPlaneSurface(plane, new IBoundsClipper[] { pokeBounds });

                pokeInteractable = surfaceObject.AddComponent<PokeInteractable>();
                pokeInteractable.enabled = false;
                pokeInteractable.InjectSurfacePatch(patch);
                pokeInteractable.InjectOptionalPointableElement(pointable);
                InjectInteractorFilters(pointable);
            }

            UpdateGeometry(true);
            surfaceObject.SetActive(true);
            ApplyInteractionPolicy();
        }

        private void InjectInteractorFilters(PointableCanvas pointable)
        {
            if (source == null || pointable == null)
                return;

            // Meta serializes injected filters as UnityEngine.Objects, then rebuilds
            // its runtime list in Awake. A plain C# adapter becomes null when this
            // surface is created under an inactive canvas. Keep the scope on this
            // component so both the injected and serialized references survive.
            var filters = new List<IGameObjectFilter> { this };
            if (Ray != null) Ray.InjectOptionalInteractorFilters(filters);
            if (pokeInteractable != null) pokeInteractable.InjectOptionalInteractorFilters(filters);
        }

        public bool Filter(GameObject interactor)
        {
            if (!isActiveAndEnabled || source == null) return false;
            if (interactionAvailable != null)
            {
                if (!source.isActiveAndEnabled || !interactionAvailable() || interactor == null) return false;
                return source.HandsActive
                    ? source.RightPoke != null && interactor == source.RightPoke.gameObject
                    : source.ControllerModeActive && source.RightRay != null && interactor == source.RightRay.gameObject;
            }
            return recordingOnly ? source.FilterRecordingScope(interactor) : source.Filter(interactor);
        }

        private void LateUpdate()
        {
            if (rect == null || Ray == null || source == null)
                return;

            UpdateGeometry(false);
            ApplyInteractionPolicy();
        }

        private void OnEnable()
        {
            if (rect == null || Ray == null)
                return;
            UpdateGeometry(true);
            ApplyInteractionPolicy();
        }

        private void OnDisable()
        {
            if (Ray != null) Ray.enabled = false;
            if (pokeInteractable != null) pokeInteractable.enabled = false;
            if (boxCollider != null) boxCollider.enabled = false;
        }

        private void ApplyInteractionPolicy()
        {
            if (rect == null || Ray == null || source == null || surfaceTransform == null || boxCollider == null)
                return;

            bool geometryValid = rect.rect.width > 0f && rect.rect.height > 0f;
            bool sourceAllowsInteraction = source.isActiveAndEnabled &&
                (interactionAvailable?.Invoke() ?? (recordingOnly ? source.CanInteractForRecording : source.CanInteract));
            bool canInteract = isActiveAndEnabled && gameObject.activeInHierarchy && geometryValid && sourceAllowsInteraction;
            bool useHands = source.HandsActive;
            bool useControllerRay = source.ControllerModeActive;
            bool wantRay = canInteract && useControllerRay;
            bool wantPoke = canInteract && allowPoke && useHands && pokeInteractable != null;

            // Turn off the previous modality before enabling the next, preventing a one-frame
            // overlap if the Meta active-controller mode changes between hand and controller.
            if (!wantRay && Ray.enabled) Ray.enabled = false;
            if (!wantPoke && pokeInteractable != null && pokeInteractable.enabled)
                pokeInteractable.enabled = false;

            boxCollider.enabled = wantRay && !hasExternalRaySurface;
            if (wantRay && !Ray.enabled) Ray.enabled = true;
            if (wantPoke && !pokeInteractable.enabled) pokeInteractable.enabled = true;
        }

        private void UpdateGeometry(bool force)
        {
            Rect current = rect.rect;
            if (!force && hasLastRect && current == lastRect)
                return;

            surfaceTransform.localPosition = new Vector3(current.center.x, current.center.y, 0f);
            boxCollider.center = Vector3.zero;
            boxCollider.size = new Vector3(current.width, current.height, ColliderDepth);
            if (pokeBounds != null)
                pokeBounds.Size = new Vector3(current.width, current.height, ColliderDepth);

            if (hasExternalRaySurface)
                boxCollider.enabled = false;

            lastRect = current;
            hasLastRect = true;
        }
    }
}
