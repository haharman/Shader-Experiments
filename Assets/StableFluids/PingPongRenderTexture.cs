using System;
using UnityEngine;

namespace StableFluids
{
    public class PingPongRenderTexture : IDisposable
    {
        private RenderTexture _read;
        private RenderTexture _write;
        
        public RenderTexture read => _read;
        public RenderTexture write => _write;

        public PingPongRenderTexture(RenderTexture firstRead, RenderTexture firstWrite)
        {
            if(firstRead == null) throw new ArgumentNullException(nameof(firstRead));
            if(firstWrite == null) throw new ArgumentNullException(nameof(firstWrite));
            if(firstRead == firstWrite) throw new ArgumentException("firstRead と firstWrite が同じ参照です");
            _read = firstRead;
            _write = firstWrite;
        }
        
        public void Swap()
        {
            (_read, _write) = (_write, _read);
        }

        public void ClearRead()
        {
            var current = RenderTexture.active;
            RenderTexture.active = _read;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = current;
        }
        
        public void Dispose()
        {
            if (_read != null) { UnityEngine.Object.Destroy(_read); }
            if (_write != null) { UnityEngine.Object.Destroy(_write); }
            
            _read = null;
            _write = null;
        }
    }
}