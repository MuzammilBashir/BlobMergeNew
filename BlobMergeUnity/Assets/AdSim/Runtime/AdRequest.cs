using System;
using System.Collections.Generic;
using UnityEngine;

namespace AdSim
{
    /// <summary>One recording job. Written by the agent/CLI, read by the editor launcher.</summary>
    [Serializable]
    public class AdRequest
    {
        public string scene;                  // scene to play, e.g. Assets/RotateRings/Scenes/Game.unity
        public string director;               // AdDirector subclass, assembly-qualified ("Ns.Type, Assembly")
        public string persona;                // persona JSON path
        public string hand;                   // hand profile JSON path
        public string endcard;                // optional endcard JSON path (played at the end, inside maxSeconds)
        public string output = "Logs/adsim/take.mp4";
        public int width = 720, height = 1280, fps = 30;
        public int msaa = 4;                  // anti-aliasing samples of the capture texture (1, 2, 4 or 8). URP takes
                                              // MSAA from the render target, not the pipeline asset, when rendering to a texture.
        public int seed = 1;
        public float maxSeconds = 59f;        // hard cap on the recording length
        public string args = "";              // game-specific, "key=value;key=value"
        public string ffmpeg = "ffmpeg";

        public string Arg(string key, string fallback = null)
        {
            foreach (var part in (args ?? "").Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq > 0 && part.Substring(0, eq).Trim() == key) return part.Substring(eq + 1).Trim();
            }
            return fallback;
        }
    }

    /// <summary>
    /// A hand sprite pair (idle / pressing). Tips are the contact pixel, measured from the image's TOP-left;
    /// visibleWidthPx is the width of the drawn hand inside the image (for sizing).
    /// </summary>
    [Serializable]
    public class HandProfile
    {
        public string idleTexture;
        public string clickTexture;
        public Vector2 imageSize = new Vector2(500, 500);
        public Vector2 idleTip;
        public Vector2 clickTip;
        public float visibleWidthPx = 136;

        // press feedback: shrink around the fingertip (no sprite swap, so nothing jumps)
        public float pressScale = 0.84f;   // hand scale while pressed
        public float pressIn = 0.07f;      // seconds to shrink on press
        public float releaseHold = 0.12f;  // stay pressed-small after a release (follow-through look)
        public float releaseOut = 0.18f;   // seconds to grow back
        public bool useClickSprite;        // swap to clickTexture while pressed (pose change, may jump)
    }

    /// <summary>What happened in a take: decisions and the pointer path (for review and calibration).</summary>
    [Serializable]
    public class TakeLog
    {
        [Serializable]
        public struct Event
        {
            public float t;
            public string kind;
            public string detail;
        }

        public string persona;
        public int seed;
        public string args;
        public int frames;
        public float seconds;
        public bool completed;
        public List<Event> events = new List<Event>();
        public List<float> path = new List<float>(); // flat t, x, y, down (x/y in W units, origin bottom-left)
    }
}
