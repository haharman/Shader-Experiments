using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ScriptableObjectからパラメーターを受け取り、Initializeする
/// 各クラス間のRenderTextureのやりとりを行う
/// 依存注入は行わない
/// </summary>
public class Bootstrapper : MonoBehaviour
{
    [SerializeField] private StableFluids stableFluids;
    [SerializeField] private Particles particles;
    [SerializeField] private Dye dye;
    [SerializeField] private VelocityInputMock velocityInputMock;
    [SerializeField] private int width;
    [SerializeField] private int height;
    [SerializeField] private RawImage image; 
    [SerializeField] private Material addMat;

    private bool _initialized;
    private RenderTexture _combined;

    private void Start()
    {
        if (stableFluids == null) {
            Debug.LogError("[Bootstrapper] stableFluids を割り当ててください");
            return;
        }
        if (particles == null)
        {
            Debug.LogError("[Bootstrapper] Particles を割り当ててください");
            return;
        }
        if (dye == null)
        {
            Debug.LogError("[Bootstrapper] Dye を割り当ててください");
            return;
        }

        if (velocityInputMock == null)
        {
            Debug.LogError("[Bootstrapper] velocityInputMock を割り当ててください");
            return;
        }

        _initialized = true;
        
        stableFluids.Initialize(width, height);
        particles.Initialize(width,height);
        dye.Initialize(width,height);
        _combined = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
        image.texture = _combined;
    }

    private void Update()
    {
        if (!_initialized) return;
        RenderTexture velocityField = stableFluids.Tick();
        RenderTexture dyeTexture = dye.Tick(velocityField, Time.deltaTime);
        RenderTexture particleTexture = particles.Tick(velocityField, Time.deltaTime);

        Graphics.Blit(dyeTexture, _combined);
        //Graphics.Blit(Texture2D.blackTexture,_combined);
        Graphics.Blit(particleTexture, _combined, addMat);
    }

    private void OnDestroy()
    {
        if(_combined != null) _combined.Release();
        _combined = null;
    }
    
}