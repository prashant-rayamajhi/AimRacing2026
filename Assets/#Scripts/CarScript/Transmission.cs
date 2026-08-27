using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

//ATとMTのギア選択および変速タイミングを管理するクラス
public class Transmission : MonoBehaviour
{
    public enum TransmissionType
    {
        Automatic,
        Manual
    }

    [SerializeField]
    TransmissionType m_type;

    [Range(-1, 7)]
  
    //現在のギア 
    [SerializeField]
    int m_currentGear;
  
    //ギア比のリスト
    [SerializeField]
    List<float> m_gearRatioList = new List<float>();

    //既存シーンのMT設定を残しつつ、AT選択時だけ公式8速比へ切り替える。
    static readonly float[] GRDatGearRatios = { 0f, 4.435f, 2.809f, 1.933f, 1.497f, 1.266f, 1.000f, 0.793f, 0.650f };

    //WheelCollider上で1速の到達速度が公称計算値より少し低くなるため、ゲーム用に早めの変速点へ補正する。
    static readonly float[] GRDatShiftUpSpeedKph = { 0f, 50f, 85f, 125f, 160f, 188f, 210f, 228f, float.PositiveInfinity };

    //6MTの公式後退ギア比
    const float k_GRMtReverseGearRatio = 3.831f;

    [SerializeField]
    float m_reverseGearRatio = 3.590f;
  
    //このスピード以下にならないとリバースに入らない
    [SerializeField]
    float m_reverseChangeSpeed = 5f;
    [SerializeField]
  
    //このRPM以上だとギアを下げられない
    float m_banShiftDownRPM = 6500f;
    [SerializeField]
    float m_shiftUpInterval = 1f;
    [SerializeField]
    float m_shiftDownInterval = 0.5f;
    [SerializeField]
    float m_gearChangingTime = 0.3f;

    //ギア切り替えの時間
    [SerializeField, ShowInInspector]
    bool m_isGearChanging;

    float m_lastShiftChangeTime = 0f;

    [Header("AT Settings")]
    [SerializeField]
  
    //単位[Km/h]
    List<float> m_shiftUpSpeed = new List<float>();
    [SerializeField, Range(0f, 1f)]
  
    //スピードの影響度(0.5の場合、必要な速度の半分でシフトアップする)
    float m_speedInfluence = 1f;
    [SerializeField]
    float m_shiftUpEngineRPM = 6200f;
    [SerializeField]
    float m_shiftDownEngineRPM = 2600f;
    [SerializeField]
  
    //1速に下げるためのRPM
    float m_shiftDownEngineRPM_1st = 1800f;
    [SerializeField, Range(0.6f, 0.95f)] float m_minimumUpshiftSpeedRatio = 0.82f;
    [SerializeField, Range(0.5f, 0.9f)] float m_downshiftSpeedRatio = 0.72f;
    [SerializeField, Range(0f, 1f)] float m_fullThrottleThreshold = 0.7f;
    [SerializeField, Range(1f, 15f)] float m_fullThrottleRestartSpeed = 8f;

    [SerializeField, ShowInInspector]
    bool m_isShiftUp = false;
    [SerializeField, ShowInInspector]
    bool m_isShiftDown = false;

    float m_engineRPM;
    float m_vehicleSpeed;
    float m_throttleInput;
    bool m_isPullUp = false;

    #region プロパティ
   
    //AT/MT 切り替え
    public TransmissionType Type
    {
        get => m_type;
        set
        {
            m_type = value;
            m_currentGear = Mathf.Clamp(m_currentGear, -1, MaxForwardGear);
        }
    }

    //現在のギア比
    public float CurrentGearRatio
    {
        get
        {
            if (m_currentGear >= 0) { return GetForwardGearRatio(m_currentGear); }
            else { return m_type == TransmissionType.Automatic ? -m_reverseGearRatio : -k_GRMtReverseGearRatio; }
        }
    }

    public float FinalDriveRatio
    {
        get
        {
            if (m_type == TransmissionType.Automatic) { return 3.329f; }

            //6MT公式値は1～4速と5～6速で最終減速比が異なる。
            return m_currentGear >= 5 ? 3.350f : 3.941f;
        }
    }
    public int MaxForwardGear => m_type == TransmissionType.Automatic ? GRDatGearRatios.Length - 1 : Mathf.Max(0, m_gearRatioList.Count - 1);

    public int ActiveGear
    {
        get => m_currentGear;
    }

    public bool IsPullUp
    {
        set => m_isPullUp = value;
    }

    public bool IsGearChanging => m_isGearChanging;
    public bool IsShiftUp => m_isShiftUp;
    public bool IsShiftDown => m_isShiftDown;
    #endregion

    //初期化処理
    public void Initialize()
    {

    }

    //レース開始前に前進1速を選択してカウント終了直後に発進できるようにする関数
    public void PrepareForwardStart()
    {
        m_currentGear = 1;
        m_isGearChanging = false;
        m_isShiftUp = false;
        m_isShiftDown = false;
        m_lastShiftChangeTime = Time.time - m_gearChangingTime;
    }

    //復帰前に始まった変速処理だけを取り消し、選択中のギアは維持する
    public void ResetDynamics()
    {
        m_engineRPM = 0f;
        m_vehicleSpeed = 0f;
        m_isGearChanging = false;
        m_isShiftUp = false;
        m_isShiftDown = false;
        m_lastShiftChangeTime = Time.time - m_gearChangingTime;
    }

    //VehicleControllerから明示的に呼ぶ更新処理。Unity標準のFixedUpdateとは分ける。
    public void TransmissionUpdate(float _engineRPM, float _speed, float _throttleInput)
    {
        m_engineRPM = _engineRPM;
        m_vehicleSpeed = Mathf.Abs(_speed);
        m_throttleInput = Mathf.Clamp01(_throttleInput);

        switch (m_type)
        {
            case TransmissionType.Automatic:
                AutomaticShift(_speed);
                break;
        }

        if (m_gearChangingTime <= Time.time - m_lastShiftChangeTime)
        {
            m_isGearChanging = false;
            m_isShiftUp = false;
            m_isShiftDown = false;
        }
        else
        {
            m_isGearChanging = true;
        }
    }

    void AutomaticShift(float _speed)
    {
        //NとRの時は処理しない
        if (m_currentGear <= 0) { return; }


        //シフトアップの条件
        //・エンジンRPMがシフトアップRPMを超えている
        //・現在の車速がスピード * 影響度の値を超えている
        //・シフトアップ待機時間を過ぎている
        float shiftSpeed = GetAutomaticShiftSpeed(m_currentGear);
        bool reachedPowerPeak = m_engineRPM >= Mathf.Min(m_shiftUpEngineRPM, 6000f);
        bool reachedGearSpeed = _speed >= shiftSpeed * m_speedInfluence;
        bool reachedMinimumUpshiftSpeed = _speed >= shiftSpeed * m_minimumUpshiftSpeedRatio;
        float downshiftSpeed = GetAutomaticShiftSpeed(m_currentGear - 1) * m_downshiftSpeedRatio;
        bool fullThrottleAllowsDownshift = m_throttleInput < m_fullThrottleThreshold || _speed < m_fullThrottleRestartSpeed;
        if (m_currentGear < MaxForwardGear && reachedMinimumUpshiftSpeed && (reachedPowerPeak || reachedGearSpeed) &&
            m_shiftUpInterval <= Time.time - m_lastShiftChangeTime)
        {
            ShiftUp();
        }

   
        //シフトダウンの条件
        //・エンジンRPMがシフトダウンRPMを下回っている
        //・1速ではない
        //・現在の車速がひとつ前のシフトチェンジの速度を下回ってる
        //・シフトダウン待機時間を過ぎている
        if (!m_isShiftUp && m_engineRPM < m_shiftDownEngineRPM && m_currentGear != 1 && _speed < downshiftSpeed &&
            fullThrottleAllowsDownshift && m_shiftDownInterval <= Time.time - m_lastShiftChangeTime)
        {
        
            //2速の時は別のシフトダウンRPMを使う
            if (m_currentGear != 2)
            {
                ShiftDown();
            }
            else if (m_engineRPM < m_shiftDownEngineRPM_1st)
            {
                ShiftDown();
            }
        }
    }

    void ManualShift()
    {
        if (Input.GetButtonDown("ShiftUp"))
            ShiftUp();

        if (Input.GetButtonDown("ShiftDown"))
            ShiftDown();
    }

    public void ShiftUp()
    {
    
        //開始前ならギアチェンジを無効化
        if (m_isPullUp) { return; }

        //経過時間(現在の時間 - 保持した時間)が待機時間を上回っていたら
        if (m_gearChangingTime <= Time.time - m_lastShiftChangeTime || m_currentGear <= 0)
        {
            m_currentGear++;
            m_isShiftUp = true;

       
            //ギア切り替え時の時間を保持
            m_lastShiftChangeTime = Time.time;
        }

    
        //ギア比リストの要素数をオーバーフローしないように
        if (m_currentGear > MaxForwardGear) { m_currentGear = MaxForwardGear; }
    }

    public void ShiftDown()
    {
    
        //開始前ならギアチェンジを無効化
        if (m_isPullUp) { return; }

        if (m_banShiftDownRPM < m_engineRPM) { return; }

        //前進中の誤操作で駆動方向が反転すると、クラッチとタイヤに不自然な負回転が出るため。
        if (m_currentGear == 0 && m_vehicleSpeed > m_reverseChangeSpeed) { return; }

        //経過時間(現在の時間 - 保持した時間)が待機時間を上回っていたら
        if (m_gearChangingTime <= Time.time - m_lastShiftChangeTime || m_currentGear <= 1)
        {
            m_currentGear--;
            m_isShiftDown = true;
       
            //ギア切り替え時の時間を保持
            m_lastShiftChangeTime = Time.time;
        }

        //1を下回らないように補正
        if (m_currentGear < -1) { m_currentGear = -1; }
    }

    public void ShiftTo(int _gear)
    {
        //スタート前の車体固定中は外部処理から前進または後退ギアへ変更させない
        if (m_isPullUp) { return; }

        //ATとMTそれぞれのギア数を超えない範囲へ指定値を収める
        int targetGear = Mathf.Clamp(_gear, -1, MaxForwardGear);
        if (targetGear == m_currentGear) { return; }

        //メーターとクラッチが変速方向を正しく判断できるように状態を更新する
        m_isShiftUp = targetGear > m_currentGear;
        m_isShiftDown = targetGear < m_currentGear;
        m_currentGear = targetGear;
        m_isGearChanging = true;
        m_lastShiftChangeTime = Time.time;
    }

    float GetForwardGearRatio(int gear)
    {
        if (m_type == TransmissionType.Automatic) { return GRDatGearRatios[Mathf.Clamp(gear, 0, GRDatGearRatios.Length - 1)]; }

        if (m_gearRatioList == null || m_gearRatioList.Count == 0) { return 0f; }

        return m_gearRatioList[Mathf.Clamp(gear, 0, m_gearRatioList.Count - 1)];
    }

    float GetAutomaticShiftSpeed(int gear)
    {
        return GRDatShiftUpSpeedKph[Mathf.Clamp(gear, 0, GRDatShiftUpSpeedKph.Length - 1)];
    }
}
