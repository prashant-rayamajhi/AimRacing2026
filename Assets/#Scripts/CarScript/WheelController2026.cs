using System.Collections.Generic;
using UnityEngine;

//4輪の接地、駆動、制動、サスペンション、表示位置を管理するクラス
public class WheelController2026 : MonoBehaviour
{
    //WheelColliderのリストは、前輪右、前輪左、後輪右、後輪左の順で設定する。
    [Header("Wheels (FR, FL, RR, RL)")]
    [SerializeField] List<WheelCollider> m_Wheels = new List<WheelCollider>();
    [SerializeField] List<Transform> m_WheelVisuals = new List<Transform>();
    [SerializeField] List<bool> m_IsDrive = new List<bool>();
    [SerializeField] List<bool> m_IsFront = new List<bool>();
    [SerializeField] List<bool> m_IsRight = new List<bool>();

    //車両の駆動、制動、操舵を担当するコンポーネントを参照する。
    [Header("Vehicle Parts")]
    [SerializeField] Differential m_differential;
    [SerializeField] Brake m_brake;
    [SerializeField] Steering m_steering;

    //WheelColliderのサスペンション、摩擦、ABSの設定値を調整する。
    [Header("GR Yaris Wheel / Suspension")]
    [SerializeField, Range(0.30f, 0.38f)] float m_wheelRadius = 0.334f;
    [SerializeField, Range(0.08f, 0.22f)] float m_suspensionDistance = 0.13f;
    [SerializeField, Range(20000f, 60000f)] float m_spring = 50000f;
    [SerializeField, Range(2000f, 8000f)] float m_damper = 6800f;
    [SerializeField, Range(0.3f, 0.8f)] float m_suspensionTarget = 0.50f;
    [SerializeField, Range(0f, 0.5f)] float m_forceApplicationDistance = 0.08f;

    //左右サスペンションの圧縮差を車体へ返して旋回時の過剰なロールを抑える設定
    [Header("Anti Roll Bar")]
    [SerializeField, Range(0f, 20000f)] float m_frontAntiRollStiffness = 5000f;
    [SerializeField, Range(0f, 20000f)] float m_rearAntiRollStiffness = 4200f;

    //タイヤのグリップ特性を設定する
    [Header("Tire Grip")]
    [SerializeField, Range(0.5f, 2f)] float m_forwardStiffness = 1.65f;
    [SerializeField, Range(0.5f, 2.5f)] float m_sidewaysStiffness = 2.1f;
    [SerializeField, Range(0.05f, 0.5f)] float m_tractionSlipLimit = 0.12f;
    [SerializeField, Range(0f, 1f)] float m_minimumTractionTorqueRatio = 0.35f;
    [SerializeField, Range(0.05f, 2f)] float m_tractionRecoverySpeed = 1.20f;
    [SerializeField, Range(500f, 10000f)] float m_motorTorqueApplyRate = 6000f;

    //空中判定の瞬間的な切り替わりを防ぐ時間
    [Header("Ground Detection")]
    [SerializeField, Range(0f, 0.5f)] float m_airborneDetectionDelay = 0.12f;
    [SerializeField, Range(0f, 0.03f)] float m_visualRadiusPadding = 0.012f;
    [SerializeField, Range(0f, 0.02f)] float m_minimumGroundClearance = 0.003f;
    [SerializeField, Range(0.05f, 0.25f)] float m_maximumGroundCorrection = 0.18f;

    //サスペンションが縮み切る直前だけ補助力を加えるバンプストップ設定
    [Header("Bump Stop")]
    [SerializeField, Range(0.7f, 0.95f)] float m_bumpStopStartCompression = 0.82f;
    [SerializeField, Range(0f, 12000f)] float m_bumpStopMaximumForce = 6500f;

    //ABSの作動条件と制御特性を設定する
    [Header("ABS")]
    [SerializeField] bool m_absEnabled = true;
    [SerializeField, Range(0.05f, 0.3f)] float m_absLockSlip = 0.14f;
    [SerializeField, Range(0.01f, 0.2f)] float m_absReleaseSlip = 0.07f;
    [SerializeField, Range(1f, 15f)] float m_absMinimumSpeedKph = 5f;
    [SerializeField, Range(1f, 20f)] float m_absApplyRate = 8f;
    [SerializeField, Range(0.5f, 1.5f)] float m_brakeGripCoefficient = 1.10f;

    //ABSの作動状態と制御圧力を保持する配列。WheelColliderの順番に対応する
    readonly float[] m_absPressure = { 1f, 1f, 1f, 1f };
    readonly bool[] m_absActive = new bool[4];
    readonly float[] m_tractionTorqueRatio = { 1f, 1f, 1f, 1f };
    readonly float[] m_forwardSlip = new float[4];
    readonly float[] m_sidewaysSlip = new float[4];
    readonly Quaternion[] m_visualRotationOffsets = new Quaternion[4];
    readonly MeshFilter[][] m_visualMeshFilters = new MeshFilter[4][];

    //車両のRigidbodyを保持する。ESCの駆動抑制と選択制動に使用する
    Rigidbody m_vehicleRigidbody;
    float m_stabilityTorqueFactor = 1f;
    int m_stabilityBrakeWheel = -1;
    float m_stabilityBrakeTorque;
    float m_shaftAngularVelocity;
    bool m_anyWheelSpinning;
    int m_groundedWheelCount;
    float m_ungroundedTime;
    bool m_isAirborne;
    bool m_initialVisualPoseReady;

    //プロパティでWheelColliderの数、シャフト回転数、ABS作動状態を取得する
    public int WheelCount => m_Wheels != null ? m_Wheels.Count : 0;
    public float ShaftAngularVelocity => m_shaftAngularVelocity;
    public bool AnyWheelSpinning => m_anyWheelSpinning;
    public int GroundedWheelCount => m_groundedWheelCount;
    public bool IsAirborne => m_isAirborne;
    public float AirborneTime => m_ungroundedTime;
    public bool AnyABSActive
    {
        get
        {
            //いずれかの車輪でABSが作動しているかを順番に調べる
            for (int i = 0; i < m_absActive.Length; i++)
            {
                //作動中の車輪を見つけた時点でABS作動中として返す
                if (m_absActive[i]) { return true; }
            }

            return false;
        }
    }

    //初期化時にRigidbodyを取得し、WheelColliderの設定を適用する
    void Awake()
    {
        m_vehicleRigidbody = GetComponentInParent<Rigidbody>();
        ResolveWheelColliders();
        ValidateLists();
        ApplyWheelColliderSettings();
        ResolveRenderedWheelTransforms();
    }

    //VehicleControllerが自動取得したデフ、ブレーキ、ステアリングを設定する関数
    public void ConfigureVehicleParts(Differential differential, Brake brake, Steering steering)
    {
        m_differential = differential;
        m_brake = brake;
        m_steering = steering;
        if (m_vehicleRigidbody == null) { m_vehicleRigidbody = GetComponentInParent<Rigidbody>(); }
        ResolveWheelColliders();
        ValidateLists();
        ApplyWheelColliderSettings();
        ResolveRenderedWheelTransforms();
    }

    //未設定の場合だけ車両階層の4輪を前右、前左、後右、後左の順へ自動登録する関数
    void ResolveWheelColliders()
    {
        if (m_Wheels != null && m_Wheels.Count == 4 && m_Wheels.TrueForAll(wheel => wheel != null)) { return; }

        WheelCollider[] wheels = GetComponentsInChildren<WheelCollider>(true);
        if (wheels.Length != 4 && m_vehicleRigidbody != null) { wheels = m_vehicleRigidbody.GetComponentsInChildren<WheelCollider>(true); }
        if (wheels.Length != 4) { return; }

        Transform reference = m_vehicleRigidbody != null ? m_vehicleRigidbody.transform : transform;
        System.Array.Sort(wheels, (left, right) => CompareWheelPosition(reference, left.transform, right.transform));
        m_Wheels = new List<WheelCollider>(wheels);
    }

    //車輪を前後位置の降順、同じ車軸では左右位置の降順に並べる関数
    int CompareWheelPosition(Transform reference, Transform left, Transform right)
    {
        Vector3 leftPosition = reference.InverseTransformPoint(left.position);
        Vector3 rightPosition = reference.InverseTransformPoint(right.position);
        if (!Mathf.Approximately(leftPosition.z, rightPosition.z)) { return rightPosition.z.CompareTo(leftPosition.z); }
        return rightPosition.x.CompareTo(leftPosition.x);
    }

    //インスペクターでWheelColliderのリストや設定値を変更したときに、WheelColliderの設定を再適用する
    void OnValidate()
    {
        ValidateLists();
        if (!Application.isPlaying) { ApplyWheelColliderSettings(); }
    }

    //VehicleControllerからESCの駆動抑制と選択制動を受け取る。
    public void SetStabilityControl(float torqueFactor, int brakeWheelIndex, float brakeTorque)
    {
        m_stabilityTorqueFactor = Mathf.Clamp01(torqueFactor);
        m_stabilityBrakeWheel = brakeWheelIndex;
        m_stabilityBrakeTorque = Mathf.Max(0f, brakeTorque);
    }

    //駆動、制動、操舵を4輪へ反映し、シャフト回転数を更新する。
    public void WheelUpdate()
    {
        //WheelColliderのリストやコンポーネントが揃っていない場合は処理を中断する
        if (!Ready()) { return; }

        //車両の速度をkm/hに換算する
        float vehicleSpeedKph = m_vehicleRigidbody != null ? m_vehicleRigidbody.linearVelocity.magnitude * 3.6f : 0f;
        m_shaftAngularVelocity = 0f;
        m_anyWheelSpinning = false;
        m_groundedWheelCount = 0;
        float currentShaftVelocity = 0f;
        int drivenWheelCount = 0;

        //各WheelColliderに駆動トルク、制動トルク、操舵角を設定する
        for (int i = 0; i < m_Wheels.Count; i++)
        {
            WheelCollider wheel = m_Wheels[i];
            bool grounded = wheel.GetGroundHit(out WheelHit hit);
            if (grounded) { m_groundedWheelCount++; }
            m_forwardSlip[i] = grounded ? hit.forwardSlip : 0f;
            m_sidewaysSlip[i] = grounded ? hit.sidewaysSlip : 0f;
            if (grounded) { ApplyBumpStop(wheel, hit); }

            //駆動輪の場合は、差動装置から駆動トルクを取得する。非駆動輪は0にする。
            float driveTorque = m_IsDrive[i] ? m_differential.GetDriveTorque(m_IsFront[i], m_IsRight[i]) : 0f;
            driveTorque *= m_stabilityTorqueFactor;

            //空転が大きい時だけ穏やかに駆動力を落とし、4WDらしい発進グリップを維持する。
            if (grounded && m_IsDrive[i] && Mathf.Abs(hit.forwardSlip) > m_tractionSlipLimit)
            {
                float slipAmount = Mathf.InverseLerp(m_tractionSlipLimit, 0.8f, Mathf.Abs(hit.forwardSlip));
                float targetRatio = Mathf.Lerp(1f, m_minimumTractionTorqueRatio, slipAmount);
                m_tractionTorqueRatio[i] = Mathf.Min(m_tractionTorqueRatio[i], targetRatio);
                m_anyWheelSpinning = true;
            }
            else 
            { 
                m_tractionTorqueRatio[i] = Mathf.MoveTowards(m_tractionTorqueRatio[i], 1f, m_tractionRecoverySpeed * Time.fixedDeltaTime); 
            }
            driveTorque *= grounded ? m_tractionTorqueRatio[i] : 0.12f;

            //制動トルクはBrakeコンポーネントから取得する。ESCの選択制動がある場合は加算する。
            float requestedBrake = m_brake.GetBrakeTorque(m_IsFront[i]);
            if (i == m_stabilityBrakeWheel) { requestedBrake += m_stabilityBrakeTorque; }
       
            //駆動トルクの増加だけを滑らかにし、空転時の低下は即時に反映する
            bool reducingTorque = Mathf.Abs(driveTorque) < Mathf.Abs(wheel.motorTorque);
            wheel.motorTorque = reducingTorque
                ? driveTorque
                : Mathf.MoveTowards(wheel.motorTorque, driveTorque, m_motorTorqueApplyRate * Time.fixedDeltaTime);
            wheel.brakeTorque = CalculateABSBrakeTorque(i, wheel, grounded, hit, requestedBrake, vehicleSpeedKph);
            wheel.steerAngle = m_IsFront[i] ? m_steering.CalcSteerAngle(m_IsRight[i]) : 0f;

            //駆動輪の場合は、差動装置にシャフト回転数を渡す
            if (m_IsDrive[i])
            {
                float angularVelocity = wheel.rpm * Mathf.PI * 2f / 60f;
                float inertia = 0.5f * wheel.mass * wheel.radius * wheel.radius;
                currentShaftVelocity = m_differential.GetShaftVelocity(angularVelocity, inertia, m_IsFront[i], m_IsRight[i]);
                drivenWheelCount++;
            }
        }

        //前後の左右輪を連携させ、片輪だけが大きく沈んだ時の荷重差を車体へ戻す
        ApplyAntiRollBar(0, 1, m_frontAntiRollStiffness);
        ApplyAntiRollBar(2, 3, m_rearAntiRollStiffness);

        //4輪の平均回転数をクラッチへ返し、一輪の停止で駆動系全体が止まることを防ぐ
        if (drivenWheelCount > 0) { m_shaftAngularVelocity = currentShaftVelocity; }

        //4輪すべてが離れた状態が一定時間続いた場合だけ空中と判定する
        m_ungroundedTime = m_groundedWheelCount == 0 ? m_ungroundedTime + Time.fixedDeltaTime : 0f;
        m_isAirborne = m_ungroundedTime >= m_airborneDetectionDelay;
    }

    //同じ車軸の左右サスペンション圧縮差からアンチロール力を加える関数
    void ApplyAntiRollBar(int rightIndex, int leftIndex, float stiffness)
    {
        if (m_vehicleRigidbody == null || stiffness <= 0f) { return; }
        if (rightIndex >= m_Wheels.Count || leftIndex >= m_Wheels.Count) { return; }

        WheelCollider rightWheel = m_Wheels[rightIndex];
        WheelCollider leftWheel = m_Wheels[leftIndex];
        if (rightWheel == null || leftWheel == null) { return; }

        bool rightGrounded = rightWheel.GetGroundHit(out WheelHit rightHit);
        bool leftGrounded = leftWheel.GetGroundHit(out WheelHit leftHit);
        float rightTravel = GetSuspensionTravel(rightWheel, rightGrounded, rightHit);
        float leftTravel = GetSuspensionTravel(leftWheel, leftGrounded, leftHit);
        float antiRollForce = (rightTravel - leftTravel) * stiffness;

        //縮んだ側を持ち上げ、伸びた側を押し下げて左右の圧縮差を減らす
        if (rightGrounded) { m_vehicleRigidbody.AddForceAtPosition(rightWheel.transform.up * antiRollForce, rightWheel.transform.position); }
        if (leftGrounded) { m_vehicleRigidbody.AddForceAtPosition(leftWheel.transform.up * -antiRollForce, leftWheel.transform.position); }
    }

    //接地点からサスペンションの伸縮位置を0から1の範囲で求める関数
    static float GetSuspensionTravel(WheelCollider wheel, bool grounded, WheelHit hit)
    {
        if (!grounded) { return 0f; }
        return GetSuspensionCompression(wheel);
    }

    //サスペンション圧縮が限界に近い時だけ段階的な補助力を車体へ加える関数
    void ApplyBumpStop(WheelCollider wheel, WheelHit hit)
    {
        if (m_vehicleRigidbody == null || m_bumpStopMaximumForce <= 0f) { return; }
        float compression = GetSuspensionCompression(wheel);
        if (compression <= m_bumpStopStartCompression) { return; }
        float compressionRatio = Mathf.InverseLerp(m_bumpStopStartCompression, 1f, compression);
        float bumpStopForce = compressionRatio * compressionRatio * m_bumpStopMaximumForce;
        m_vehicleRigidbody.AddForceAtPosition(hit.normal * bumpStopForce, wheel.transform.position);
    }

    //ABSの作動状態を判定し、制動トルクを調整する
    float CalculateABSBrakeTorque(int index, WheelCollider wheel, bool grounded, WheelHit hit, float requestedBrake, float speedKph)
    {
        //制動トルクが0以下の場合はABSを作動させず、制動圧力を1にする
        requestedBrake = Mathf.Max(0f, requestedBrake);
        if (requestedBrake <= 0.01f)
        {
            m_absPressure[index] = 1f;
            m_absActive[index] = false;
            return 0f;
        }

        //接地していない場合はABSを作動させず、制動圧力を1にする
        float sprungLoad = Mathf.Max(1f, wheel.sprungMass) * Physics.gravity.magnitude;
        float gripLimitedBrake = sprungLoad * wheel.radius * m_brakeGripCoefficient;
        float limitedBrake = Mathf.Min(requestedBrake, gripLimitedBrake);

        //ABSが無効、接地していない、または速度がABSの作動条件を満たさない場合は、制動圧力を1にしてABSを作動させない
        if (!m_absEnabled || !grounded || speedKph < m_absMinimumSpeedKph)
        {
            m_absPressure[index] = 1f;
            m_absActive[index] = false;
            return limitedBrake;
        }

        //UnityのforwardSlipは制動時だけ正になるため、加速側の負スリップをABS判定から除外する
        float brakingSlip = Mathf.Max(0f, hit.forwardSlip);
        if (brakingSlip >= m_absLockSlip)
        {
            m_absPressure[index] = Mathf.MoveTowards(m_absPressure[index], 0f, 20f * Time.fixedDeltaTime);
        }
        else if (brakingSlip <= m_absReleaseSlip)
        {
            m_absPressure[index] = Mathf.MoveTowards(m_absPressure[index], 1f, m_absApplyRate * Time.fixedDeltaTime);
        }
        
        //ABSの作動状態を更新する
        m_absActive[index] = m_absPressure[index] < 0.999f || requestedBrake > limitedBrake;
        return limitedBrake * m_absPressure[index];
    }

    //WheelColliderの位置と回転を、表示用タイヤのTransformに反映する
    void LateUpdate()
    {
        //最初の接地計算が終わるまではシーン保存位置を維持し、タイヤが一度沈んでから跳ね上がる表示を防ぐ
        if (!m_initialVisualPoseReady && m_groundedWheelCount == 0) { return; }
        m_initialVisualPoseReady = true;

        //GetWorldPoseを使わないとWheelColliderだけが回転し、画面上のタイヤが静止したままになる
        int count = Mathf.Min(m_Wheels.Count, m_WheelVisuals.Count);

        //WheelColliderの位置と回転を、表示用タイヤのTransformに反映する
        for (int i = 0; i < count; i++)
        {
            //WheelColliderまたは表示用タイヤがnullの場合はスキップする
            if (m_Wheels[i] == null || m_WheelVisuals[i] == null) { continue; }

            //WheelColliderからサスペンション、操舵、回転を含む車輪姿勢を取得する
            WheelCollider wheel = m_Wheels[i];
            wheel.GetWorldPose(out Vector3 position, out Quaternion wheelRotation);
            Quaternion rotation = wheelRotation * m_visualRotationOffsets[i];
            //物理値が不正なフレームでタイヤと追従カメラへNaN座標を伝えないための処理
            if (!IsFinite(position) || !IsFinite(rotation)) { continue; }
            m_WheelVisuals[i].SetPositionAndRotation(position, rotation);
        }
    }

    //座標の各成分がNaNまたは無限値ではないことを確認する関数
    static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    //回転の各成分がNaNまたは無限値ではないことを確認する関数
    static bool IsFinite(Quaternion value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
    }

    //単精度値がNaNまたは無限値ではないことを確認する関数
    static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    //WheelColliderが接地しているかどうかを返す
    public bool IsGrounded(int index)
    {
        return index >= 0 && index < m_Wheels.Count && m_Wheels[index] != null && m_Wheels[index].isGrounded;
    }

    //WheelColliderの回転数をrpmで返す
    public float GetWheelRPM(int index)
    {
        return index >= 0 && index < m_Wheels.Count && m_Wheels[index] != null ? m_Wheels[index].rpm : 0f;
    }

    //指定したタイヤの前後スリップ量を返す
    public float GetForwardSlip(int index)
    {
        return index >= 0 && index < m_forwardSlip.Length ? m_forwardSlip[index] : 0f;
    }

    //指定したタイヤの横スリップ量を返す
    public float GetSidewaysSlip(int index)
    {
        return index >= 0 && index < m_sidewaysSlip.Length ? m_sidewaysSlip[index] : 0f;
    }

    //指定した車輪の表示用タイヤが設定されているか返す関数
    public bool HasWheelVisual(int index)
    {
        return index >= 0 && index < m_WheelVisuals.Count && m_WheelVisuals[index] != null;
    }

    //表示用タイヤとWheelColliderのサスペンション位置の誤差を返す関数
    public float GetVisualPoseError(int index)
    {
        if (index < 0 || index >= m_Wheels.Count || index >= m_WheelVisuals.Count) { return float.PositiveInfinity; }
        if (m_Wheels[index] == null || m_WheelVisuals[index] == null) { return float.PositiveInfinity; }
        m_Wheels[index].GetWorldPose(out Vector3 position, out Quaternion unusedRotation);
        return Vector3.Distance(position, m_WheelVisuals[index].position);
    }

    //WheelColliderのサスペンション圧縮率の最小値を返す。0=伸びきり、1=縮みきり。
    public float MinimumSuspensionCompression
    {
        get
        {
            //WheelColliderのリストがnullまたは空の場合は0を返す
            if (m_Wheels == null || m_Wheels.Count == 0) { return 0f; }

            //接地しているWheelColliderのサスペンション圧縮率の最小値を求める
            float minimum = 1f;
            for (int i = 0; i < m_Wheels.Count; i++)
            {
                minimum = Mathf.Min(minimum, GetSuspensionCompression01(i));
            }
            return minimum;
        }
    }

    //表示用タイヤのTransformの回転を取得する。WheelColliderが接地していない場合はQuaternion.identityを返す
    public Quaternion[] GetVisualRotations()
    {
        //表示用タイヤのTransformのリストがnullの場合は空の配列を返す
        if (m_WheelVisuals == null) { return new Quaternion[0]; }

        //表示用タイヤのTransformの回転を取得する
        Quaternion[] result = new Quaternion[m_WheelVisuals.Count];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = m_WheelVisuals[i] != null ? m_WheelVisuals[i].rotation : Quaternion.identity;
        }
        return result;
    }

    //表示用タイヤのTransformの回転が変化したかどうかを判定する
    public bool HaveVisualRotationsChanged(Quaternion[] initial, float minimumAngle)
    {
        //表示用タイヤのTransformのリストがnullまたは初期値の配列と長さが異なる場合はfalseを返す
        if (initial == null || m_WheelVisuals == null || initial.Length != m_WheelVisuals.Count) { return false; }

        //表示用タイヤのTransformの回転が初期値からminimumAngle以上変化しているかどうかを判定する
        for (int i = 0; i < initial.Length; i++)
        {
            if (m_WheelVisuals[i] != null && Quaternion.Angle(initial[i], m_WheelVisuals[i].rotation) >= minimumAngle) { return true; }
        }
        return false;
    }

    //WheelColliderのサスペンション圧縮率を0～1で返す。0=伸びきり、1=縮みきり。
    public float GetSuspensionCompression01(int index)
    {
        //WheelColliderが存在しない場合は0を返す
        if (index < 0 || index >= m_Wheels.Count || m_Wheels[index] == null) { return 0f; }

        WheelCollider wheel = m_Wheels[index];
        if (!wheel.isGrounded) { return 0f; }
        return GetSuspensionCompression(wheel);
    }

    //サスペンション上端から現在の車輪中心までの伸び量を圧縮率へ変換する関数
    static float GetSuspensionCompression(WheelCollider wheel)
    {
        wheel.GetWorldPose(out Vector3 position, out Quaternion unusedRotation);
        Vector3 suspensionTop = wheel.transform.TransformPoint(wheel.center);
        float extensionDistance = Vector3.Dot(suspensionTop - position, wheel.transform.up);
        float extensionRatio = extensionDistance / Mathf.Max(0.01f, wheel.suspensionDistance);
        return 1f - Mathf.Clamp01(extensionRatio);
    }

    //表示タイヤのメッシュ外周が接触路面より下へ入らないように表示位置を補正する関数
    void CorrectVisualGroundClearance(int index, WheelCollider wheel, Transform visual)
    {
        if (!wheel.GetGroundHit(out WheelHit hit)) { return; }
        float currentClearance = CalculateVisualBoundsClearance(index, hit);
        if (!float.IsFinite(currentClearance) || currentClearance >= m_minimumGroundClearance) { return; }
        visual.position += hit.normal * (m_minimumGroundClearance - currentClearance);
    }

    //表示タイヤ内の全メッシュ境界から路面に最も近い距離を求める関数
    float CalculateVisualBoundsClearance(int index, WheelHit hit)
    {
        if (index < 0 || index >= m_visualMeshFilters.Length || m_visualMeshFilters[index] == null) { return float.PositiveInfinity; }
        float minimumClearance = float.PositiveInfinity;
        MeshFilter[] filters = m_visualMeshFilters[index];
        for (int i = 0; i < filters.Length; i++)
        {
            MeshFilter filter = filters[i];
            if (filter == null || filter.sharedMesh == null) { continue; }
            Bounds bounds = filter.sharedMesh.bounds;
            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                        Vector3 worldCorner = filter.transform.TransformPoint(corner);
                        minimumClearance = Mathf.Min(minimumClearance, Vector3.Dot(worldCorner - hit.point, hit.normal));
                    }
                }
            }
        }
        return minimumClearance;
    }

    //開始時やコース復帰時に4輪の最大食い込み量だけ車体を路面上へ戻す関数
    public float CorrectGroundPenetrationImmediately()
    {
        if (m_vehicleRigidbody == null) { m_vehicleRigidbody = GetComponentInParent<Rigidbody>(); }
        if (m_vehicleRigidbody == null || m_Wheels == null) { return 0f; }
        Physics.SyncTransforms();
        float requiredCorrection = 0f;
        Vector3 correctionNormal = transform.up;
        for (int i = 0; i < m_Wheels.Count; i++)
        {
            WheelCollider wheel = m_Wheels[i];
            if (wheel == null || !TryFindGroundBelowWheel(wheel, out RaycastHit hit)) { continue; }
            wheel.GetWorldPose(out Vector3 position, out Quaternion unusedRotation);
            float requiredDistance = wheel.radius + m_visualRadiusPadding + m_minimumGroundClearance;
            float penetration = requiredDistance - Vector3.Dot(position - hit.point, hit.normal);
            if (penetration <= requiredCorrection) { continue; }
            requiredCorrection = penetration;
            correctionNormal = hit.normal;
        }
        float correctionDistance = Mathf.Clamp(requiredCorrection, 0f, m_maximumGroundCorrection);
        if (correctionDistance <= 0f) { return 0f; }
        m_vehicleRigidbody.position += correctionNormal * correctionDistance;
        float inwardSpeed = Vector3.Dot(m_vehicleRigidbody.linearVelocity, correctionNormal);
        if (inwardSpeed < 0f) { m_vehicleRigidbody.linearVelocity -= correctionNormal * inwardSpeed; }
        Physics.SyncTransforms();
        return correctionDistance;
    }

    //WheelColliderの接地情報が未生成の開始フレームでも外部路面を検出する関数
    bool TryFindGroundBelowWheel(WheelCollider wheel, out RaycastHit groundHit)
    {
        groundHit = default;
        wheel.GetWorldPose(out Vector3 position, out Quaternion unusedRotation);
        Vector3 wheelUp = wheel.transform.up;
        Vector3 origin = position + wheelUp * m_maximumGroundCorrection;
        float searchDistance = wheel.radius + wheel.suspensionDistance + m_maximumGroundCorrection * 2f;
        RaycastHit[] hits = Physics.RaycastAll(origin, -wheelUp, searchDistance, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        float nearestDistance = float.PositiveInfinity;
        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || hit.collider.transform.IsChildOf(m_vehicleRigidbody.transform)) { continue; }
            if (Vector3.Dot(hit.normal, wheelUp) < 0.35f || hit.distance >= nearestDistance) { continue; }
            nearestDistance = hit.distance;
            groundHit = hit;
        }
        return nearestDistance < float.PositiveInfinity;
    }

    //停止後の再発進前に制動と残留トルクを消去する。
    public void PrepareDriveAway(float speedInGearDirection)
    {
        //駆動トルクと制動トルクをリセットする
        ResetDynamics();
        if (m_vehicleRigidbody == null || Mathf.Abs(speedInGearDirection) <= 0.01f) { return; }
        float currentForward = Vector3.Dot(m_vehicleRigidbody.linearVelocity, transform.forward);
        m_vehicleRigidbody.linearVelocity += transform.forward * (speedInGearDirection - currentForward);
    }

    public void ResetDynamics()
    {
        //WheelColliderの駆動トルクと制動トルクをリセットする
        for (int i = 0; i < m_Wheels.Count; i++)
        {
            //WheelColliderがnullの場合はスキップする
            if (m_Wheels[i] == null) { continue; }

            //駆動トルクと制動トルクをリセットする
            m_Wheels[i].motorTorque = 0f;
            m_Wheels[i].brakeTorque = 0f;
            m_absPressure[Mathf.Min(i, m_absPressure.Length - 1)] = 1f;
            m_absActive[Mathf.Min(i, m_absActive.Length - 1)] = false;
        }

        //駆動抑制と選択制動をリセットする
        m_shaftAngularVelocity = 0f;
        m_stabilityTorqueFactor = 1f;
        m_stabilityBrakeWheel = -1;
        m_stabilityBrakeTorque = 0f;
        m_groundedWheelCount = 0;
        m_ungroundedTime = 0f;
        m_isAirborne = false;
        for (int i = 0; i < m_tractionTorqueRatio.Length; i++) { m_tractionTorqueRatio[i] = 1f; }
    }

    void ApplyWheelColliderSettings()
    {
        //WheelColliderの設定を行う前に、リストがnullでないことを確認する
        if (m_Wheels == null) { return; }

        //WheelColliderの設定を一括で行う
        foreach (WheelCollider wheel in m_Wheels)
        {
            //WheelColliderがアタッチされていない場合はスキップする。
            if (wheel == null) { continue; }

            //WheelColliderの基本設定
            wheel.radius = m_wheelRadius;
            wheel.suspensionDistance = m_suspensionDistance;
            wheel.forceAppPointDistance = m_forceApplicationDistance;
            wheel.mass = 20f;
            wheel.wheelDampingRate = 0.35f;

            //サスペンション設定
            JointSpring suspension = wheel.suspensionSpring;
            suspension.spring = m_spring;
            suspension.damper = m_damper;
            suspension.targetPosition = m_suspensionTarget;
            wheel.suspensionSpring = suspension;

            //前方向の摩擦設定
            WheelFrictionCurve forward = wheel.forwardFriction;
            forward.extremumSlip = 0.18f;
            forward.extremumValue = 1.15f;
            forward.asymptoteSlip = 0.55f;
            forward.asymptoteValue = 0.90f;
            forward.stiffness = m_forwardStiffness;
            wheel.forwardFriction = forward;

            //横方向の摩擦設定
            WheelFrictionCurve sideways = wheel.sidewaysFriction;
            sideways.extremumSlip = 0.14f;
            sideways.extremumValue = 1.15f;
            sideways.asymptoteSlip = 0.40f;
            sideways.asymptoteValue = 0.92f;
            sideways.stiffness = m_sidewaysStiffness;
            wheel.sidewaysFriction = sideways;
        }
    }

    //4輪の位置に対応する表示用タイヤを車両階層から取得する関数
    void ResolveRenderedWheelTransforms()
    {
        int count = Mathf.Min(m_Wheels.Count, m_WheelVisuals.Count);
        for (int i = 0; i < count; i++)
        {
            WheelCollider wheel = m_Wheels[i];
            if (wheel == null) { continue; }
            if (m_WheelVisuals[i] == null) { m_WheelVisuals[i] = FindWheelVisual(i); }
            Transform visual = m_WheelVisuals[i];
            wheel.GetWorldPose(out Vector3 unusedPosition, out Quaternion wheelRotation);
            m_visualRotationOffsets[i] = visual != null ? Quaternion.Inverse(wheelRotation) * visual.rotation : Quaternion.identity;
            m_visualMeshFilters[i] = visual != null ? visual.GetComponentsInChildren<MeshFilter>(true) : new MeshFilter[0];
        }
    }

    //車輪番号に対応するGRヤリスの表示用タイヤを名前から取得する関数
    Transform FindWheelVisual(int index)
    {
        string wheelName = index == 0 ? "FR" : index == 1 ? "FL" : index == 2 ? "Tire_Mirror_RR" : "RL";
        Transform searchRoot = m_vehicleRigidbody != null ? m_vehicleRigidbody.transform : transform.root;
        Transform[] candidates = searchRoot.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < candidates.Length; i++)
        {
            if (candidates[i].name.Equals(wheelName, System.StringComparison.OrdinalIgnoreCase)) { return candidates[i]; }
        }

        Debug.LogError($"WheelController2026: 表示用タイヤ {wheelName} が見つかりません。", this);
        return null;
    }

    //WheelColliderのリストと駆動・前輪・右輪のブールリストの長さを揃える
    void ValidateLists()
    {
        //WheelColliderのリストがnullの場合は空のリストを作成する
        int count = m_Wheels != null ? m_Wheels.Count : 0;
        //自動登録前に前後左右の設定がインスペクターで完了していたかを保持する変数
        bool hasFrontConfiguration = m_IsFront != null && m_IsFront.Count == count;
        bool hasRightConfiguration = m_IsRight != null && m_IsRight.Count == count;

        //駆動・前輪・右輪のブールリストの長さをWheelColliderのリストの長さに揃える
        EnsureBoolCount(m_IsDrive, count, true);
        EnsureBoolCount(m_IsFront, count, false);
        EnsureBoolCount(m_IsRight, count, false);
        //4輪を自動登録した場合は前右、前左、後右、後左の順から前後左右を設定する
        if (!hasFrontConfiguration)
        {
            for (int index = 0; index < count; index++) { m_IsFront[index] = index < 2; }
        }
        if (!hasRightConfiguration)
        {
            for (int index = 0; index < count; index++) { m_IsRight[index] = index == 0 || index == 2; }
        }
        if (m_WheelVisuals == null) { m_WheelVisuals = new List<Transform>(); }
        while (m_WheelVisuals.Count < count) { m_WheelVisuals.Add(null); }
        while (m_WheelVisuals.Count > count) { m_WheelVisuals.RemoveAt(m_WheelVisuals.Count - 1); }
    }

    //ブールリストの長さを指定した数に揃える
    static void EnsureBoolCount(List<bool> list, int count, bool defaultValue)
    {
        if (list == null) { return; }
        while (list.Count < count) { list.Add(defaultValue); }
        while (list.Count > count) { list.RemoveAt(list.Count - 1); }
    }

    //WheelColliderのリストとコンポーネントが揃っているかどうかを返す
    bool Ready()
    {
        return m_Wheels != null && m_Wheels.Count > 0 && m_differential != null && m_brake != null && m_steering != null;
    }
}
