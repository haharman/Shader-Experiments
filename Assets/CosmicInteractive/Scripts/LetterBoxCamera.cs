using UnityEngine;

[RequireComponent(typeof(Camera))]
public class LetterBoxCamera : MonoBehaviour
{
    private Camera _cam;
    private bool _init;
    private int _lastWidth;
    private int _lastHeight;
    private Vector2Int _targetResolution;
    
    public void Initialize(Vector2Int targetResolution)
    {
        _cam = GetComponent<Camera>();
        _init = true;
        _targetResolution = targetResolution;
        Refresh();
    }

    void Update()
    {
        if (!_init) return;
        if (_lastWidth != Screen.width || _lastHeight != Screen.height) Refresh();
    }

    private void Refresh()
    {
        float targetAspect = (float)_targetResolution.x / _targetResolution.y;
        float w = Screen.width;
        float h = Screen.height;
        float screenAspect = w / h;
        
        if (screenAspect < targetAspect) // 上下に帯
        {
            float targetHeight = w / targetAspect;
            _cam.rect = new Rect(0f, (h - targetHeight) / 2f / h, 1, targetHeight/h);
        }
        else // 左右に帯
        {
            float targetWidth = h * targetAspect;
            _cam.rect = new Rect((w - targetWidth) / 2f / w, 0f, targetWidth/w, 1);
        }
        
        _lastWidth = Screen.width;
        _lastHeight = Screen.height;
    }
}