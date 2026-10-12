#!/usr/bin/env python3
"""
Motion / behaviour stats of recorded takes (the .json next to each .mp4), side by side with the reference ad
numbers from Documentation~/RESEARCH.md, to check a persona looks like what it claims to be.

  python Assets/AdSim/Tools~/take_stats.py Logs/adsim/*.json
"""
import json
import sys
from collections import Counter

import numpy as np

REFERENCE = {  # reference ad, after the entrance (see RESEARCH.md section 4)
    "speed p50 (W/s)": 0.29, "speed p90 (W/s)": 2.35, "still %": 36, "pause p50 (s)": 0.17,
    "pause max (s)": 0.57, "frees / min": 11.5,
}


def stats(path):
    d = json.load(open(path))
    p = np.array(d["path"], dtype=float).reshape(-1, 4)
    t, x, y, down = p[:, 0], p[:, 1], p[:, 2], p[:, 3]
    v = np.hypot(np.diff(x), np.diff(y)) / np.maximum(np.diff(t), 1e-6)
    still = v < 0.15
    pauses, run = [], 0.0
    for s, dt in zip(still, np.diff(t)):
        if s:
            run += dt
        elif run:
            pauses.append(run)
            run = 0.0
    pauses = [q for q in pauses if q > 0.1] or [0.0]
    # high-frequency shake while (nearly) still: distance from a 7-frame moving average, in px of a 720-wide frame
    k = 7
    xs, ys = np.convolve(x, np.ones(k) / k, "same"), np.convolve(y, np.ones(k) / k, "same")
    resid = (np.hypot(x - xs, y - ys) * 720)[k:-k]
    slow = np.convolve(np.r_[v, v[-1]], np.ones(k) / k, "same")[k:-k] < 0.15
    shake = float(np.median(resid[slow])) if slow.any() else 0.0
    ev = Counter(e["kind"] for e in d["events"])
    frees = sum(int(e["detail"].split()[0]) for e in d["events"] if e["kind"] == "freed")
    minutes = max(d["seconds"], 1e-6) / 60.0
    return {
        "take": path.replace("\\", "/").split("/")[-1], "persona": d["persona"], "seconds": round(d["seconds"], 1),
        "completed": d["completed"],
        "speed p50 (W/s)": round(float(np.percentile(v, 50)), 2), "speed p90 (W/s)": round(float(np.percentile(v, 90)), 2),
        "shake px (still)": round(shake, 2), "still %": round(100 * float(still.mean())), "pause p50 (s)": round(float(np.median(pauses)), 2),
        "pause max (s)": round(float(max(pauses)), 2), "frees / min": round(frees / minutes, 1),
        "pressed %": round(100 * float(down.mean())),  # drags + probes + taps (not visible in the ad)
        "moves": ev["move"], "probes": ev["probe"], "wrong-way": ev["wrong-way"], "misses": ev["miss"],
        "hesitations": ev["hesitate"], "wanders": ev["wander"], "undos": ev["undo"], "nudges": ev["nudge"],
        "blocked": ev["blocked"], "replans": ev["plan"],
    }


def main():
    rows = [stats(p) for p in sys.argv[1:]]
    keys = list(rows[0].keys()) if rows else []
    print(f"{'':18}" + "".join(f"{r['take'][:22]:>24}" for r in rows) + f"{'reference ad':>16}")
    for k in keys[1:]:
        print(f"{k:18}" + "".join(f"{str(r[k]):>24}" for r in rows) + f"{str(REFERENCE.get(k, '')):>16}")


if __name__ == "__main__":
    main()
