// スピンした時に道に戻す処理
using System;
using System.Collections;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Splines;

public class ReturnRoad : MonoBehaviour
{
    // 進行方向のスプライン
    [SerializeField]
    private SplineContainer m_spline = null;
    // 車両
    [SerializeField]
    private GameObject m_vehicle = null;
    // 逆走を知らせるUI
    [SerializeField]
    private GameObject m_reverseDrivingUI = null;
    // 逆走を検知する角度(進行方向から150°以上ずれていたら検知する)
    [SerializeField]
    private float m_reverseDetectionAngle = 150.0f;
    [SerializeField]
    private bool m_flip;
    [SerializeField, Tooltip("自動リセットの有効設定")]
    private bool m_autoReturn = true; // 自動でリセットを行うか否か。
    // 復帰用キー
    [SerializeField]
    KeyCode _returnKey = KeyCode.Space;
    // 解像度
    // 内部的にPickResolutionMin～PickResolutionMaxの範囲に丸められる
    [SerializeField]
    [Range(SplineUtility.PickResolutionMin, SplineUtility.PickResolutionMax)]
    private int m_resolution = 4;
    // 計算回数
    // 内部的に10回以下に丸められる
    [SerializeField]
    [Range(1, 10)]
    private int m_iterations = 2;
    private float m_prePos = 0.0f;
    private float m_nowPos = 0.0f;
    [SerializeField, Tooltip("移動したとみなすスプライン上での比率")]
    public float m_MovingDistance = 0.000001f;
    [SerializeField, Tooltip("自動でリセットされるまでの時間")]
    public float m_returnTime = 5.0f;
    [SerializeField, Tooltip("復帰カウントを開始するまでの待機時間")]
    public float m_CountStartTime = 2.0f;
    private float m_returnTimer = 0.0f;
    [SerializeField]
    private TextMeshProUGUI m_countUI = null; // テキストを画面上に表示する
    private bool m_afterReturn = false; // 戻った後か
    private void FixedUpdate()
    {
        if (m_spline == null || m_vehicle == null)
            return;
        // スプラインにおける直近位置を求める
        float _distance = SplineUtility.GetNearestPoint(m_spline.Spline, m_vehicle.transform.position, out var nearestPoint, out float t, m_resolution, m_iterations);
        m_prePos = m_nowPos;
        m_nowPos = t; // tはスプラインにおける比率(0~1)
        // 車とスプラインにおける直近位置の角度の差を求める
        float signedAngle;
        if (!m_flip)
            signedAngle = Vector3.SignedAngle(m_vehicle.transform.forward, m_spline.Spline.EvaluateTangent(t), Vector3.up);
        else
            signedAngle = Vector3.SignedAngle(-m_vehicle.transform.forward, m_spline.Spline.EvaluateTangent(t), Vector3.up);
        // 道に沿っての前進が無いならリセットタイマーを加算
        // そもそもの自動リセット有効な時のみ加算
        if (m_autoReturn)
        {
            if (m_prePos - m_nowPos <= m_MovingDistance || signedAngle <= -m_reverseDetectionAngle || signedAngle >= m_reverseDetectionAngle)
            {
                m_returnTimer += Time.deltaTime;
            }
            // 少しでも進むならリセット無し
            // バック走は許す
            else if (!(signedAngle <= -m_reverseDetectionAngle || signedAngle >= m_reverseDetectionAngle))
            {
                m_returnTimer = 0.0f;
            }
        }
        // 自動復帰が無効の時は少し動いてから有効化
        else if (!m_afterReturn)
        {
            if (m_prePos - m_nowPos > m_MovingDistance)
            {
                m_autoReturn = true;
            }
        }

        // 復帰までの秒数表示
        if (m_returnTimer >= m_CountStartTime)
        {
            m_countUI.enabled = true;
            m_countUI.text = ((int)(m_returnTime - m_returnTimer) + 1).ToString();
        }
        else
        {
            m_countUI.enabled = false;
        }

        // 逆走しているかを判定し、UIを表示する
        if (signedAngle <= -m_reverseDetectionAngle || signedAngle >= m_reverseDetectionAngle)
        {
            m_reverseDrivingUI.SetActive(true);
        }
        else
        {
            m_reverseDrivingUI.SetActive(false);
        }

        // 手動か条件が揃ったらリセット
        if (Input.GetKeyDown(_returnKey) || m_returnTimer >= m_returnTime)
        {
            // 車両の位置変更
            if (!m_flip)
                m_vehicle.transform.rotation = Quaternion.LookRotation(m_spline.Spline.EvaluateTangent(t));
            else
                m_vehicle.transform.rotation = Quaternion.LookRotation(-m_spline.Spline.EvaluateTangent(t));
            m_vehicle.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
            nearestPoint.y += 1.0f;
            m_vehicle.transform.position = nearestPoint;
            m_returnTimer = 0.0f;
            m_afterReturn = true;
            m_autoReturn = false;
            // 復帰直後はしばらくその状態であることを示しとく
            StartCoroutine(DelayProcess(2.0f, () =>
            {
                m_afterReturn = false;
            }));
        }
    }

    // ディレイプロセス
    public IEnumerator DelayProcess(float delaySecond, UnityAction callback)
    {
        yield return new WaitForSeconds(delaySecond);
        callback?.Invoke();
    }
}
