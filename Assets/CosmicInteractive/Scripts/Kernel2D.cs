using System;
using UnityEngine;

/// <summary>
/// RTのすべてのテクセルに対して処理を行うカーネルを扱いやすくするラッパークラス
/// 1テクセル1スレッド、切り上げDispatch、シェーダー側ではみ出しチェック必須
/// </summary>
public class Kernel2D
{
    public ComputeShader Shader { get; }
    public int Index { get; }
    public Vector3Int ThreadGroupSizes { get; }

    public Kernel2D(ComputeShader shader, string name)
    {
        if (shader == null) throw new ArgumentNullException(nameof(shader));
        if (name == null) throw new ArgumentNullException(nameof(name));
        Shader = shader;
        Index = shader.FindKernel(name);
        uint x, y, z;
        shader.GetKernelThreadGroupSizes(Index, out x, out y, out z);
        if (z != 1) throw new ArgumentException($"Kernel2D は numthreads の z が 1 のカーネルのみ対応 kernel{name} z:{z}", nameof(name));
        ThreadGroupSizes = new Vector3Int((int)x, (int)y, (int)z);
    }
    
    public void SetTexture(string name, Texture texture)
    {
        if (name == null) throw new ArgumentNullException(nameof(name));
        if (texture == null) throw new ArgumentNullException(nameof(texture));
        Shader.SetTexture(Index, name, texture);
    }
    
    public void SetBuffer(string name, ComputeBuffer buffer)
    {
        if (name == null) throw new ArgumentNullException(nameof(name));
        if (buffer == null) throw new ArgumentNullException(nameof(buffer));
        Shader.SetBuffer(Index, name, buffer);
    }

    public void Dispatch(Vector2Int resolution)
    {
        if (resolution.x <= 0 || resolution.y <= 0) throw new ArgumentOutOfRangeException(nameof(resolution));
        Shader.Dispatch(Index,
            (resolution.x + ThreadGroupSizes.x - 1) / ThreadGroupSizes.x,
            (resolution.y + ThreadGroupSizes.y - 1) / ThreadGroupSizes.y,
            1);
    }
}