using TMPro;
using UnityEngine;

public class ScoreUI : MonoBehaviour
{
    public TextMeshProUGUI scoreText; // ENERGY
    public TextMeshProUGUI o2Text;    // O2

    void Update()
    {
        if (GameData.Instance == null) return;

        scoreText.text = $"ENERGY: {GameData.Instance.GetCleanScore()}";
        o2Text.text = $"O2: {GameData.Instance.GetO2Score()}";
    }
}
