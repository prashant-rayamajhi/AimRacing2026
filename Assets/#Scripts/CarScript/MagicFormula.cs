// 簡略版マジックフォーミュラ曲線
using UnityEngine;

[System.Serializable]
public class MagicFormula
{
    // 係数
    [UnityEngine.Serialization.FormerlySerializedAs("B_stiffness")]
    [SerializeField]
    float m_bStiffness = 10f; // 剛性係数
    [UnityEngine.Serialization.FormerlySerializedAs("C_shape")]
    [SerializeField]
    float m_cShape = 1.9f; // 形状係数
    [UnityEngine.Serialization.FormerlySerializedAs("D_peak")]
    [SerializeField]
    float m_dPeak = 1f; // ピーク値
    [UnityEngine.Serialization.FormerlySerializedAs("E_curvature")]
    [SerializeField]
    float m_eCurvature = 1f; // 曲率係数
    const int m_peakSlipResolution = 1000; // ピークスリップ値を計算する解像度
    [SerializeField, ShowInInspector]
    float m_peakSlipRatio;
    [SerializeField, ShowInInspector]
    float m_peakSlipAngle;
#region プロパティ
    public float PeakSlipRatio => m_peakSlipRatio;
    public float PeakSlipAngle => m_peakSlipAngle;
    public float B_val { get => m_bStiffness; }
    public float C_val { get => m_cShape; }
    public float D_val { get => m_dPeak; }
    public float E_val { get => m_eCurvature; }

#endregion
    public void Initialize()
    {
        CalcPeakSlipRatio();
        CalcPeakSlipAngle();
    }

    public float Evaluate(in float _slip)
    {
        var b = m_bStiffness;
        var c = m_cShape;
        var d = m_dPeak;
        var e = m_eCurvature;
        var x = _slip;
        return d * Mathf.Sin(c * Mathf.Atan(b * x - e * (b * x - Mathf.Atan(b * x))));
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
