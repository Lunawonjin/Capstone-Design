using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LovelyIslandSample
{
    public class CameraController : MonoBehaviour
    {
        public Button LeftBtn;
        public Button RightBtn;
        public GameObject Cam;
        int _idx = 0;
        int max = 6;
        void Start()
        {
            LeftBtn.onClick.AddListener(OnClickLeft);
            RightBtn.onClick.AddListener(OnClickRight);
            Cam.transform.position = new Vector3(0f, 0f, -10f);
        }

        void OnClickRight()
        {
            _idx++;
            if (_idx >= max)
            {
                _idx = max;
            }
            SetCamera();
        }

        void OnClickLeft()
        {
            _idx--;
            if (_idx <= 0)
            {
                _idx = 0;
            }
            SetCamera();
        }

        void SetCamera()
        {
            Cam.transform.position = new Vector3(0f + (_idx * 20), 0f, -10f);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                OnClickRight();
            }
            else if (Input.GetKeyDown(KeyCode.LeftArrow))
            {
                OnClickLeft();
            }
        }
    }
}

