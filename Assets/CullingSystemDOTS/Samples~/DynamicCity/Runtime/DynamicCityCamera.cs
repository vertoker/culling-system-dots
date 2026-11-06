using System.Collections.Generic;
using UnityEngine;

namespace CullingSystemDOTS.Samples.DynamicCity
{
    public sealed class DynamicCityCamera : MonoBehaviour
    {
        public DynamicCityBootstrap Sample;
        public bool Tour;
        private readonly HashSet<KeyCode> held=new HashSet<KeyCode>();
        private float elapsed,yaw,pitch;
        private bool wasTour=true;
        public void SetPose(Vector3 position,Vector3 lookAt)
        {
            Tour=wasTour=false;held.Clear();transform.position=position;transform.LookAt(lookAt);
            yaw=transform.eulerAngles.y;pitch=Mathf.DeltaAngle(0,transform.eulerAngles.x);
        }
        private void Update()
        {
            if(Sample==null)return;
            if(wasTour && !Tour){yaw=transform.eulerAngles.y;pitch=Mathf.DeltaAngle(0,transform.eulerAngles.x);}wasTour=Tour;
            if(Tour)
            {
                elapsed+=Time.deltaTime;float phase=elapsed*.12f;
                var home=(Vector3)DynamicCityBootstrap.Home(0);
                transform.position=home+new Vector3(Mathf.Sin(phase)*11,1.7f,-Mathf.Cos(phase)*11);
                transform.LookAt(home+new Vector3(0,1.7f,0));return;
            }
            float speed=(held.Contains(KeyCode.LeftShift)?12:4)*Time.deltaTime;
            var forward=Vector3.ProjectOnPlane(transform.forward,Vector3.up).normalized;var right=Vector3.ProjectOnPlane(transform.right,Vector3.up).normalized;
            transform.position+=speed*((held.Contains(KeyCode.W)?forward:Vector3.zero)-(held.Contains(KeyCode.S)?forward:Vector3.zero)+
                (held.Contains(KeyCode.D)?right:Vector3.zero)-(held.Contains(KeyCode.A)?right:Vector3.zero)+
                (held.Contains(KeyCode.Space)?Vector3.up:Vector3.zero)-(held.Contains(KeyCode.LeftControl)?Vector3.up:Vector3.zero));
        }
        private void OnGUI()
        {
            var current=Event.current;
            if(current.type==EventType.KeyDown){held.Add(current.keyCode);if(current.keyCode==KeyCode.T)Tour=!Tour;}
            if(current.type==EventType.KeyUp)held.Remove(current.keyCode);
            if(!Tour && current.type==EventType.MouseDrag && current.button==1)
            {yaw+=current.delta.x*.2f;pitch=Mathf.Clamp(pitch+current.delta.y*.2f,-85,85);transform.rotation=Quaternion.Euler(pitch,yaw,0);}
        }
        private void OnApplicationFocus(bool focused){if(!focused)held.Clear();}
    }
}
