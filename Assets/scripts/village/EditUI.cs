using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class EditUI : MonoBehaviour
{
    private const string SoundKey = "settings.sound.enabled";
    private const string MusicKey = "settings.music.enabled";
    private const string AccountKey = "settings.account.info";

    [SerializeField] private GameObject panel;
    [SerializeField] private Toggle soundToggle;
    [SerializeField] private Toggle musicToggle;
    [SerializeField] private TMP_Text accountInfoText;
    [SerializeField] private TMP_InputField accountInputField;
    [SerializeField] private Button closeButton;

    private void Awake()
    {
        if (panel != null)
            panel.SetActive(false);

        if (soundToggle != null)
            soundToggle.onValueChanged.AddListener(HandleSoundChanged);
        if (musicToggle != null)
            musicToggle.onValueChanged.AddListener(HandleMusicChanged);
        if (accountInputField != null)
            accountInputField.onEndEdit.AddListener(HandleAccountEdited);
        if (closeButton != null)
            closeButton.onClick.AddListener(Close);

        LoadSettingsIntoUi();
    }

    public void Open()
    {
        LoadSettingsIntoUi();
        if (panel != null)
            panel.SetActive(true);
    }

    public void Close()
    {
        if (panel != null)
            panel.SetActive(false);
    }

    private void HandleSoundChanged(bool enabled)
    {
        PlayerPrefs.SetInt(SoundKey, enabled ? 1 : 0);
        AudioListener.volume = enabled ? 1f : 0f;
        PlayerPrefs.Save();
    }

    private void HandleMusicChanged(bool enabled)
    {
        PlayerPrefs.SetInt(MusicKey, enabled ? 1 : 0);
        AudioSource[] allSources = FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
        for (int i = 0; i < allSources.Length; i++)
        {
            AudioSource source = allSources[i];
            if (source != null && source.loop)
                source.mute = !enabled;
        }
        PlayerPrefs.Save();
    }

    private void HandleAccountEdited(string value)
    {
        PlayerPrefs.SetString(AccountKey, value ?? string.Empty);
        PlayerPrefs.Save();
        RefreshAccountText();
    }

    private void LoadSettingsIntoUi()
    {
        bool soundEnabled = PlayerPrefs.GetInt(SoundKey, 1) == 1;
        bool musicEnabled = PlayerPrefs.GetInt(MusicKey, 1) == 1;

        if (soundToggle != null)
            soundToggle.isOn = soundEnabled;
        if (musicToggle != null)
            musicToggle.isOn = musicEnabled;
        if (accountInputField != null)
            accountInputField.text = PlayerPrefs.GetString(AccountKey, "Guest");

        AudioListener.volume = soundEnabled ? 1f : 0f;
        RefreshAccountText();
    }

    private void RefreshAccountText()
    {
        if (accountInfoText != null)
            accountInfoText.text = PlayerPrefs.GetString(AccountKey, "Guest");
    }
}
