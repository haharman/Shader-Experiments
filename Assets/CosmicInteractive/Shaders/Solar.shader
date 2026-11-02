// 球メッシュに割り当てるだけで恒星になる URP 用 Unlit シェーダー。
//  - Body   : 色温度(K)ベースの黒体放射色 + 粒状斑 + 超粒状斑 + 磁場フィラメント(明るい網目) + 周縁減光 + 自転/表面の流れ
//  - Corona : 球を拡大したメッシュ上で視線と球の距離から、球の外側へグロー/コロナ(放射状ストリーマ付き)を広げる
//  どちらも1パスで描く(複数パス構成だと URP で2つ目が描かれないことがあるため)。
// 出力は HDR (Surface Intensity > 1)。Volume の Bloom を前提に調整してある。
// 球の中心は原点、均一スケール前提(Unity 標準 Sphere でも任意半径の球メッシュでも可)。
// Queue=Transparent、ZWrite Off。手前の不透明物には隠れるが、星同士の前後はカメラ距離のソート任せ。
Shader "Custom/Solar"
{
    Properties
    {
        [Header(Color)]
        _Temperature ("Temperature (K)", Range(1500, 40000)) = 5772
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Intensity ("Surface Intensity (HDR)", Range(0, 20)) = 3
        _TemperatureVariation ("Temperature Variation", Range(0, 0.6)) = 0.22
        _LimbDarkening ("Limb Darkening", Range(0, 1)) = 0.65

        [Header(Granulation)]
        _GranuleDensity ("Granule Density", Range(5, 150)) = 45
        _GranuleContrast ("Granule Contrast", Range(0, 1)) = 0.75
        _GranuleLane ("Granule Lane Width", Range(0.02, 0.5)) = 0.14
        _BoilSpeed ("Boil Speed", Range(0, 3)) = 0.5
        _SuperGranuleScale ("Super Granule Scale", Range(1, 30)) = 7
        _SuperGranuleAmount ("Super Granule Amount", Range(0, 1)) = 0.4

        [Header(Magnetic Network)]
        _NetworkScale ("Network Scale", Range(1, 20)) = 6
        _NetworkAmount ("Network Amount", Range(0, 2)) = 0.9
        _NetworkSharpness ("Network Sharpness", Range(1, 10)) = 4
        _ActivityCoverage ("Activity Coverage", Range(0, 1)) = 0.5

        [Header(Rotation and Flow)]
        _RotationSpeed ("Rotation Speed (deg per sec)", Range(-60, 60)) = 6
        _FlowScale ("Flow Scale", Range(0.5, 8)) = 2.5
        _FlowAmount ("Flow Amount", Range(0, 1)) = 0.35
        _FlowSpeed ("Flow Speed", Range(0, 1)) = 0.08

        [Header(Corona)]
        _CoronaSize ("Corona Size (x radius)", Range(1.05, 4)) = 1.9
        _CoronaIntensity ("Corona Intensity (HDR)", Range(0, 10)) = 1.6
        _CoronaFalloff ("Corona Falloff", Range(0.5, 16)) = 4
        _CoronaTint ("Corona Tint", Color) = (1, 1, 1, 1)
        _CoronaStreamers ("Corona Streamers", Range(0, 1)) = 0.6
        _CoronaStreamerScale ("Corona Streamer Scale", Range(1, 16)) = 4
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        HLSLINCLUDE
        #pragma target 3.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float _Temperature;
            half4 _Tint;
            float _Intensity;
            float _TemperatureVariation;
            float _LimbDarkening;

            float _GranuleDensity;
            float _GranuleContrast;
            float _GranuleLane;
            float _BoilSpeed;
            float _SuperGranuleScale;
            float _SuperGranuleAmount;

            float _NetworkScale;
            float _NetworkAmount;
            float _NetworkSharpness;
            float _ActivityCoverage;

            float _RotationSpeed;
            float _FlowScale;
            float _FlowAmount;
            float _FlowSpeed;

            float _CoronaSize;
            float _CoronaIntensity;
            float _CoronaFalloff;
            half4 _CoronaTint;
            float _CoronaStreamers;
            float _CoronaStreamerScale;
        CBUFFER_END

        // ---------------------------------------------------------------- noise

        float Hash13(float3 p)
        {
            p = frac(p * 0.1031);
            p += dot(p, p.zyx + 31.32);
            return frac((p.x + p.y) * p.z);
        }

        float3 Hash33(float3 p)
        {
            p = frac(p * float3(0.1031, 0.1030, 0.0973));
            p += dot(p, p.yxz + 33.33);
            return frac((p.xxy + p.yxx) * p.zyx);
        }

        // 0..1 の値ノイズ(5次補間)
        float Noise3(float3 p)
        {
            float3 i = floor(p);
            float3 f = frac(p);
            f = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);

            float n000 = Hash13(i);
            float n100 = Hash13(i + float3(1, 0, 0));
            float n010 = Hash13(i + float3(0, 1, 0));
            float n110 = Hash13(i + float3(1, 1, 0));
            float n001 = Hash13(i + float3(0, 0, 1));
            float n101 = Hash13(i + float3(1, 0, 1));
            float n011 = Hash13(i + float3(0, 1, 1));
            float n111 = Hash13(i + float3(1, 1, 1));

            return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                        lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
        }

        // 2オクターブ。0..1
        float Fbm2(float3 p)
        {
            return Noise3(p) * 0.65 + Noise3(p * 2.03 + 17.7) * 0.35;
        }

        // 3D セルラーノイズ。(F1, F2, 最寄りセルの乱数)。特徴点は anim で揺らして「沸騰」させる
        float3 Worley(float3 p, float anim)
        {
            float3 ip = floor(p);
            float3 fp = frac(p);
            float f1 = 8.0;
            float f2 = 8.0;
            float id = 0.0;

            [unroll]
            for (int z = -1; z <= 1; z++)
            {
                [unroll]
                for (int y = -1; y <= 1; y++)
                {
                    [unroll]
                    for (int x = -1; x <= 1; x++)
                    {
                        float3 g = float3(x, y, z);
                        float3 h = Hash33(ip + g);
                        float3 o = 0.5 + 0.5 * sin(anim * (0.5 + h.zxy) + 6.2831853 * h);
                        float3 d = g + o - fp;
                        float dd = dot(d, d);
                        if (dd < f1)
                        {
                            f2 = f1;
                            f1 = dd;
                            id = h.x;
                        }
                        else if (dd < f2)
                        {
                            f2 = dd;
                        }
                    }
                }
            }
            return float3(sqrt(f1), sqrt(f2), id);
        }

        // ---------------------------------------------------------------- color

        // 黒体放射の近似色(Tanner Helland)。1000K〜40000K。出力はリニア(ガンマ空間プロジェクトではそのまま)
        float3 BlackbodyRGB(float kelvin)
        {
            float t = clamp(kelvin, 1000.0, 40000.0) * 0.01;
            float tm60 = max(t - 60.0, 1e-3);
            float3 c;
            c.r = t <= 66.0 ? 1.0 : 1.292936186 * pow(tm60, -0.1332047592);
            c.g = t <= 66.0 ? 0.390081579 * log(t) - 0.631841444 : 1.129890861 * pow(tm60, -0.0755148492);
            c.b = t >= 66.0 ? 1.0 : (t <= 19.0 ? 0.0 : 0.543206789 * log(max(t - 10.0, 1e-3)) - 1.196254089);
            c = saturate(c);
        #ifndef UNITY_COLORSPACE_GAMMA
            c = pow(c, 2.2);
        #endif
            return c;
        }
        ENDHLSL

        // 球を _CoronaSize 倍に拡大したメッシュ1枚・1パスで、内側(恒星本体)と外側(コロナ)を両方描く。
        // 本体は視線と球のレイ交差から法線を求める。出力は Blend One OneMinusSrcAlpha で、
        // 本体はα=1(上書き)、コロナはα=0(加算)になる。
        Pass
        {
            Name "Solar"

            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float meshRadius : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                // 原点中心に拡大(球メッシュ前提)
                float3 posOS = IN.positionOS.xyz * _CoronaSize;
                OUT.positionWS = TransformObjectToWorld(posOS);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.meshRadius = length(IN.positionOS.xyz);
                return OUT;
            }

            // 恒星本体の色(リニア・HDR)。d: オブジェクト空間の単位方向、mu: 視線との cos、pxDir: 1ピクセルあたりの方向変化
            float3 ShadeBody(float3 d, float mu, float pxDir)
            {
                float detailFade = 1.0 - smoothstep(0.25, 0.7, pxDir * _GranuleDensity);

                // 自転(オブジェクト Y 軸まわり)
                float s, c;
                sincos(_Time.y * radians(_RotationSpeed), s, c);
                float3 p = float3(c * d.x + s * d.z, d.y, -s * d.x + c * d.z);

                // 表面の流れ: 低周波ベクトル場でサンプル位置を歪ませる
                float ft = _Time.y * _FlowSpeed;
                float3 fp = p * _FlowScale + float3(ft, -ft * 0.6, ft * 0.8);
                float3 w = float3(Fbm2(fp),
                                  Fbm2(fp + float3(31.4, 17.1, 5.7)),
                                  Fbm2(fp + float3(7.3, 43.9, 21.2))) * 2.0 - 1.0;

                // 粒状斑 + 超粒状斑
                float boil = _Time.y * _BoilSpeed * 2.0;
                float3 wk = Worley(p * _GranuleDensity + w * (_FlowAmount * 3.0), boil);
                float edge = smoothstep(0.0, _GranuleLane, wk.y - wk.x);
                float granule = edge * (0.65 + 0.35 * saturate(1.0 - wk.x * 1.1)) * lerp(0.75, 1.0, wk.z);

                float3 wks = Worley(p * _SuperGranuleScale + w * (_FlowAmount * 0.8) + 11.3, boil * 0.3);
                float superG = lerp(0.65, 1.15, smoothstep(0.0, 0.35, wks.y - wks.x));
                float superMod = lerp(1.0, superG, _SuperGranuleAmount);

                float v = lerp(1.0, granule * 1.5, _GranuleContrast * detailFade) * superMod;

                // 磁場フィラメント(活動領域に出る明るい網目)
                float3 q = p * _NetworkScale + w * (_FlowAmount * 0.7);
                float r1 = 1.0 - abs(2.0 * Noise3(q) - 1.0);
                float r2 = 1.0 - abs(2.0 * Noise3(q * 2.1 + 13.7) - 1.0);
                float ridge = pow(saturate(r1 * 0.65 + r2 * 0.35), _NetworkSharpness);
                float act = Noise3(p * (_NetworkScale * 0.4) + float3(5.2, 1.3, 8.8) + w * 0.3);
                float thr = lerp(0.75, 0.35, _ActivityCoverage);
                float mask = smoothstep(thr - 0.12, thr + 0.12, act);
                v += ridge * mask * _NetworkAmount;
                v = max(v, 0.0);

                // 明るい所ほど高温(白く)、暗い所ほど低温(橙〜赤)
                float tLocal = _Temperature * (1.0 + (v - 1.0) * _TemperatureVariation);
                float3 col = BlackbodyRGB(tLocal) * v;

                // 周縁減光: 青ほど強く暗くなるので縁が赤みを帯びる
                float3 limb = saturate(1.0 - _LimbDarkening * float3(0.85, 1.0, 1.25) * (1.0 - mu));
                col *= limb;

                return col * _Tint.rgb * _Intensity;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float4x4 m = GetObjectToWorldMatrix();
                float3 center = float3(m._m03, m._m13, m._m23);
                float radiusWS = IN.meshRadius * length(float3(m._m00, m._m10, m._m20));

                // 視線レイ(透視/平行投影どちらも)。IN.positionWS はレイ上の点
                float3 rd = unity_OrthoParams.w < 0.5
                    ? normalize(IN.positionWS - _WorldSpaceCameraPos)
                    : -UNITY_MATRIX_V[2].xyz;
                float3 oc = center - IN.positionWS;
                float tca = dot(oc, rd);
                float d2 = max(dot(oc, oc) - tca * tca, 0.0);
                float dist = sqrt(d2);                          // 球中心から視線までの最短距離

                // 球との交点。外れたピクセルは縁(最接近点)で代用し、導関数を連続に保つ
                float thc = sqrt(max(radiusWS * radiusWS - d2, 0.0));
                float3 hit = IN.positionWS + rd * (tca - thc);
                float3 nWS = normalize(hit - center);
                float3 d = normalize(TransformWorldToObjectDir(nWS, false));
                float3 fw = fwidth(d);
                float pxDir = max(fw.x, max(fw.y, fw.z));
                float mu = saturate(dot(nWS, -rd));

                // 縁のアンチエイリアス
                float cov = saturate((radiusWS - dist) / max(fwidth(dist), 1e-5) + 0.5);

                float3 body = 0.0;
                if (cov > 0.0)
                {
                    body = ShadeBody(d, mu, pxDir);
                }

                // ---- Corona: 恒星の縁(0) → コロナの外縁(1)
                float3 corona = 0.0;
                if (cov < 1.0)
                {
                    float t = saturate((dist / radiusWS - 1.0) / (_CoronaSize - 1.0));

                    // 最接近点の方向(オブジェクト空間)で放射状ストリーマを作る
                    float3 closest = IN.positionWS + rd * tca;
                    float3 dirOS = normalize(TransformWorldToObjectDir(closest - center, false) + 1e-5);
                    float s, c;
                    sincos(_Time.y * radians(_RotationSpeed) * 0.5, s, c);
                    float3 sd = float3(c * dirOS.x + s * dirOS.z, dirOS.y, -s * dirOS.x + c * dirOS.z);
                    float n = Fbm2(sd * _CoronaStreamerScale + float3(0.0, _Time.y * 0.05, 0.0));

                    // ストリーマ: ノイズが高い方向ほど遠くまで伸びる
                    float falloff = max(_CoronaFalloff * lerp(1.0, 2.4 - 2.0 * n, _CoronaStreamers), 0.2);
                    float radial = 0.6 * exp(-t * falloff * 3.0) + 0.4 * exp(-t * falloff);
                    float fade = 1.0 - smoothstep(0.55, 1.0, t);
                    float streak = lerp(1.0, 0.5 + n, _CoronaStreamers);
                    float I = radial * fade * streak;

                    // 内側は高温、外側ほど低温(赤みがかる)
                    corona = BlackbodyRGB(_Temperature * lerp(1.1, 0.7, t)) * _CoronaTint.rgb * (I * _CoronaIntensity);
                }

                return half4(body * cov + corona * (1.0 - cov), cov);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
