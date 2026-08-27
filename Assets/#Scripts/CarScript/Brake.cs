using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//前後輪へ配分するブレーキトルクを計算するクラス
[System.Serializable]
public class Brake : MonoBehaviour
{
    //制動時に前輪へ配分する割合
    [SerializeField,Range(0f,1f)]
    float m_frontBrakeBias = 0.62f;

    //通常ブレーキで発生させる最大トルク
    [SerializeField]
    float m_maxBrakeTorque = 2200f;

    //後輪だけを制動するハンドブレーキ状態
    [SerializeField]
    bool m_onHandBrake;

    //VehicleControllerから受け取るブレーキ入力
    float m_BreakeInput;
    public float BrakeInput
    {
        get {  return m_BreakeInput; }
        set { m_BreakeInput = value; }
    }

    //前輪または後輪へ渡すブレーキトルクを取得する関数
    public float GetBrakeTorque(bool _isFront)
    {
        float totalBrakeTorque = m_maxBrakeTorque * m_BreakeInput;
        float frontBrakeTorque;
        float rearBrakeTorque;

        if(m_onHandBrake)
        {
            frontBrakeTorque = 0f;
            rearBrakeTorque = totalBrakeTorque;
        }
        else
        {
            frontBrakeTorque = totalBrakeTorque * m_frontBrakeBias;
            rearBrakeTorque = totalBrakeTorque - frontBrakeTorque;
        }

        return (_isFront) ? frontBrakeTorque : rearBrakeTorque;
    }
}

