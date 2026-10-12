using System.Collections;
using AdSim;
using UnityEngine;

namespace MyGame.Ads
{
    /// <summary>
    /// Template ad director: copy into your project, rename, and fill the TODOs with your game's types.
    /// Compiles as-is (it only taps and drags around the screen centre) so you can test capture + hand first.
    /// See Example-RotateRings/RotateRingsDirector.cs for a complete adapter (solver plan + persona detours).
    /// </summary>
    public class MyGameDirector : AdDirector
    {
        protected override Camera AcquireCamera()
        {
            // TODO: find your game controller; hide dev UI / debug overlays; turn off auto-advance to the next level.
            // Return the camera that renders gameplay (it will be rendered into the capture texture).
            return Camera.main;
        }

        protected override void PrepareScene()
        {
            // The camera now renders at the capture size (Request.width x Request.height), so load/re-frame the level
            // here. Game-specific options come from the request's "args" ("level=12;foo=bar").
            // TODO: MyGame.LoadLevel(Request.Arg("level"));

            // TODO: route your game's input through the virtual pointer (see Template/GameHooks.md), e.g.
            // MyInput.Override = () => new MyInput.State {
            //     Pressed = Motor.Pointer.Pressed, Held = Motor.Pointer.Down,
            //     Released = Motor.Pointer.Released, Screen = Motor.Pointer.Position };
        }

        protected override void Cleanup()
        {
            // TODO: MyInput.Override = null; restore any saved progress the take changed.
        }

        protected override IEnumerator Perform()
        {
            if (Persona.entrance) Hand.PlayEntrance(0.8f);
            var centre = new Vector2(W * 0.5f, H * 0.5f);
            yield return Motor.MoveTo(centre, 0.8f, false);

            while (Elapsed < TimeLimit)
            {
                // TODO: decide with your game's own rules (ask a solver / pick a valid move), then let the persona
                // add detours before acting: Persona.probeRate, missRate, hesitationRate, wrongDirectionRate...
                yield return Motor.Hover(Rng.Range(Persona.thinkTime));

                var a = centre + Rng.Gauss2() * (0.12f * W);
                Log("tap", a.ToString());
                yield return Motor.MoveTo(a);
                yield return Motor.Tap();

                var b = a + Rng.Gauss2() * (0.15f * W);
                Log("drag", $"{a} -> {b}");
                yield return Drag(a, b);
            }
            Completed = false; // TODO: true when the level was cleared
        }

        /// <summary>Press at <paramref name="from"/>, drag to <paramref name="to"/> with a human speed profile, release.</summary>
        IEnumerator Drag(Vector2 from, Vector2 to)
        {
            yield return Motor.MoveTo(from);
            yield return Motor.Press();
            float T = Mathf.Max(0.15f, HumanMotor.MinJerkTime(Vector2.Distance(from, to), Persona.travelSpeed * 0.6f * W));
            for (float t = 0f; t < T; )
            {
                t += Time.deltaTime;
                Motor.Base = Vector2.Lerp(from, to, HumanMotor.MinJerk(t / T)); // set Base every frame while pressed
                yield return null;
            }
            yield return Motor.Wait(Rng.Range(Persona.holdBeforeRelease));
            yield return Motor.Release();
        }
    }
}
