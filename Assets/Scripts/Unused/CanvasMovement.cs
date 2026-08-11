using UnityEngine;
using Oculus.Interaction;

public class CanvasMoveProvider : MonoBehaviour, IMovementProvider
{
    [SerializeField] private float fixedZ = 0f; // 锁定Z平面

    public IMovement CreateMovement()
    {
        return new CanvasMove(fixedZ);
    }

    private class CanvasMove : IMovement
    {
        public Pose Pose { get; private set; } = Pose.identity;
        public bool Stopped => false;
        private readonly float fixedZ;

        public CanvasMove(float z)
        {
            fixedZ = z;
        }

        public void StopMovement() { }

        public void MoveTo(Pose target) => Pose = FilterPose(target);
        public void UpdateTarget(Pose target) => Pose = FilterPose(target);
        public void StopAndSetPose(Pose source) => Pose = FilterPose(source);
        public void Tick() { }

        private Pose FilterPose(Pose target)
        {
            // 只允许XY平移，Z固定
            Vector3 pos = target.position;
            pos = new Vector3(pos.x, pos.y, fixedZ);

            // 禁止旋转，始终正对摄像机
            Quaternion rot = Quaternion.identity;

            return new Pose(pos, rot);
        }
    }
}
