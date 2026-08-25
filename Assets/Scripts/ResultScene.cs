using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
public class ResultScene : MonoBehaviour
{


    void Update()
    {
        if (Keyboard.current != null &&
            Keyboard.current.enterKey.wasPressedThisFrame)
        {
            SceneManager.LoadScene("TitleScene");
        }
    }

}