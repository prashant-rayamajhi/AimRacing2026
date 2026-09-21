// 簡略版マジックフォーミュラ曲線
using UnityEngine;

[System.Serializable]
public class MagicFormula
{
    // 係数
    [SerializeField]
    float B_stiffness = 10f; // 剛性係数
    [SerializeField]
    float C_shape = 1.9f; // 形状係数
    [SerializeField]
    float D_peak = 1f; // ピーク値
    [SerializeField]
    float E_curvature = 1f; // 曲率係数
    const int m_peakSlipResolution = 1000; // ピークスリップ値を計算する解像度
    [SerializeField, ShowInInspector]
    float m_peakSlipRatio;
    [SerializeField, ShowInInspector]
    float m_peakSlipAngle;
#region プロパティ
    public float PeakSlipRatio => m_peakSlipRatio;
    public float PeakSlipAngle => m_peakSlipAngle;
    public float B_val { get => B_stiffness; }
    public float C_val { get => C_shape; }
    public float D_val { get => D_peak; }
    public float E_val { get => E_curvature; }

#endregion
    public void Initialize()
    {
        CalcPeakSlipRatio();
        CalcPeakSlipAngle();
    }

    public float Evaluate(in float _slip)
    {
        var B = B_stiffness;
        var C = C_shape;
        var D = D_peak;
        var E = E_curvature;
        var x = _slip;
        return D * Mathf.Sin(C * Mathf.Atan(B * x - E * (B * x - Mathf.Atan(B * x))));
    }

    void CalcPeakSlipRatio()
    {
        float max = 0f;
        float calcCoeff = 1f / m_peakSlipResolution;
        // スリップ率が0%～100%の範囲で、最大値のスリップ率を求める
        for (int i = 1; i <= m_peakSlipResolution; ++i)
        {
            float tmp = Evaluate(i * calcCoeff);
            if (max < tmp)
            {
                max = tmp;
                m_peakSlipRatio = i * calcCoeff;
            }
            else
            {
                m_peakSlipRatio = i * calcCoeff;
                break;
            }
        }
    }

    void CalcPeakSlipAngle()
    {
        float max = 0f;
        float calcCoeff = 90f / m_peakSlipResolution;
        // スリップ角が0°～90°の範囲で、最大値のスリップ角を求める
        for (int i = 1; i <= m_peakSlipResolution; ++i)
        {
            float tmp = Evaluate(i * calcCoeff);
            if (max < tmp)
            {
                max = tmp;
                m_peakSlipAngle = i * calcCoeff;
            }
            else
            {
                m_peakSlipAngle = i * calcCoeff;
                break;
            }
        }
    }
}
