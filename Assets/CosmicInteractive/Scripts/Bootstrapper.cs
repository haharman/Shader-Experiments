using UnityEngine;

/// <summary>
/// ScriptableObjectからパラメーターを受け取り、Initializeする
/// 各クラス間のRenderTextureのやりとりを行う
/// 依存注入は行わない
/// </summary>
public class Bootstrapper : MonoBehaviour
{
    [SerializeField] private StableFluids stableFluids;
    [SerializeField] private Particles particles;
    [SerializeField] private TextDye textDye;
    [SerializeField] private Boids boids;
    [SerializeField] private Nebula nebula;
    [SerializeField] private SolarBodies solarBodies;
    [SerializeField] private LetterBoxCamera letterBoxCamera;
    [SerializeField] private Vector2Int resolution;
    [SerializeField, Min(1)] private int velocityDownsample;

    private bool _initialized;

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
        if (textDye == null)
        {
            Debug.LogError("[Bootstrapper] TextDye を割り当ててください");
            return;
        }
        if (boids == null)
        {
            Debug.LogError("[Bootstrapper] Boids を割り当ててください");
        }
        if (letterBoxCamera == null)
        {
            Debug.LogError("[Bootstrapper] LetterBoxCamera を割り当ててください");
        }

        _initialized = true;
        
        stableFluids.Initialize(resolution / velocityDownsample);
        particles.Initialize(resolution);
        textDye.Initialize(resolution);
        boids.Initialize(resolution);
        nebula.Initialize(resolution);
        letterBoxCamera.Initialize(resolution);
        if (solarBodies != null) { solarBodies.Initialize(resolution); }
    }

    private void Update()
    {
        if (!_initialized) return;
        RenderTexture velocityField = stableFluids.Tick();
        textDye.Tick(velocityField, Time.deltaTime);
        RenderTexture particleTexture = particles.Tick(velocityField, Time.deltaTime);
        boids.Tick(velocityField);
        nebula.Tick(velocityField, particleTexture);
        if (solarBodies != null) { solarBodies.Tick(); }
    }
}
