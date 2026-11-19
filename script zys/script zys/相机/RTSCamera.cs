using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class RTSCamera : MonoBehaviour
{
    [Header("平移")]
    public float panSpeed = 20f;
    public float edgeSize = 20f;

    [Header("边界(世界坐标)")]
    public float mapWidth = 80f;//X方向
        public float mapDepth = 60f;//z方向
    public bool showBoundsGizmo = true;
    //打勾就是画蓝框



    void Update()
    {
        Vector3 input = Vector3.zero;
        //一个方向判定盒子，默认为0，0，0

  Vector2 mouse = Input.mousePosition;
    //unity自带的鼠标服务工具，左下角是0，0

   if(mouse.x<edgeSize) input.x-=1;//左边缘
   if(mouse.x>Screen.width-edgeSize) input.x+=1;//右边缘
   if(mouse.y<edgeSize) input.z-=1;//贴下
   if(mouse.y>Screen.height-edgeSize) input.z+=1;//贴上

        input.x += Input.GetAxis("Horizontal");
        input.z += Input.GetAxis("Vertical");
        //Input.GetAxis("")是unity自带的服务，按D返回+1.按A返回-1，键盘遥感都可以返回

        if (input.sqrMagnitude < 0.001f) return;
        //方向判定盒子为0，退出这帧


        Vector3 camForward=Vector3.ProjectOnPlane(transform.forward,Vector3.up).normalized;
        Vector3 camRight=Vector3.ProjectOnPlane(transform.right,Vector3.up).normalized;
        //vector3.projectonplane(方向，法线)unity自带的影子机器，斜着的方向投影到地面上
        //normalized是把向量长度拉成1
        Vector3 move = (camForward * input.z + camRight * input.x).normalized * panSpeed * Time.deltaTime;
        //括号里两个向量相加得到合成向量大小为1，time.deltatime是让帧率快慢不同的一样快

        Vector3 newPos = transform.position + move;

        newPos.x=Mathf.Clamp(newPos.x, -mapWidth*0.5f, mapWidth*0.5f);
        newPos.z = Mathf.Clamp(newPos.z, -mapDepth * 0.5f, mapDepth * 0.5f);
        newPos.y=transform.position.y;//高度永远定格
        //mathf.calmp(值，最小，最大) 控制范围
        //x的范围整体大小是mapwidth，按照一半大小，原点就定在了中间

        transform.position=newPos;//装回去，摄像头搬家
    }

    private void OnDrawGizmos()//unity编辑器里画辅助线调用，游戏看不到
    {
        Gizmos.color = Color.cyan;
        Vector3 center = new Vector3(0, transform.position.y, 0);
        //x=0,z=0,中心在原点，mathf.clamp定了原点
        Gizmos.DrawWireCube(center, new Vector3(mapWidth, 0, mapDepth));
        //gizmos=编辑器里的便签笔
        //画一个drawwirecube 线框立方体，中心是center，宽80 高0 深60
    }







}
