using TMPro;
using UnityEngine;

public class ScoreUI : MonoBehaviour
{
    public TextMeshProUGUI scoreText; // SCORE
    public TextMeshProUGUI o2Text;    // O2

    void Update()
    {
        if (GameData.Instance == null) return;

        scoreText.text = $"SCORE: {GameData.Instance.GetCleanScore()}";
        o2Text.text = $"O2: {GameData.Instance.GetO2Score()}";
    }
}
