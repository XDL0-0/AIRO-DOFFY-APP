using Oculus.Interaction.Surfaces;
using UnityEngine;

namespace Doffy.UI
{
    /// <summary>Finite annular Meta ray surface with a genuinely empty wrist centre.</summary>
    public sealed class WristRingSurface : MonoBehaviour, ISurface
    {
        public float InnerRadius;
        public float OuterRadius;
        public Transform Transform => transform;

        public bool Raycast(in Ray ray, out SurfaceHit hit, float maxDistance = 0)
        {
            hit = default;
            var plane = new Plane(-transform.forward, transform.position);
            if (!plane.Raycast(ray, out float distance) || (maxDistance > 0 && distance > maxDistance)) return false;
            Vector3 point = ray.GetPoint(distance);
            Vector3 local = transform.InverseTransformPoint(point);
            float radiusSquared = local.x * local.x + local.y * local.y;
            if (radiusSquared < InnerRadius * InnerRadius || radiusSquared > OuterRadius * OuterRadius) return false;
            hit = new SurfaceHit { Point = point, Normal = -transform.forward, Distance = distance };
            return true;
        }

        public bool ClosestSurfacePoint(in Vector3 point, out SurfaceHit hit, float maxDistance = 0)
        {
            Vector3 local = transform.InverseTransformPoint(point);
            var radial = new Vector2(local.x, local.y);
            float magnitude = radial.magnitude;
            radial = (magnitude > .00001f ? radial / magnitude : Vector2.up) * Mathf.Clamp(magnitude, InnerRadius, OuterRadius);
            Vector3 closest = transform.TransformPoint(new Vector3(radial.x, radial.y, 0));
            float distance = Vector3.Distance(point, closest);
            hit = new SurfaceHit { Point = closest, Normal = -transform.forward, Distance = distance };
            return maxDistance <= 0 || distance <= maxDistance;
        }
    }
}
