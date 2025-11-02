using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using TarodevController;

public class PlayerStats : MonoBehaviour
{
    [SerializeField] private int health = 3;
    [SerializeField] private List<GameObject> hearts;
    [SerializeField] public Transform spawnPoint;
    [SerializeField] private float invisFrame = 0.8f;
    [SerializeField] private float blinkInterval = 0.1f;
    [SerializeField] private Collider2D attackAreaCollider;
    [SerializeField] private TMP_Text PointsText;
    private int Points = 0;
    [Header("Audio")]
    [SerializeField] private AudioClip hitSound; // assign the .wav file here

    private AudioSource audioSource;
    private SpriteRenderer sr;
    private Coroutine blinkRoutine;
    

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>() ?? GetComponentInChildren<SpriteRenderer>();
        audioSource = GetComponent<AudioSource>();

        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        audioSource.playOnAwake = false;
        getPoints(0);
        RefreshHeartsVisuals();
    }

    void Update()
    {
        if (health <= 0)
        {
            if (spawnPoint.position != null)
                transform.position = spawnPoint.position;
            else 
                transform.position = Vector3.zero;
            health = 3;
            RefreshHeartsVisuals();
        }

        if (invisFrame > 0f)
            invisFrame -= Time.deltaTime;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.otherCollider == attackAreaCollider)
        {
            return;
        }
        if (collision.transform.CompareTag("Enemy") && invisFrame <= 0f)
        {
            TakeHit();
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (attackAreaCollider != null && attackAreaCollider.IsTouching(collision))
        {
            if (collision.transform.tag == "Enemy")
            {
                getPoints(200);
            }
            return;
        }
        if (collision.transform.CompareTag("Enemy") && invisFrame <= 0f)
        {
            TakeHit();
        }
    }

    private void TakeHit()
    {
        health = Mathf.Max(health - 1, 0);

        // Disable one heart
        if (health >= 0 && health < hearts.Count)
            hearts[health].SetActive(false);

        // Play hit sound once
        if (hitSound != null)
            audioSource.PlayOneShot(hitSound);
        removePoints(100);
        StartInvincibilityBlink();
    }

    private void StartInvincibilityBlink()
    {
        invisFrame = 0.8f;

        if (blinkRoutine != null)
            StopCoroutine(blinkRoutine);

        blinkRoutine = StartCoroutine(BlinkDuringInvincibility());
    }

    private IEnumerator BlinkDuringInvincibility()
    {
        List<GameObject> blinkingHearts = new List<GameObject>();
        for (int i = 0; i < hearts.Count; i++)
        {
            if (hearts[i].activeSelf) blinkingHearts.Add(hearts[i]);
        }

        bool visible = true;
        while (invisFrame > 0f)
        {
            visible = !visible;
            if (sr != null) sr.enabled = visible;
            foreach (var h in blinkingHearts)
                if (h != null) h.SetActive(visible);
            yield return new WaitForSeconds(blinkInterval);
        }

        if (sr != null) sr.enabled = true;
        RefreshHeartsVisuals();
        blinkRoutine = null;
    }

    private void RefreshHeartsVisuals()
    {
        for (int i = 0; i < hearts.Count; i++)
        {
            bool shouldBeOn = i < health;
            if (hearts[i] != null && hearts[i].activeSelf != shouldBeOn)
                hearts[i].SetActive(shouldBeOn);
        }
    }

    public void getPoints(int amountOfPoints)
    {
        Points += amountOfPoints;
        PointsText.text = "Points: " + Points; 
    }

    public void removePoints(int amountOfPoints)
    {
        if (Points >= amountOfPoints)
        {
            Points -= amountOfPoints;
            PointsText.text = "Points: " + Points;
        }
        else
        {
            Points = 0;
        }
    }
}
