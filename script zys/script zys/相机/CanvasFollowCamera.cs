using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CanvasFollowCamera : MonoBehaviour
{
    [Header("跟随设置")]
    public Camera targetCamera;
    public float distance = 2f;//画布距离镜头前方多远

    private void LateUpdate()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
            if (targetCamera == null) return;
        }

        Transform cam = targetCamera.transform;
        //找到摄像头的信息
        transform.position = cam.position+cam.forward*distance;
        //画布位于摄像头正前方
        transform.rotation = cam.rotation;

        //旋转方向一致
    }


}
