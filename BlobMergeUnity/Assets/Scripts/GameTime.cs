using UnityEngine;

namespace BlobMerge
{
    /// <summary>
    /// Unscaled clock for visuals that keep animating while gameplay time is frozen (game over). Unlike Unity's
    /// unscaled time it follows <c>Time.captureFramerate</c>, so recorded videos (AdSim) animate at the right speed.
    /// </summary>
    public static class GameTime
    {
        public static float UnscaledDelta => Time.captureDeltaTime > 0f ? Time.captureDeltaTime : Time.unscaledDeltaTime;
        public static float Unscaled => Time.captureDeltaTime > 0f ? Time.frameCount * Time.captureDeltaTime : Time.unscaledTime;
    }
}
