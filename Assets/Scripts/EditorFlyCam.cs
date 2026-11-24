using UnityEngine;
using UnityEngine.InputSystem;

public class EditorFlyCam : MonoBehaviour
{
    public float moveSpeed = 0.6f;
    public float turnSpeed = 60f;

    void Update()
    {
#if UNITY_EDITOR
        var k = Keyboard.current;
        if (k == null) return;

        float yaw = 0f, pitch = 0f;
        if (k.leftArrowKey.isPressed)  yaw -= 1f;
        if (k.rightArrowKey.isPressed) yaw += 1f;
        if (k.upArrowKey.isPressed)    pitch -= 1f;
        if (k.downArrowKey.isPressed)  pitch += 1f;
        transform.Rotate(0f, yaw * turnSpeed * Time.deltaTime, 0f, Space.World);
        transform.Rotate(pitch * turnSpeed * Time.deltaTime, 0f, 0f, Space.Self);

        Vector3 move = Vector3.zero;
        if (k.wKey.isPressed) move += transform.forward;
        if (k.sKey.isPressed) move -= transform.forward;
        if (k.aKey.isPressed) move -= transform.right;
        if (k.dKey.isPressed) move += transform.right;
        if (k.eKey.isPressed) move += Vector3.up;
        if (k.qKey.isPressed) move -= Vector3.up;
        transform.position += move * moveSpeed * Time.deltaTime;
#endif
    }
}