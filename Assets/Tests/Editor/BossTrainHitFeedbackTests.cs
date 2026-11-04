#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class BossTrainHitFeedbackTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (GameData.Instance != null) Object.Destroy(GameData.Instance.gameObject);
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator RightCapDisappearsAtDeathStartWhileBossIsStillPresent()
    {
        Boss boss = new GameObject("Boss cap timing test").AddComponent<Boss>();
        boss.usePool = false;
        boss.deathByHpDelay = 2f;
        GameObject cap = new GameObject("capRight");
        cap.transform.SetParent(boss.transform);
        Set(boss, "rightCapObj", cap);
        Call(boss, "DieByPlayer");
        Assert.IsTrue(boss.gameObject.activeSelf, "The boss still has its destruction sequence to play.");
        Assert.IsFalse(cap.activeSelf, "capRight must disappear immediately when HP death starts.");
        Assert.IsNull(typeof(Boss).GetField("rightCapObj", Private).GetValue(boss));
        boss.StopAllCoroutines();
        boss.ResetBossRuntime();
        var timeoutCap = new GameObject("Timeout capRight");
        timeoutCap.transform.SetParent(boss.transform);
        Set(boss, "rightCapObj", timeoutCap);
        Call(boss, "HideDestroyVisualParts");
        Assert.IsFalse(timeoutCap.activeSelf, "The shared destruction-animation entry also removes capRight for timeout death.");
        Assert.IsTrue(boss.gameObject.activeSelf);
        yield break;
    }

    [UnityTest]
    public IEnumerator ObstacleOxygenLossFlashesTrainAndOxygenUiAndShakesCameraThenRestoresOriginalColors()
    {
        GameData data = new GameObject("Oxygen loss data").AddComponent<GameData>();
        data.enabled = false;
        data.AddO2(4);
        var cameraObject = new GameObject("Main Camera", typeof(Camera));
        cameraObject.tag = "MainCamera";
        var shake = cameraObject.AddComponent<CameraShakeManager>();
        shake.defaultMagnitudeY = 0.2f;
        Vector3 cameraOrigin = cameraObject.transform.position;
        var trainRoot = new GameObject("Train", typeof(SpriteRenderer));
        var trainSprite = trainRoot.GetComponent<SpriteRenderer>();
        Color trainColor = new Color(0.3f, 0.7f, 0.8f, 0.6f);
        trainSprite.color = trainColor;
        Train train = trainRoot.AddComponent<Train>();
        ScoreUI ui = new GameObject("Score UI").AddComponent<ScoreUI>();
        var hud = new GameObject("GameplayHUD", typeof(RectTransform));
        var oxygenRoot = new GameObject("O2", typeof(RectTransform)).GetComponent<RectTransform>();
        oxygenRoot.SetParent(hud.transform, false);
        oxygenRoot.anchoredPosition = new Vector2(35f, -17f);
        Vector2 oxygenPosition = oxygenRoot.anchoredPosition;
        var icon = new GameObject("O2Icon", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        icon.transform.SetParent(oxygenRoot, false);
        Color iconColor = new Color(0.7f, 0.8f, 0.9f, 0.4f);
        icon.color = iconColor;
        var energy = new GameObject("Energy", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        energy.transform.SetParent(hud.transform, false);
        var oxygenText = new GameObject("O2txt", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        oxygenText.transform.SetParent(oxygenRoot, false);
        Set(ui, "gameplayHudRoot", hud.transform);
        ui.o2Text = new GameObject("Result O2", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        Color uiColor = new Color(0.1f, 0.2f, 0.3f, 0.8f);
        oxygenText.color = uiColor;
        ui.o2Text.color = Color.black;
        var gate = new GameObject("Gate collision", typeof(BoxCollider2D));
        gate.tag = "Gate";
        var requests = (IList)typeof(CameraShakeManager).GetField("activeShakes", Private).GetValue(shake);
        data.SpendO2(1);
        Assert.AreEqual(3, data.GetO2Score());
        Assert.AreEqual(trainColor, trainSprite.color, "Ordinary O2 spending must not trigger collision feedback.");
        Assert.AreEqual(uiColor, oxygenText.color);
        Assert.AreEqual(iconColor, icon.color);
        Assert.AreEqual(0, requests.Count);
        var obstacle = new GameObject("Machinegun obstacle", typeof(BoxCollider2D), typeof(Rigidbody2D)).AddComponent<ObstacleMover>();
        Call(obstacle, "TryHandleGateCollision", gate.GetComponent<Collider2D>());
        Assert.AreEqual(2, data.GetO2Score());
        Assert.AreEqual(new Color(1f, 0f, 0f, trainColor.a), trainSprite.color);
        Assert.AreEqual(new Color(1f, 0f, 0f, uiColor.a), ui.gameplayO2Text.color);
        Assert.AreSame(oxygenText, ui.gameplayO2Text, "Resolve the nested O2txt under the O2 container.");
        Assert.AreEqual(new Color(1f, 0f, 0f, iconColor.a), icon.color);
        Assert.AreEqual(Color.white, energy.color, "Only the O2 group is tinted.");
        Assert.AreEqual(Color.red, ui.o2Text.color);
        Assert.AreEqual(1, requests.Count);
        Call(obstacle, "TryHandleGateCollision", gate.GetComponent<Collider2D>());
        Assert.AreEqual(2, data.GetO2Score(), "Repeated collider notifications cannot charge the same obstacle twice.");
        bool cameraMoved = false;
        bool oxygenMoved = false;
        float until = Time.unscaledTime + 0.08f;
        while (Time.unscaledTime < until)
        {
            yield return null;
            cameraMoved |= (cameraObject.transform.position - cameraOrigin).sqrMagnitude > 0.00000001f;
            oxygenMoved |= Mathf.Abs(oxygenRoot.anchoredPosition.x - oxygenPosition.x) > 0.00001f;
            Assert.AreEqual(oxygenPosition.y, oxygenRoot.anchoredPosition.y);
        }
        Assert.IsTrue(cameraMoved);
        Assert.IsTrue(oxygenMoved, "The entire O2 container must shake horizontally.");
        HitGate(gate.GetComponent<Collider2D>());
        until = Time.unscaledTime + 0.12f;
        while (Time.unscaledTime < until) yield return null;
        Assert.AreEqual(new Color(1f, 0f, 0f, trainColor.a), trainSprite.color, "A repeated hit extends the red flash from the latest hit.");
        until = Time.unscaledTime + 0.12f;
        while (Time.unscaledTime < until) yield return null;
        Assert.AreEqual(trainColor, trainSprite.color);
        Assert.AreEqual(uiColor, ui.gameplayO2Text.color);
        Assert.AreEqual(Color.black, ui.o2Text.color);
        Assert.AreEqual(iconColor, icon.color);
        Assert.AreEqual(oxygenPosition, oxygenRoot.anchoredPosition, "Repeated hits must not accumulate position drift.");
        HitGate(gate.GetComponent<Collider2D>());
        train.Reinit();
        ui.enabled = false;
        Assert.AreEqual(trainColor, trainSprite.color, "Retry restores the train's original tint immediately.");
        Assert.AreEqual(uiColor, ui.gameplayO2Text.color);
        Assert.AreEqual(iconColor, icon.color);
        Assert.AreEqual(oxygenPosition, oxygenRoot.anchoredPosition);
        ui.enabled = true;
        requests.Clear();
        var emptyOxygenHit = HitGate(gate.GetComponent<Collider2D>());
        Assert.AreEqual(0, data.GetO2Score());
        Assert.AreEqual(1, requests.Count, "A damaging obstacle collision still gives feedback when O2 is already zero.");
        Assert.AreEqual(new Color(1f, 0f, 0f, trainColor.a), trainSprite.color);
        Assert.AreEqual(new Color(1f, 0f, 0f, iconColor.a), icon.color);
        Call(emptyOxygenHit, "TryHandleGateCollision", gate.GetComponent<Collider2D>());
        Assert.AreEqual(1, requests.Count, "The same collision still only produces one feedback sequence.");
        requests.Clear();
        train.Reinit();
        ui.enabled = false;
        ui.enabled = true;
        HitGate(gate.GetComponent<Collider2D>(), 0);
        Assert.AreEqual(0, requests.Count, "An obstacle without an O2 penalty must not trigger the warning sequence.");
        Assert.AreEqual(trainColor, trainSprite.color);
        Assert.AreEqual(iconColor, icon.color);
    }

    [UnityTest]
    public IEnumerator TrainFlashUsesInspectorIntensityAndDurationWithoutStackingTint()
    {
        var root = new GameObject("Configurable train flash", typeof(SpriteRenderer));
        var sprite = root.GetComponent<SpriteRenderer>();
        Color original = new Color(0.2f, 0.6f, 0.9f, 0.45f);
        sprite.color = original;
        Train train = root.AddComponent<Train>();
        var inspector = new SerializedObject(train);
        inspector.FindProperty("oxygenHitRedIntensity").floatValue = 0.4f;
        inspector.FindProperty("oxygenHitFlashDuration").floatValue = 0.35f;
        inspector.ApplyModifiedPropertiesWithoutUndo();
        Color expected = Color.Lerp(original, new Color(1f, 0f, 0f, original.a), 0.4f);
        train.FlashOxygenLoss();
        Assert.AreEqual(expected, sprite.color);
        float until = Time.unscaledTime + 0.1f;
        while (Time.unscaledTime < until) yield return null;
        train.FlashOxygenLoss();
        Assert.AreEqual(expected, sprite.color, "Another hit must blend from the original color, not the previous flash.");
        until = Time.unscaledTime + 0.22f;
        while (Time.unscaledTime < until) yield return null;
        Assert.AreEqual(expected, sprite.color, "The configured duration exceeds the old fixed 0.2 seconds.");
        until = Time.unscaledTime + 0.16f;
        while (Time.unscaledTime < until) yield return null;
        Assert.AreEqual(original, sprite.color);
        train.oxygenHitFlashDuration = 0f;
        train.FlashOxygenLoss();
        Assert.AreEqual(original, sprite.color);
        train.oxygenHitFlashDuration = 0.2f;
        train.oxygenHitRedIntensity = 0f;
        train.FlashOxygenLoss();
        Assert.AreEqual(original, sprite.color);
    }

    [UnityTest]
    public IEnumerator NestedHudNumbersUpdateAndOnlyTheCorrespondingIconPulsesOnGains()
    {
        GameData data = new GameObject("HUD data").AddComponent<GameData>();
        data.enabled = false;
        var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
        ScoreUI ui = canvas.AddComponent<ScoreUI>();
        var hud = new GameObject("GameplayHUD", typeof(RectTransform));
        hud.transform.SetParent(canvas.transform, false);
        RectTransform energyBackground = HudRect("Energy Background", hud.transform);
        RectTransform oxygenBackground = HudRect("O2 Background", hud.transform);
        TextMeshProUGUI energyNumber = HudRect("EnergyTxt", energyBackground).gameObject.AddComponent<TextMeshProUGUI>();
        TextMeshProUGUI oxygenNumber = HudRect("O2Txt", oxygenBackground).gameObject.AddComponent<TextMeshProUGUI>();
        energyNumber.text = "Energy";
        oxygenNumber.text = "O2";
        TextMeshProUGUI title = HudRect("Energy", hud.transform).gameObject.AddComponent<TextMeshProUGUI>();
        title.text = "Energy";
        RectTransform energyIcon = HudRect("EnergyIcon", energyBackground);
        RectTransform oxygenIcon = HudRect("O2Icon", oxygenBackground);
        energyIcon.localScale = new Vector3(0.8f, 1.1f, 1f);
        oxygenIcon.localScale = new Vector3(0.7f, 0.7f, 1f);
        Vector3 energyScale = energyIcon.localScale;
        Vector3 oxygenScale = oxygenIcon.localScale;

        Call(ui, "Update");
        Assert.AreSame(energyNumber, ui.gameplayEnergyText);
        Assert.AreSame(oxygenNumber, ui.gameplayO2Text);
        Assert.AreEqual("0", energyNumber.text);
        Assert.AreEqual("0", oxygenNumber.text);
        Assert.AreEqual("Energy", title.text, "A static title is not the numeric label.");

        data.AddEnergyScore(3);
        Call(ui, "Update");
        Assert.AreEqual("3", energyNumber.text);
        float peak = 1f;
        float until = Time.unscaledTime + 0.36f;
        while (Time.unscaledTime < until)
        {
            yield return null;
            peak = Mathf.Max(peak, energyIcon.localScale.x / energyScale.x);
            Assert.AreEqual(oxygenScale, oxygenIcon.localScale, "An energy gain only animates EnergyIcon.");
            Assert.AreEqual(Vector3.one, energyBackground.localScale);
            Assert.AreEqual(Vector3.one, oxygenBackground.localScale);
            Assert.AreEqual(Vector3.one, energyNumber.rectTransform.localScale);
            Assert.AreEqual(Vector3.one, oxygenNumber.rectTransform.localScale);
        }
        Assert.That(peak, Is.InRange(1.18f, 1.201f));
        Assert.AreEqual(energyScale, energyIcon.localScale);

        data.score = 2;
        data.AddO2(4);
        Call(ui, "Update");
        Assert.AreEqual("2", energyNumber.text);
        Assert.AreEqual("4", oxygenNumber.text);
        peak = 1f;
        until = Time.unscaledTime + 0.36f;
        while (Time.unscaledTime < until)
        {
            yield return null;
            peak = Mathf.Max(peak, oxygenIcon.localScale.x / oxygenScale.x);
            Assert.AreEqual(energyScale, energyIcon.localScale, "An O2 gain and energy decrease do not animate EnergyIcon.");
            Assert.AreEqual(Vector3.one, oxygenBackground.localScale);
            Assert.AreEqual(Vector3.one, oxygenNumber.rectTransform.localScale);
        }
        Assert.That(peak, Is.InRange(1.18f, 1.201f));
        Assert.AreEqual(oxygenScale, oxygenIcon.localScale);

        data.AddO2();
        Call(ui, "Update");
        until = Time.unscaledTime + 0.08f;
        while (Time.unscaledTime < until) yield return null;
        Vector3 beforeRepeat = oxygenIcon.localScale;
        data.AddO2();
        Call(ui, "Update");
        Call(ui, "LateUpdate");
        Assert.That((oxygenIcon.localScale - beforeRepeat).sqrMagnitude, Is.LessThan(0.000001f));
        ui.enabled = false;
        Assert.AreEqual(oxygenScale, oxygenIcon.localScale, "Disabling the HUD restores the icon without scale accumulation.");
    }

    private static RectTransform HudRect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static ObstacleMover HitGate(Collider2D gate, int penalty = 1)
    {
        var obstacle = new GameObject("O2 penalty obstacle", typeof(BoxCollider2D), typeof(Rigidbody2D)).AddComponent<ObstacleMover>();
        Set(obstacle, "gateOxygenPenalty", penalty);
        Call(obstacle, "TryHandleGateCollision", gate);
        return obstacle;
    }

    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    private static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
}
#endif
