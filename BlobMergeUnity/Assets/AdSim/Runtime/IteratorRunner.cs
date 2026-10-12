using System.Collections;
using System.Collections.Generic;

namespace AdSim
{
    /// <summary>
    /// Steps a routine once per frame. Yielding an IEnumerator runs it inline (nested), yielding null waits a frame.
    /// </summary>
    public class IteratorRunner
    {
        readonly Stack<IEnumerator> _stack = new Stack<IEnumerator>();

        public bool Running => _stack.Count > 0;

        public void Start(IEnumerator routine)
        {
            _stack.Clear();
            if (routine != null) _stack.Push(routine);
        }

        public void Stop() => _stack.Clear();

        /// <summary>Advance to the routine's next frame yield. Returns false once it has finished.</summary>
        public bool Step()
        {
            for (int guard = 0; _stack.Count > 0 && guard < 10000; guard++)
            {
                var it = _stack.Peek();
                if (!it.MoveNext()) { _stack.Pop(); continue; }
                if (it.Current is IEnumerator sub) { _stack.Push(sub); continue; }
                return true;
            }
            return _stack.Count > 0;
        }
    }
}
