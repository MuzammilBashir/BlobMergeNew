using UnityEngine;

namespace AdSim
{
    /// <summary>
    /// A scripted single pointer in capture-target pixels (origin bottom-left, like Input/Camera screen space).
    /// The director sets <see cref="Position"/>/<see cref="Down"/> each frame and calls <see cref="Commit"/>;
    /// the game reads it through its input override instead of the device.
    /// </summary>
    public class VirtualPointer
    {
        public Vector2 Position;
        public bool Down;
        public bool Pressed { get; private set; }
        public bool Released { get; private set; }
        bool _wasDown;

        public void Commit()
        {
            Pressed = Down && !_wasDown;
            Released = !Down && _wasDown;
            _wasDown = Down;
        }
    }
}
