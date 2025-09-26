using UnityEngine;
using UnityEngine.SceneManagement;

public class GemCollector : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Gem"))
        {
            Destroy(collision.gameObject);
        }
        else if (collision.CompareTag("final"))
        {
            // Finish the level
            SceneManager.LoadScene(0);
        }
    }
}
