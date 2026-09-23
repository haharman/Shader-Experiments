using UnityEngine;

namespace StableFluids
{
    public class PingPongRenderTexture
    {
        private RenderTexture _read;
        private RenderTexture _write;
        
        public RenderTexture read => _read;
        public RenderTexture write => _write;

        public PingPongRenderTexture(RenderTexture firstRead, RenderTexture firstWrite)
        {
            if(firstRead == null) throw new System.ArgumentNullException(nameof(firstRead));
            if(firstWrite == null) throw new System.ArgumentNullException(nameof(firstWrite));
            if(firstRead == firstWrite) throw new System.ArgumentException("firstRead と firstWrite が同じ参照です");
            _read = firstRead;
            _write = firstWrite;
        }
        
        public void Swap()
        {
            (_read, _write) = (_write, _read);
        }
    }
}