using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public class TextDye : MonoBehaviour
{
    [SerializeField] private Texture2D[] textTex;
    [SerializeField] private ComputeShader shader;
    [SerializeField, Range(0f, 1f)] private float correctionIntensity;
    [SerializeField] private Material addMat;
    [SerializeField] private RawImage rawImage;
    [SerializeField] private float fadeInDuration;
    [SerializeField] private float holdDuration;
    [SerializeField] private float flowDuration;
    [SerializeField] private float restDuration;

    private PingPongRenderTexture _dye;
    private int _advectDyeKernelIndex;
    private Vector3Int _advectDyeGroupSize;
    private int _w, _h;
    private int _textureIndex;
    private bool _doesAdvect;
    private float _advectWeight;

    
    public void Initialize(int w, int h)
    {
        _w = w;
        _h = h;
        
        // RTの初期化
        var dyeFirstReadRt = new RenderTexture(_w, _h, 0, RenderTextureFormat.ARGBHalf) { enableRandomWrite = true };
        var dyeFirstWriteRt = new RenderTexture(_w, _h, 0, RenderTextureFormat.ARGBHalf) { enableRandomWrite = true };
        dyeFirstReadRt.Create();
        dyeFirstWriteRt.Create();
        _dye = new PingPongRenderTexture(dyeFirstReadRt, dyeFirstWriteRt);

        // カーネル情報の取得
        _advectDyeKernelIndex = shader.FindKernel("AdvectDye");
        uint x, y, z;
        shader.GetKernelThreadGroupSizes(_advectDyeKernelIndex, out x, out y, out z);
        _advectDyeGroupSize = new Vector3Int((int)x, (int)y, (int)z);
        
        // シーケンス
        _textureIndex = 0;
        DOTween.Sequence()
            .AppendCallback(InitDye)
            .Append(rawImage.DOFade(1.0f, fadeInDuration))
            .AppendInterval(holdDuration)
            .AppendCallback(() =>
            {
                _advectWeight = 0.0f;
                _doesAdvect = true;
            })
            .Append(rawImage.DOFade(0.0f, flowDuration))
            .Join(DOTween.To(() => _advectWeight, v => _advectWeight = v, 2.0f, flowDuration))
            .AppendCallback(() => _doesAdvect = false)
            .AppendInterval(restDuration)
            .SetLoops(-1)
            .SetLink(gameObject);
        
        var color = rawImage.color;
        color.a = 0.0f;
        rawImage.color = color;
        rawImage.texture = _dye.read;
    }
    
    public void Tick(RenderTexture velocityField, float deltaTime)
    {
        if (!_doesAdvect) return;
        shader.SetFloat("_AdvectWeight", _advectWeight);
        shader.SetFloat("_DeltaTime", deltaTime);
        shader.SetInts("_Resolution", _w, _h);
        shader.SetFloat("_CorrectionIntensity", correctionIntensity);
        shader.SetTexture(_advectDyeKernelIndex, "_VelocityFieldRead", velocityField);
        shader.SetTexture(_advectDyeKernelIndex, "_DyeTextureRead", _dye.read);
        shader.SetTexture(_advectDyeKernelIndex, "_DyeTextureWrite", _dye.write);
        
        shader.Dispatch(_advectDyeKernelIndex,
            (_w + _advectDyeGroupSize.x - 1) / _advectDyeGroupSize.x,
            (_h + _advectDyeGroupSize.y - 1) / _advectDyeGroupSize.y,
            1);
        
        _dye.Swap();

        rawImage.texture = _dye.read;
    }
    
    private void InitDye()
    {
        var texture = textTex[_textureIndex % textTex.Length];
        _textureIndex++;
        
        BlitFitted(texture, _dye.read);
        
        rawImage.texture = _dye.read;
        var color = rawImage.color;
        color.a = 0.0f;
        rawImage.color = color;

        _doesAdvect = false;
    }

    private void BlitFitted(Texture2D src, RenderTexture dst)
    {
        // RTを透明にする
        var width = dst.width;
        var height = dst.height;
        var prev = RenderTexture.active;
        RenderTexture.active = dst;
        GL.Clear(false, true, Color.clear);
        RenderTexture.active = prev;
        
        // 中央いっぱいにsourceを加算する
        float scale = Mathf.Min((float)width / src.width, (float)height / src.height);
        Vector2 size = new Vector2(src.width, src.height) * scale;
        Vector2 uvScale = new Vector2(width, height) / size;
        Vector2 uvOffset = (new Vector2(1, 1) - uvScale) / 2;
        addMat.SetVector("_ScaleOffsetUV", new Vector4(uvScale.x, uvScale.y, uvOffset.x, uvOffset.y));
        Graphics.Blit(src, dst, addMat);
    }

    private void OnDestroy()
    {
        _dye?.Dispose();
    }
}