// 空力を計算してAddForceする
using UnityEngine;

public class AearoDynamics : MonoBehaviour
{
    [SerializeField]
    Rigidbody m_vehicleRigidbody;
    [SerializeField]
    float m_rho = 1.293f; // 空気密度ρ(基準状態:1.293[kg/m^3])
    [Header("AirDrag")]
    [SerializeField, Range(0.04f, 1f)]
    float m_dragCoeff; // 空気抵抗係数(物体の形状によって決まる)
    [SerializeField]
    float m_frontArea; // 車体の前方投影面積(m^2)
    [Header("DownForce")]
    [SerializeField]
    float m_downForceCoeff; // Cl*A (揚力係数*ウィング面積)の値
    // 詳細な数値を確かめるのが難しいので
    // F1だとCl*Aは5.5くらいなのでそれを基準に設定
    // AddComponent/Reset したときの設定
    void Reset()
    {
        m_vehicleRigidbody = GetComponentInParent<Rigidbody>();
    }

    void Start()
    {
        if (m_vehicleRigidbody == null)
            AppLog.LogError("RigidBodyが設定されていません[AeroDynamics]");
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        // 参照が未設定の車両で空力計算を続けて例外を繰り返さない
        if (m_vehicleRigidbody == null)
        {
            return;
        }

        // 車体前後方向の速度に符号を残して後退でも抵抗の向きを求める
        float forwardSpeed = Vector3.Dot(m_vehicleRigidbody.linearVelocity, m_vehicleRigidbody.transform.forward);
        // 前進と後退のどちらでも空気抵抗が走行を加速させない方向へ働くようにする
        Vector3 dragForceDir = -Mathf.Sign(forwardSpeed) * m_vehicleRigidbody.transform.forward;
        float dragForce = 0.5f * m_dragCoeff * m_frontArea * m_rho * forwardSpeed * forwardSpeed;
        Vector3 downForceDir = -m_vehicleRigidbody.transform.up;
        float downForce = 0.5f * m_downForceCoeff * m_rho * forwardSpeed * forwardSpeed;
        m_vehicleRigidbody.AddForce(dragForceDir * dragForce + downForceDir * downForce);
    }
}
