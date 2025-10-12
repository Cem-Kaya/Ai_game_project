using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{
    public void StartButton()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex + 1); //put the main menu scene as 0 
    }

    public void QuitGame()
    {
        Application.Quit();
    } 
}
