// 実装中

using TMPro;
using UnityEngine;

public class Calibration : MonoBehaviour
{
    [SerializeField] private RectTransform pointerRect;
    [SerializeField] private TMP_Text guideText;
    [SerializeField] private TMP_Text countDownText;

    private void StartCalibration()
    {
        Vector2 uv = default; // TODO: 書きかけだった行。コンパイルを通すための仮の代入
        guideText.text = "Touch it.";
        guideText.text = "Keep it.";
        guideText.text = "Don't touch anything.";
        


    }
    
    private void Calibrate(Vector2 right, Vector2 left, Vector2 aspectRatio)
    {
        
    }
    
}