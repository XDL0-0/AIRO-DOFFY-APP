namespace Doffy.UI
{
    /// <summary>Pose debounce only. Robot authority remains in AppManager.</summary>
    public sealed class WristVisibilityGate
    {
        public bool Visible { get; private set; }
        private float pendingSeconds;

        public bool Step(bool blocked, bool showPose, bool hidePose, float deltaTime,
            float showDelay, float hideDelay)
        {
            if (blocked) { Reset(); return false; }
            bool changing = Visible ? hidePose : showPose;
            if (!changing) { pendingSeconds = 0; return Visible; }
            pendingSeconds += System.Math.Max(0, deltaTime);
            if (pendingSeconds >= (Visible ? hideDelay : showDelay))
            {
                Visible = !Visible;
                pendingSeconds = 0;
            }
            return Visible;
        }

        public void Reset() { Visible = false; pendingSeconds = 0; }
    }
}
