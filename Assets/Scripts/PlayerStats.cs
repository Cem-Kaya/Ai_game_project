using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public class PlayerStats : MonoBehaviour
{
    [SerializeField] private int health = 3;
    [SerializeField] private List<GameObject> hearts;
    [SerializeField] Transform spawnPoint;
    [SerializeField] private float invisFrame = 0.8f;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (health <= 0)
        {
            Debug.Log("dead");
            this.transform.position = spawnPoint.transform.position;
            health = 3;
        }
        if (invisFrame > 0)
        {
            invisFrame -= Time.deltaTime;
        }
    }
    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.transform.tag == "Enemy" && invisFrame <= 0)
        {
            health -= 1;
            invisFrame = 0.8f;
            hearts[health].gameObject.SetActive(false);
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.transform.tag == "Enemy" && invisFrame <= 0)
        {
            health -= 1;
            invisFrame = 0.8f;
            hearts[health].gameObject.SetActive(false);
        }
    }
}
