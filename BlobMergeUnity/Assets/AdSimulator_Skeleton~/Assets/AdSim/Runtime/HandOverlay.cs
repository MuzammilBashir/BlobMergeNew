using UnityEngine;

namespace AdSim
{
    /// <summary>
    /// Draws the hand sprite with its fingertip on the virtual pointer, in front of everything the capture camera sees.
    /// Pressing shrinks the hand around the fingertip (pushing into the screen), so the contact point never moves;
    /// after a release it stays pressed-small a moment (still "on" the piece) and eases back. Profiles may opt into a
    /// separate click sprite instead (<see cref="HandProfile.useClickSprite"/>), which swaps pose but can jump.
    /// Optional entrance: starts scaled up (close to the camera) and settles, like the reference ad.
    /// </summary>
    public class HandOverlay : MonoBehaviour
    {
        Camera _cam;
        VirtualPointer _pointer;
        HandProfile _profile;
        SpriteRenderer _sr;
        Sprite _idle, _click;
        float _scale;
        float _entranceTime = -1f, _entranceDuration;
        float _press;          // 0 = up, 1 = fully pressed
        float _releaseHold;    // seconds left to stay pressed-small after a release
        bool _wasDown;
        const float EntranceScale = 1.5f;

        public void Init(Camera cam, VirtualPointer pointer, HandProfile profile, Texture2D idle, Texture2D click,
                         float handScale, float captureWidthPx)
        {
            _cam = cam;
            _pointer = pointer;
            _profile = profile;
            _idle = MakeSprite(idle, profile.idleTip, profile.imageSize);
            _click = profile.useClickSprite && click != null ? MakeSprite(click, profile.clickTip, profile.imageSize) : _idle;
            _sr = gameObject.AddComponent<SpriteRenderer>();
            _sr.sprite = _idle;
            _sr.sortingOrder = 32000;
            var mat = new Material(Shader.Find("Sprites/Default")) { renderQueue = 4000 };
            _sr.sharedMaterial = mat;

            // world width of the visible hand = handScale * screen width
            float screenWorldW = cam.orthographicSize * 2f * cam.aspect;
            float spriteWorldW = idle.width / _idle.pixelsPerUnit;
            float visibleWorldW = spriteWorldW * profile.visibleWidthPx / profile.imageSize.x;
            _scale = handScale * screenWorldW / visibleWorldW;
            LateUpdate();
        }

        static Sprite MakeSprite(Texture2D tex, Vector2 tipFromTopLeft, Vector2 imageSize)
        {
            var pivot = new Vector2(tipFromTopLeft.x / imageSize.x, 1f - tipFromTopLeft.y / imageSize.y);
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot, 100f, 0, SpriteMeshType.FullRect);
        }

        public void PlayEntrance(float seconds)
        {
            _entranceTime = 0f;
            _entranceDuration = Mathf.Max(0.05f, seconds);
        }

        void LateUpdate()
        {
            if (_cam == null) return;
            float dt = Time.deltaTime;
            float k = 1f;
            if (_entranceTime >= 0f)
            {
                _entranceTime += dt;
                float u = HumanMotor.MinJerk(_entranceTime / _entranceDuration);
                k = Mathf.Lerp(EntranceScale, 1f, u);
                if (_entranceTime >= _entranceDuration) _entranceTime = -1f;
            }

            bool down = _pointer.Down;
            if (_wasDown && !down) _releaseHold = _profile.releaseHold;
            _wasDown = down;
            if (down)
                _press = Mathf.MoveTowards(_press, 1f, dt / Mathf.Max(0.01f, _profile.pressIn));
            else if (_releaseHold > 0f)
                _releaseHold -= dt;
            else
                _press = Mathf.MoveTowards(_press, 0f, dt / Mathf.Max(0.01f, _profile.releaseOut));
            float press = Mathf.SmoothStep(0f, 1f, _press);
            k *= Mathf.Lerp(1f, _profile.pressScale, press);

            float dist = 1f + _cam.nearClipPlane; // just in front of the camera
            transform.position = _cam.ScreenToWorldPoint(new Vector3(_pointer.Position.x, _pointer.Position.y, dist));
            transform.localScale = Vector3.one * (_scale * k);
            _sr.sprite = down ? _click : _idle;
        }
    }
}
