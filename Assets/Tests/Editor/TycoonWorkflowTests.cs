#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using NUnit.Framework;
using Tycoon;
using UnityEngine;
using ResourceItem = Tycoon.Resources;
using UnityEngine.UI;
using TMPro;

public sealed class TycoonWorkflowTests
{
    private readonly List<GameObject> roots = new List<GameObject>();
    private readonly List<Sprite> testSprites = new List<Sprite>();
    private readonly List<Texture2D> testTextures = new List<Texture2D>();
    private readonly List<Camera> previousMainCameras = new List<Camera>();
    private Building breadBuilding;
    private ResourceItem bread;
    private Receipt receipt;
    private VillageManagement village;

    [SetUp]
    public void SetUp()
    {
        foreach (Camera camera in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            if (camera.CompareTag("MainCamera"))
            {
                previousMainCameras.Add(camera);
                camera.tag = "Untagged";
            }
        breadBuilding = Building("Bread");
        bread = Root("BreadResource").AddComponent<ResourceItem>();
        Set(bread, "buildingPrefab", breadBuilding.gameObject);
        Set(bread, "energyPrice", 3);
        Set(bread, "requiredQuantity", 4);
        receipt = Root("Receipt").AddComponent<Receipt>();
        village = Root("TestVillage").AddComponent<VillageManagement>();
        Set(village, "autoSaveOnChange", false);
        village.SaveData.energyCapacity = 100;
        village.SaveData.currentEnergy = 30;
        village.SaveData.oxygenCapacity = 100;
        village.SaveData.currentOxygen = 30;
    }

    [TearDown]
    public void TearDown()
    {
        foreach (DeliveryRuntime runtime in Object.FindObjectsByType<DeliveryRuntime>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Object.DestroyImmediate(runtime.gameObject);
        for (int i = roots.Count - 1; i >= 0; i--)
            if (roots[i] != null) Object.DestroyImmediate(roots[i]);
        roots.Clear();
        foreach (Sprite sprite in testSprites) Object.DestroyImmediate(sprite);
        testSprites.Clear();
        foreach (Texture2D texture in testTextures) Object.DestroyImmediate(texture);
        testTextures.Clear();
        foreach (Camera camera in previousMainCameras)
            if (camera != null) camera.tag = "MainCamera";
        previousMainCameras.Clear();
        VillagePointerCapture.ReleaseAll();
    }

    [TestCase(false)]
    [TestCase(true)]
    public void StoreUsesRegisteredDigitSpritesForMultiDigitPricesAndReceiptAndResizesWithoutReset(bool useUi)
    {
        Sprite[] digits = new Sprite[10];
        for (int i = 0; i < 10; i++) digits[i] = DigitSprite("Digit" + i);
        Sprite slash = DigitSprite("Slash");
        Sprite question = DigitSprite("Question");
        StoreIn store = Root("SpriteNumberStore").AddComponent<StoreIn>();
        receipt.transform.SetParent(store.transform, false);
        Set(store, "receiptPrefab", receipt.gameObject);
        Set(store, "numberSprites", digits);
        Set(store, "numberScale", 1.25f);
        Set(store, "slashSprite", slash);
        Set(store, "questionMarkSprite", question);
        Transform labelParent = store.transform;
        if (useUi)
        {
            var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            canvas.transform.SetParent(store.transform, false);
            canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            labelParent = canvas.transform;
        }
        Transform pricePosition = NumberPosition("PricePosition", labelParent, useUi);
        Transform quantityPosition = NumberPosition("QuantityPosition", labelParent, useUi);
        Set(receipt, "quantityPosition", quantityPosition);
        Transform resourcePosition = new GameObject("ResourcePosition").transform;
        resourcePosition.SetParent(store.transform, false);
        Set(bread, "energyPrice", 120);
        Set(bread, "requiredQuantity", 12);
        village.SaveData.energyCapacity = 2000;
        village.SaveData.currentEnergy = 1000;
        Set(store, "resources", new List<StoreIn.ResourceSlot> { new StoreIn.ResourceSlot
        {
            resourcePrefab = bread.gameObject,
            resourcePosition = resourcePosition,
            pricePosition = pricePosition
        }});
        store.Initialize();
        SpriteNumberLabel price = pricePosition.GetComponentInChildren<SpriteNumberLabel>();
        SpriteNumberLabel quantity = quantityPosition.GetComponentInChildren<SpriteNumberLabel>();
        Assert.AreEqual("120", price.Value);
        CollectionAssert.AreEqual(new[] { digits[1], digits[2], digits[0] }, LabelSprites(price, useUi));
        CollectionAssert.AreEqual(new[] { digits[0] }, LabelSprites(quantity, useUi));
        Assert.AreEqual(0, price.GetComponentsInChildren<TextMesh>().Length);
        Assert.AreEqual(0, price.GetComponentsInChildren<Text>().Length);
        Assert.AreEqual(0, price.GetComponentsInChildren<TMP_Text>().Length);
        float height = LabelHeight(price, useUi);
        if (!useUi)
        {
            SpriteRenderer[] renderers = price.GetComponentsInChildren<SpriteRenderer>();
            float min = renderers.Min(renderer => renderer.bounds.min.x);
            float max = renderers.Max(renderer => renderer.bounds.max.x);
            Assert.Less(Mathf.Abs((min + max) * 0.5f - price.transform.position.x), 0.0001f);
        }
        Assert.IsTrue(receipt.TryAdd(bread, village));
        Assert.AreEqual("1/12", quantity.Value);
        CollectionAssert.AreEqual(new[] { digits[1], slash, digits[1], digits[2] }, LabelSprites(quantity, useUi));
        Set(store, "numberScale", 2.5f);
        Call(store, "OnValidate");
        Call(store, "Update");
        Assert.AreEqual(1, receipt.Quantity);
        Assert.AreEqual("1/12", quantity.Value);
        Assert.AreEqual(height * 2f, LabelHeight(price, useUi), 0.0001f);
        Set(breadBuilding, "isPlaced", false);
        Call(store, "RefreshAvailability");
        Assert.AreEqual("?", price.Value);
        CollectionAssert.AreEqual(new[] { question }, LabelSprites(price, useUi));
        receipt.Clear();
        Assert.AreEqual("0", quantity.Value);
        CollectionAssert.AreEqual(new[] { digits[0] }, LabelSprites(quantity, useUi));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NarrowAndWideDigitsKeepFixedCentersAndTheirOriginalAspectRatio(bool useUi)
    {
        var texture = new Texture2D(32, 16);
        testTextures.Add(texture);
        Sprite[] digits = new Sprite[10];
        for (int i = 0; i < digits.Length; i++) digits[i] = DigitSprite("Digit" + i);
        digits[0] = Sprite.Create(texture, new Rect(0, 0, 24, 16), new Vector2(0.1f, 0.8f), 16f, 0, SpriteMeshType.FullRect);
        digits[1] = Sprite.Create(texture, new Rect(0, 0, 4, 16), new Vector2(0.1f, 0.8f), 16f, 0, SpriteMeshType.FullRect);
        testSprites.Add(digits[0]);
        testSprites.Add(digits[1]);
        Transform anchor = Root("NumberPosition").transform;
        if (useUi)
        {
            var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            canvas.transform.SetParent(anchor, false);
            canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            anchor = canvas.transform;
        }
        SpriteNumberLabel label = SpriteNumberLabel.Create(anchor, digits, 1.5f).GetComponent<SpriteNumberLabel>();
        label.SetValue("1001");
        Transform[] glyphs = label.transform.Cast<Transform>().ToArray();
        float[] centers = glyphs.Select(glyph => glyph.localPosition.x).ToArray();
        float spacing = centers[1] - centers[0];
        Assert.AreEqual(spacing, centers[2] - centers[1], 0.0001f);
        Assert.AreEqual(spacing, centers[3] - centers[2], 0.0001f);
        for (int i = 0; i < glyphs.Length; i++)
        {
            float expectedAspect = i == 0 || i == 3 ? 0.25f : 1.5f;
            if (useUi)
            {
                RectTransform image = glyphs[i].GetComponentInChildren<Image>().rectTransform;
                Assert.AreEqual(expectedAspect, image.rect.width / image.rect.height, 0.0001f);
                Assert.AreEqual(centers[i], label.transform.InverseTransformPoint(image.TransformPoint(image.rect.center)).x, 0.0001f);
            }
            else
            {
                SpriteRenderer renderer = glyphs[i].GetComponentInChildren<SpriteRenderer>();
                Assert.AreEqual(expectedAspect, renderer.bounds.size.x / renderer.bounds.size.y, 0.0001f);
                Assert.AreEqual(centers[i], label.transform.InverseTransformPoint(renderer.bounds.center).x, 0.0001f);
            }
        }
        foreach (string value in new[] { "1111", "0000" })
        {
            label.SetValue(value);
            float[] changedCenters = label.transform.Cast<Transform>().Select(glyph => glyph.localPosition.x).ToArray();
            CollectionAssert.AreEqual(centers, changedCenters);
        }
    }

    [Test]
    public void MissingOptionalSpritesUseSpriteGeometryForDigitsSlashAndQuestion()
    {
        GameObject root = SpriteNumberLabel.Create(Root("NumberPosition").transform, new Sprite[10], 1f);
        SpriteNumberLabel label = root.GetComponent<SpriteNumberLabel>();
        label.SetValue("0/4?");
        Assert.AreEqual(4, root.transform.childCount);
        foreach (Transform glyph in root.transform)
            Assert.Greater(glyph.GetComponentsInChildren<SpriteRenderer>().Length, 0);
        Assert.AreEqual(0, root.GetComponentsInChildren<TextMesh>().Length);
        Assert.AreEqual(0, root.GetComponentsInChildren<Text>().Length);
        Assert.AreEqual(0, root.GetComponentsInChildren<TMP_Text>().Length);
        label.SetValue("1037");
        foreach (Transform glyph in root.transform)
        {
            SpriteRenderer[] parts = glyph.GetComponentsInChildren<SpriteRenderer>();
            float center = (parts.Min(part => part.bounds.min.x) + parts.Max(part => part.bounds.max.x)) * 0.5f;
            Assert.AreEqual(glyph.position.x, center, 0.0001f);
        }
    }

    private Sprite DigitSprite(string name)
    {
        Sprite sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.1f, 0.8f), 1f);
        sprite.name = name;
        testSprites.Add(sprite);
        return sprite;
    }
    private static Transform NumberPosition(string name, Transform parent, bool ui)
    {
        var root = ui ? new GameObject(name, typeof(RectTransform)) : new GameObject(name);
        root.transform.SetParent(parent, false);
        return root.transform;
    }
    private static Sprite[] LabelSprites(SpriteNumberLabel label, bool ui) => ui
        ? label.GetComponentsInChildren<Image>().Select(image => image.sprite).ToArray()
        : label.GetComponentsInChildren<SpriteRenderer>().Select(renderer => renderer.sprite).ToArray();
    private static float LabelHeight(SpriteNumberLabel label, bool ui) => ui
        ? label.GetComponentInChildren<Image>().rectTransform.rect.height
        : label.GetComponentInChildren<SpriteRenderer>().bounds.size.y;

    [Test]
    public void SpriteBoxUsesRegisteredWorldPointInsteadOfCameraCenter()
    {
        Camera camera = Root("Camera").AddComponent<Camera>();
        camera.tag = "MainCamera";
        camera.transform.position = new Vector3(0f, 0f, -10f);
        Transform point = Root("PanelPosition").transform;
        point.position = new Vector3(3f, -2f, 0f);
        point.localScale = new Vector3(0.8f, 0.7f, 1f);
        BoxIn inventory = Inventory(1);
        var button = Root("BoxButton").AddComponent<Tycoon.Box>();
        Set(button, "boxInPrefab", inventory.gameObject);
        Set(button, "panelPosition", point);
        Assert.AreSame(inventory, button.EnsurePanel());
        button.Toggle();
        Assert.Less(Vector3.Distance(point.position, inventory.transform.position), 0.001f);
        Assert.Less(Vector3.Distance(point.lossyScale, inventory.transform.lossyScale), 0.001f);
        Assert.IsNull(inventory.transform.parent);
        point.position = new Vector3(-1f, 2f, 0f);
        Call(inventory.GetComponent<DeliveryPanel>(), "LateUpdate");
        Assert.Less(Vector3.Distance(point.position, inventory.transform.position), 0.001f);
    }

    [TestCase(RenderMode.ScreenSpaceOverlay)]
    [TestCase(RenderMode.ScreenSpaceCamera)]
    public void SpriteStoreFollowsCanvasPointWithoutInheritingCanvasScale(RenderMode mode)
    {
        Camera camera = Root("Camera").AddComponent<Camera>();
        camera.tag = "MainCamera";
        camera.orthographic = true;
        camera.orthographicSize = 5f;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        var canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
        roots.Add(canvasObject);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = mode;
        canvas.worldCamera = camera;
        canvas.planeDistance = 2f;
        var pointObject = new GameObject("PanelPosition", typeof(RectTransform));
        pointObject.transform.SetParent(canvas.transform, false);
        var point = (RectTransform)pointObject.transform;
        point.anchoredPosition = new Vector2(100f, -65f);
        point.localScale = Vector3.one * 0.75f;
        Canvas.ForceUpdateCanvases();
        GameObject source = Root("StorePanel");
        StoreIn storePanel = source.AddComponent<StoreIn>();
        Set(storePanel, "receiptPrefab", receipt.gameObject);
        receipt.transform.SetParent(source.transform, false);
        var button = Root("StoreButton").AddComponent<Store>();
        Set(button, "storeInPrefab", source);
        Set(button, "panelPosition", point);
        button.Toggle();

        Camera uiCamera = mode == RenderMode.ScreenSpaceOverlay ? null : camera;
        Vector2 expected = RectTransformUtility.WorldToScreenPoint(uiCamera, point.position);
        Assert.Less(Vector2.Distance(expected, camera.WorldToScreenPoint(source.transform.position)), 0.1f);
        Assert.Greater(source.transform.lossyScale.x, 0.1f);
        Assert.IsNull(source.transform.parent);

        point.anchoredPosition += new Vector2(45f, 20f);
        Canvas.ForceUpdateCanvases();
        Call(source.GetComponent<DeliveryPanel>(), "LateUpdate");
        expected = RectTransformUtility.WorldToScreenPoint(uiCamera, point.position);
        Assert.Less(Vector2.Distance(expected, camera.WorldToScreenPoint(source.transform.position)), 0.1f);
        button.Toggle();
        button.Toggle();
        Assert.IsTrue(source.activeSelf);
        Assert.Less(Vector2.Distance(expected, camera.WorldToScreenPoint(source.transform.position)), 0.1f);
    }

    [Test]
    public void PackingPrefabHasPlayablePackAndOpenAnimations()
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Tycoon/Box.prefab");
        Assert.IsNotNull(prefab);
        Animator animator = prefab.GetComponent<Animator>();
        Assert.IsNotNull(animator);
        var controller = animator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
        Assert.IsNotNull(controller);
        Assert.Greater(controller.layers.Length, 0);
        CollectionAssert.IsSubsetOf(new[] { "pack", "boxopen" }, controller.parameters.Select(parameter => parameter.name).ToArray());
        var states = controller.layers[0].stateMachine.states.Select(child => child.state).ToArray();
        foreach (string name in new[] { "Pack", "Open", "Idle" })
        {
            var state = states.Single(item => item.name == name);
            Assert.IsNotNull(state.motion);
            Assert.Greater(((AnimationClip)state.motion).length, 0f);
        }
    }

    [Test]
    public void PurchaseRequiresPlacedBuildingAndSufficientEnergy()
    {
        Set(breadBuilding, "isPlaced", false);
        Assert.IsFalse(receipt.TryAdd(bread, village));
        Assert.AreEqual(30, village.CurrentEnergy);
        Set(breadBuilding, "isPlaced", true);
        village.SaveData.currentEnergy = 2;
        Assert.IsFalse(receipt.TryAdd(bread, village));
        Assert.AreEqual(0, receipt.Quantity);
        Assert.AreEqual(2, village.CurrentEnergy);
        village.SaveData.currentEnergy = 3;
        Assert.IsTrue(receipt.TryAdd(bread, village));
        Assert.AreEqual(1, receipt.Quantity);
        Assert.AreEqual(0, village.CurrentEnergy);
        Assert.AreEqual(30, village.CurrentOxygen);
    }

    [Test]
    public void StoreAllowsPurchaseWithEnergyWhenOxygenIsEmpty()
    {
        StoreIn store = Root("EnergyStore").AddComponent<StoreIn>();
        Set(store, "receipt", receipt);
        var instance = typeof(VillageManagement).GetProperty("Instance");
        VillageManagement previous = VillageManagement.Instance;
        instance.SetValue(null, village);
        try
        {
            village.SaveData.currentOxygen = 0;
            Assert.IsTrue(store.CanBuy(bread));
            Assert.IsTrue(receipt.TryAdd(bread, village));
            Assert.AreEqual(27, village.CurrentEnergy);
            Assert.AreEqual(0, village.CurrentOxygen);
            village.SaveData.currentEnergy = 2;
            village.SaveData.currentOxygen = 100;
            Assert.IsFalse(store.CanBuy(bread));
            Assert.IsFalse(receipt.TryAdd(bread, village));
            Assert.AreEqual(1, receipt.Quantity);
            Assert.AreEqual(100, village.CurrentOxygen);
        }
        finally { instance.SetValue(null, previous); }
    }

    [Test]
    public void BuildingTradesAwardOxygenAndLeaveEnergyUntouched()
    {
        Set(breadBuilding, "oxygenProducedPerTrade", 5);
        breadBuilding.AwardTradeOxygen();
        Assert.AreEqual(35, village.CurrentOxygen);
        Assert.AreEqual(30, village.CurrentEnergy);
    }

    [TestCase(1, 100, 6)]
    [TestCase(1, 10, 6)]
    [TestCase(2, 100, 3)]
    [TestCase(2, 10, 3)]
    [TestCase(2, 300, 3)]
    public void DeliveryRefillsExactLevelFractionAndCapsAtFull(int level, int capacity, int deliveriesToFill)
    {
        Set(breadBuilding, "level", level);
        ((Building.LevelDefinition)Call(breadBuilding, "GetDefinitionForLevel", level)).boxStockCapacity = capacity;
        Set(breadBuilding, "currentBoxStock", 0f);
        Set(breadBuilding, "isWorking", false);
        Slider gauge = AttachGauge(breadBuilding);
        Assert.IsTrue(breadBuilding.TryReceiveDelivery());
        Assert.That(breadBuilding.CurrentSalary, Is.EqualTo(capacity / (float)deliveriesToFill).Within(0.0001f));
        Assert.That(gauge.value, Is.EqualTo(1f / deliveriesToFill).Within(0.00001f));
        Assert.IsTrue(breadBuilding.IsWorking);
        for (int i = 1; i < deliveriesToFill; i++)
        {
            Assert.IsTrue(breadBuilding.TryReceiveDelivery());
            Assert.That(gauge.value, Is.EqualTo((i + 1f) / deliveriesToFill).Within(0.00001f));
        }
        Assert.AreEqual((float)capacity, breadBuilding.CurrentSalary);
        Assert.IsTrue(breadBuilding.TryReceiveDelivery());
        Assert.AreEqual((float)capacity, breadBuilding.CurrentSalary);
        Assert.AreEqual(30, village.CurrentEnergy);
        Assert.AreEqual(30, village.CurrentOxygen);
    }

    [Test]
    public void StoreRecoversLostSlotCachesAndUnlocksCompletedBuildingWithoutDuplicatingViews()
    {
        StoreIn store = Root("ReloadedStore").AddComponent<StoreIn>();
        Set(store, "receiptPrefab", receipt.gameObject);
        Transform position = new GameObject("ResourcePosition").transform;
        position.SetParent(store.transform, false);
        Transform pricePosition = new GameObject("PricePosition").transform;
        pricePosition.SetParent(store.transform, false);
        GameObject padlockPrefab = Root("Padlock");
        Set(store, "padlockPrefab", padlockPrefab);
        var slot = new StoreIn.ResourceSlot { resourcePrefab = bread.gameObject, resourcePosition = position, pricePosition = pricePosition };
        Set(store, "resources", new List<StoreIn.ResourceSlot> { slot });
        Set(breadBuilding, "isPlaced", false);
        store.Initialize();
        GameObject resource = position.GetComponentInChildren<ResourceItem>(true).gameObject;
        GameObject padlock = position.GetComponentsInChildren<Transform>(true).Single(child => child.name == "Padlock(Clone)").gameObject;
        SpriteNumberLabel price = pricePosition.GetComponentInChildren<SpriteNumberLabel>();
        Assert.IsTrue(padlock.activeSelf);
        Assert.AreEqual("?", price.Value);
        int children = position.childCount;
        foreach (string field in new[] { "definition", "resource", "padlock", "price" }) Set(slot, field, null);
        Set(breadBuilding, "isPlaced", true);
        Call(store, "OnEnable");
        Assert.IsTrue(resource.activeSelf);
        Assert.IsFalse(padlock.activeSelf);
        Assert.AreEqual("3", price.Value);
        Assert.AreEqual(children, position.childCount);
        Assert.AreSame(resource, position.GetComponentInChildren<ResourceItem>(true).gameObject);
        Assert.AreSame(price, pricePosition.GetComponentInChildren<SpriteNumberLabel>());
        Set(breadBuilding, "isPlaced", false);
        Call(store, "RefreshAvailability");
        Assert.IsTrue(padlock.activeSelf);
        Assert.IsFalse(resource.activeSelf);
        Assert.AreEqual("?", price.Value);
    }

    [Test]
    public void NumberLabelRebuildRemovesOrphanedGlyphsAfterReload()
    {
        GameObject root = SpriteNumberLabel.Create(Root("NumberPosition").transform, new Sprite[10], 1f);
        SpriteNumberLabel label = root.GetComponent<SpriteNumberLabel>();
        label.SetValue("?");
        new GameObject("OrphanedGlyph").transform.SetParent(root.transform, false);
        label.Configure(new Sprite[10], 1f);
        label.SetValue("3");
        Assert.AreEqual(1, root.transform.childCount);
        Assert.AreEqual("3", root.transform.GetChild(0).name);
    }

    [TestCase("BreadResource", "Bread")]
    [TestCase("ChickenResource", "ChickenTruck")]
    [TestCase("CoffeeResource", "Coffee")]
    [TestCase("FruitResource", "Fruit")]
    [TestCase("IcecreamResource", "Icecream")]
    [TestCase("PharmacyResource", "Pharmacy")]
    [TestCase("SushiResource", "Fish")]
    public void ResourceAndCompletedBoxReferenceTheSameBuildingPrefabRoot(string resourceName, string buildingName)
    {
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Tycoon/" + resourceName + ".prefab");
        GameObject expected = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Village/" + buildingName + ".prefab");
        ResourceItem resource = prefab.GetComponent<ResourceItem>();
        var serialized = new UnityEditor.SerializedObject(resource);
        Assert.AreSame(expected, serialized.FindProperty("buildingPrefab").objectReferenceValue);
        Assert.AreSame(expected.GetComponent<Building>(), resource.BuildingPrefab);
        var completed = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Tycoon/boxComplete.prefab").GetComponent<boxComplete>();
        var mappings = new UnityEditor.SerializedObject(completed).FindProperty("receiptPhotos");
        int matching = 0;
        for (int i = 0; i < mappings.arraySize; i++)
            if (mappings.GetArrayElementAtIndex(i).FindPropertyRelative("buildingPrefab").objectReferenceValue == expected) matching++;
        Assert.AreEqual(1, matching);
    }

    [Test]
    public void ResourceAndBoxBuildingLinksSurviveReplacingTheBuildingComponent()
    {
        GameObject root = breadBuilding.gameObject;
        var mapping = new boxComplete.PhotoMapping { buildingPrefab = root };
        Object.DestroyImmediate(breadBuilding);
        Assert.IsNull(bread.BuildingPrefab);
        breadBuilding = root.AddComponent<Building>();
        Assert.AreSame(breadBuilding, bread.BuildingPrefab);
        Assert.AreSame(breadBuilding, mapping.BuildingPrefab);
        Assert.IsTrue(receipt.TryAdd(bread, village));
    }

    [Test]
    public void GaugeSaveLoadsExistingIntegerValuesAndPreservesFractionalValues()
    {
        var state = JsonUtility.FromJson<VillageManagement.BuildingState>("{\"currentSalary\":37,\"maxSalary\":100}");
        Assert.AreEqual(37f, state.currentSalary);
        state.currentSalary = 100f / 3f;
        var restored = JsonUtility.FromJson<VillageManagement.BuildingState>(JsonUtility.ToJson(state));
        Assert.That(restored.currentSalary, Is.EqualTo(state.currentSalary).Within(0.0001f));
        Assert.AreEqual(100, restored.maxSalary);
    }

    [Test]
    public void ExistingBuildingPrefabKeepsGaugeAndMigratesTradeReward()
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Village/Bread.prefab");
        Assert.IsNotNull(prefab);
        Building building = prefab.GetComponent<Building>();
        Assert.AreEqual(100f, building.CurrentSalary);
        Assert.AreEqual(5, building.OxygenValue);
        var serialized = new UnityEditor.SerializedObject(building);
        var icon = serialized.FindProperty("exclamationPrefab").objectReferenceValue;
        Assert.AreEqual("Assets/Prefabs/Village/OxygenTradeIcon.prefab", UnityEditor.AssetDatabase.GetAssetPath(icon));
    }

    [Test]
    public void ReceiptLocksToFirstResourceAndDoesNotChargeForOverflow()
    {
        ResourceItem other = Root("CoffeeResource").AddComponent<ResourceItem>();
        Set(other, "buildingPrefab", Building("Coffee").gameObject);
        Assert.IsTrue(receipt.TryAdd(bread, village));
        Assert.IsFalse(receipt.TryAdd(other, village));
        Assert.AreEqual(27, village.CurrentEnergy);
        for (int i = 0; i < 3; i++) Assert.IsTrue(receipt.TryAdd(bread, village));
        Assert.IsTrue(receipt.IsFull);
        Assert.IsFalse(receipt.TryAdd(bread, village));
        Assert.AreEqual(18, village.CurrentEnergy);
        Assert.AreEqual(30, village.CurrentOxygen);
        Assert.AreEqual(4, receipt.Quantity);
    }

    [Test]
    public void PackedSnapshotSurvivesReceiptClearAndPanelHide()
    {
        FillReceipt();
        ReceiptData data = receipt.Snapshot();
        receipt.gameObject.SetActive(false);
        Assert.AreEqual(4, receipt.Quantity);
        receipt.Clear();
        Assert.AreEqual(0, receipt.Quantity);
        Assert.IsFalse(receipt.IsFull);
        Assert.AreEqual(4, data.Lines[0].quantity);
        Assert.AreEqual(breadBuilding.BuildingId, data.Lines[0].buildingId);
    }

    [Test]
    public void CapacityReservationsPreventConcurrentPackingAndCountUniquePositions()
    {
        BoxIn inventory = Inventory(1);
        Transform slot = inventory.transform.GetChild(0);
        Set(inventory, "boxCompletePositions", new List<Transform> { slot, null, slot });
        Assert.AreEqual(1, inventory.Capacity);
        Assert.IsTrue(inventory.TryReserve(out object token));
        Assert.IsTrue(inventory.IsFull);
        Assert.AreEqual(0, inventory.Count);
        Assert.IsFalse(inventory.TryReserve(out _));
        Assert.IsFalse(inventory.CommitReservation(new object(), Order()));
        Assert.IsTrue(inventory.CommitReservation(token, Order()));
        Assert.AreEqual(1, inventory.Count);
        Assert.IsFalse(inventory.CommitReservation(token, Order()));
    }

    [Test]
    public void ReleasingReservationAllowsAnotherPack()
    {
        BoxIn inventory = Inventory(1);
        Assert.IsTrue(inventory.TryReserve(out object first));
        inventory.ReleaseReservation(first);
        Assert.IsFalse(inventory.IsFull);
        Assert.IsTrue(inventory.TryReserve(out object next));
        Assert.AreNotSame(first, next);
    }

    [Test]
    public void PackingCommitsOrderAndResetsReceipt()
    {
        FillReceipt();
        BoxIn inventory = Inventory(1);
        Assert.IsTrue(inventory.TryReserve(out object token));
        StoreIn store = Root("StoreIn").AddComponent<StoreIn>();
        Set(store, "receipt", receipt);
        Set(store, "reservedInventory", inventory);
        Set(store, "reservation", token);
        Set(store, "packing", true);
        Set(store, "packAnimationSeconds", 0f);
        Set(store, "shrinkSeconds", 0f);
        Set(store, "packingBox", Root("PackingBox"));
        IEnumerator pack = (IEnumerator)Call(store, "Pack", receipt.Snapshot());
        while (pack.MoveNext()) { }
        Assert.AreEqual(1, inventory.Count);
        Assert.AreEqual(0, receipt.Quantity);
        Assert.IsFalse(store.IsPacking);
    }

    [Test]
    public void FullInventoryLeavesReceiptAndEnergyUntouched()
    {
        FillReceipt();
        BoxIn inventory = Inventory(1);
        Assert.IsTrue(inventory.TryReserve(out object token));
        Assert.IsTrue(inventory.CommitReservation(token, Order()));
        StoreIn store = Root("StoreIn").AddComponent<StoreIn>();
        Set(store, "receipt", receipt);
        Set(store, "boxIn", inventory);
        Set(store, "packingBox", Root("PackingBox"));
        Assert.IsFalse(store.TryPack());
        Assert.AreEqual(4, receipt.Quantity);
        Assert.AreEqual(18, village.CurrentEnergy);
        Assert.AreEqual(30, village.CurrentOxygen);
    }

    [Test]
    public void FailedAndCancelledDeliveryRestoreSameEntry()
    {
        Camera camera = Root("Camera").AddComponent<Camera>();
        camera.tag = "MainCamera";
        camera.transform.position = new Vector3(0f, 0f, -10f);
        BoxIn inventory = Inventory(1);
        inventory.transform.localScale = new Vector3(0.7f, 0.8f, 1f);
        inventory.TryReserve(out object token);
        inventory.CommitReservation(token, Order());
        boxComplete source = inventory.GetComponentInChildren<boxComplete>();
        source.transform.localScale = new Vector3(1.2f, 1.3f, 1f);
        source.transform.localPosition = new Vector3(0.1f, 0.2f, 0f);
        Transform originalParent = source.transform.parent;
        Vector3 originalPosition = source.transform.localPosition;
        Vector3 originalScale = source.transform.localScale;
        Vector3 worldScale = source.transform.lossyScale;
        ReceiptData data = source.Data;
        Transform photo = new GameObject("ReceiptPhoto", typeof(SpriteRenderer)).transform;
        photo.SetParent(source.transform, false);
        photo.localPosition = new Vector3(0.2f, 0.4f, 0f);
        photo.localScale = new Vector3(0.5f, 0.6f, 1f);
        Vector3 photoPosition = photo.localPosition;
        Vector3 photoWorldScale = photo.lossyScale;
        int packedBoxCount = Object.FindObjectsByType<PackedBox>(FindObjectsSortMode.None).Length;
        Vector2 screen = camera.WorldToScreenPoint(new Vector3(50f, 50f));
        Assert.IsTrue(inventory.BeginDelivery(source, screen, -1));
        Assert.IsFalse(inventory.gameObject.activeSelf);
        Assert.IsTrue(source.gameObject.activeInHierarchy);
        Assert.IsTrue(photo.gameObject.activeInHierarchy);
        Assert.IsFalse(source.transform.IsChildOf(inventory.transform));
        Assert.AreSame(source.transform, photo.parent);
        Assert.AreSame(data, source.Data);
        Assert.Less(Vector3.Distance(worldScale * 1.1f, source.transform.lossyScale), 0.0001f);
        Assert.Less(Vector3.Distance(photoWorldScale * 1.1f, photo.lossyScale), 0.0001f);
        Assert.AreEqual(photoPosition, photo.localPosition);
        Assert.AreEqual(packedBoxCount, Object.FindObjectsByType<PackedBox>(FindObjectsSortMode.None).Length);
        Vector2 movedScreen = screen + new Vector2(35f, -20f);
        var move = (System.Action<Vector2>)typeof(DeliveryRuntime).GetField("move", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(DeliveryRuntime.Ensure());
        move(movedScreen);
        Assert.Less(Vector2.Distance(movedScreen, camera.WorldToScreenPoint(source.transform.position)), 0.1f);
        Call(DeliveryRuntime.Ensure(), "End", false);
        Assert.IsTrue(inventory.gameObject.activeSelf);
        Assert.AreEqual(1, inventory.Count);
        Assert.AreSame(source, inventory.GetComponentInChildren<boxComplete>());
        Assert.AreSame(originalParent, source.transform.parent);
        Assert.AreEqual(originalPosition, source.transform.localPosition);
        Assert.AreEqual(originalScale, source.transform.localScale);
        Assert.IsNull(source.GetComponent<UnityEngine.Rendering.SortingGroup>());
        Assert.IsTrue(inventory.BeginDelivery(source, screen, -1));
        Assert.Less(Vector3.Distance(worldScale * 1.1f, source.transform.lossyScale), 0.0001f);
        Call(DeliveryRuntime.Ensure(), "End", true);
        Assert.IsTrue(inventory.gameObject.activeSelf);
        Assert.AreEqual(1, inventory.Count);
        Assert.AreEqual(originalScale, source.transform.localScale);
        Assert.AreSame(source.transform, photo.parent);
        Assert.AreEqual(photoPosition, photo.localPosition);
        Assert.AreEqual(0f, breadBuilding.CurrentSalary);
    }

    [Test]
    public void BusyDragRestoresCompletedBoxWithoutHidingInventory()
    {
        BoxIn inventory = Inventory(1);
        inventory.TryReserve(out object token);
        inventory.CommitReservation(token, Order());
        boxComplete source = inventory.GetComponentInChildren<boxComplete>();
        Transform parent = source.transform.parent;
        Vector3 scale = source.transform.localScale;
        GameObject otherOwner = Root("OtherDrag");
        Assert.IsTrue(DeliveryRuntime.Ensure().BeginDrag(otherOwner, Vector2.zero, -1, position => { }, (position, cancelled) => { }));
        Assert.IsFalse(inventory.BeginDelivery(source, Vector2.zero, -1));
        Assert.AreSame(parent, source.transform.parent);
        Assert.AreEqual(scale, source.transform.localScale);
        Assert.IsTrue(inventory.gameObject.activeSelf);
        Assert.IsFalse(inventory.IsDelivering);
    }

    [TestCase(1, 100, 6)]
    [TestCase(2, 300, 3)]
    public void SuccessfulDeliveryShrinksThenShowsDeliveredPrefabAndRefillsOnlyAfterFade(int level, int capacity, int divisor)
    {
        Set(breadBuilding, "level", level);
        ((Building.LevelDefinition)Call(breadBuilding, "GetDefinitionForLevel", level)).boxStockCapacity = capacity;
        Slider gauge = AttachGauge(breadBuilding);
        Camera camera = Root("Camera").AddComponent<Camera>();
        camera.tag = "MainCamera";
        camera.transform.position = new Vector3(0f, 0f, -10f);
        breadBuilding.gameObject.AddComponent<BoxCollider2D>().size = new Vector2(2f, 2f);
        Transform lineTwo = new GameObject("RegisteredLineTwoPoint").transform;
        lineTwo.SetParent(breadBuilding.transform, false);
        lineTwo.localPosition = new Vector3(2f, 1f, 0f);
        Set(breadBuilding, "line2Point", lineTwo);
        BoxIn inventory = Inventory(2);
        inventory.TryReserve(out object first);
        inventory.CommitReservation(first, Order());
        inventory.TryReserve(out object second);
        inventory.CommitReservation(second, Order());
        boxComplete[] entries = inventory.GetComponentsInChildren<boxComplete>();
        Transform firstPosition = entries[0].transform.parent;
        int deliveries = 0;
        inventory.Delivered += (data, building) => { deliveries++; Assert.AreSame(breadBuilding, building); };
        Vector2 screen = camera.WorldToScreenPoint(breadBuilding.transform.position);
        Assert.IsTrue(inventory.BeginDelivery(entries[0], screen, -1));
        Physics2D.SyncTransforms();
        Call(DeliveryRuntime.Ensure(), "End", false);
        Assert.AreEqual(0, deliveries);
        Assert.AreEqual(1, inventory.Count);
        Assert.AreSame(firstPosition, entries[1].transform.parent);
        Assert.IsTrue(inventory.gameObject.activeSelf);
        Assert.IsTrue(entries[0].gameObject.activeInHierarchy);
        Assert.AreEqual(0f, breadBuilding.CurrentSalary);
        DeliveryArrival arrival = breadBuilding.GetComponentInChildren<DeliveryArrival>();
        Assert.IsNotNull(arrival);
        Vector3 initialScale = entries[0].transform.localScale;
        Call(arrival, "Advance", 0.125f);
        Assert.Less(Vector3.Distance(initialScale * 0.5f, entries[0].transform.localScale), 0.00001f);
        Assert.AreEqual(0, lineTwo.childCount);
        Call(arrival, "Advance", 0.125f);
        Assert.IsTrue(entries[0] == null);
        Assert.AreEqual(1, lineTwo.childCount);
        GameObject delivered = lineTwo.GetChild(0).gameObject;
        Assert.AreEqual("DeliveredBoxTemplate(Clone)", delivered.name);
        Assert.IsNull(delivered.GetComponent<boxComplete>());
        Assert.AreEqual(Vector3.zero, delivered.transform.localPosition);
        SpriteRenderer renderer = delivered.GetComponent<SpriteRenderer>();
        Assert.AreEqual(0.7f, renderer.color.a, 0.00001f);
        Call(arrival, "Advance", 3f);
        Assert.AreEqual(0.7f, renderer.color.a, 0.00001f);
        Assert.AreEqual(0f, breadBuilding.CurrentSalary);
        Call(arrival, "Advance", 0.5f);
        Assert.AreEqual(0.35f, renderer.color.a, 0.00001f);
        Assert.AreEqual(0f, breadBuilding.CurrentSalary);
        Assert.AreEqual(0, deliveries);
        Call(arrival, "Advance", 0.5f);
        Assert.IsTrue(delivered == null);
        Assert.IsTrue(arrival == null);
        Assert.AreEqual(1, deliveries);
        Assert.That(breadBuilding.CurrentSalary, Is.EqualTo(breadBuilding.MaxSalary / (float)divisor).Within(0.0001f));
        Assert.That(gauge.value, Is.EqualTo(1f / divisor).Within(0.00001f));
    }

    [Test]
    public void DeliveryRejectsDifferentBuildingOrUnplacedBuilding()
    {
        ReceiptData order = Order();
        Assert.IsFalse(order.CanDeliverTo(Building("Coffee")));
        Assert.IsTrue(order.CanDeliverTo(breadBuilding));
        Set(breadBuilding, "isPlaced", false);
        Assert.IsFalse(order.CanDeliverTo(breadBuilding));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ArrivalCompletesAfterInventoryClosesOrIsDestroyedAndWaitsForBuildingReadiness(bool destroyInventory)
    {
        Camera camera = Root("Camera").AddComponent<Camera>(); camera.tag = "MainCamera";
        camera.transform.position = new Vector3(0f, 0f, -10f);
        breadBuilding.gameObject.AddComponent<BoxCollider2D>().size = Vector2.one * 2f;
        BoxIn inventory = Inventory(1);
        inventory.TryReserve(out object token); inventory.CommitReservation(token, Order());
        boxComplete source = inventory.GetComponentInChildren<boxComplete>();
        Assert.IsTrue(inventory.BeginDelivery(source, camera.WorldToScreenPoint(breadBuilding.transform.position), -1));
        Physics2D.SyncTransforms(); Call(DeliveryRuntime.Ensure(), "End", false);
        DeliveryArrival arrival = breadBuilding.GetComponentInChildren<DeliveryArrival>();
        Assert.IsNotNull(arrival);
        if (destroyInventory) Object.DestroyImmediate(inventory.gameObject);
        else inventory.Close();
        Set(breadBuilding, "isPlaced", false);
        Call(arrival, "Advance", 4.25f);
        Assert.AreEqual(0f, breadBuilding.CurrentSalary);
        Assert.IsNull(typeof(DeliveryArrival).GetField("delivered", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(arrival));
        Set(breadBuilding, "isPlaced", true);
        Call(arrival, "Advance", 0f);
        Assert.That(breadBuilding.CurrentSalary, Is.EqualTo(breadBuilding.MaxSalary / 6f).Within(0.0001f));
        Assert.IsTrue(arrival == null);
    }

    [Test]
    public void MissingDeliveredPrefabRestoresTheInventoryBoxWithoutChargingTheBuilding()
    {
        Camera camera = Root("Camera").AddComponent<Camera>(); camera.tag = "MainCamera";
        camera.transform.position = new Vector3(0f, 0f, -10f);
        breadBuilding.gameObject.AddComponent<BoxCollider2D>().size = Vector2.one * 2f;
        BoxIn inventory = Inventory(1); Set(inventory, "deliveredBoxPrefab", null);
        inventory.TryReserve(out object token); inventory.CommitReservation(token, Order());
        boxComplete source = inventory.GetComponentInChildren<boxComplete>();
        Transform parent = source.transform.parent; Vector3 scale = source.transform.localScale;
        Assert.IsTrue(inventory.BeginDelivery(source, camera.WorldToScreenPoint(breadBuilding.transform.position), -1));
        Physics2D.SyncTransforms(); Call(DeliveryRuntime.Ensure(), "End", false);
        Assert.AreEqual(1, inventory.Count); Assert.AreSame(parent, source.transform.parent);
        Assert.AreEqual(scale, source.transform.localScale); Assert.IsTrue(inventory.gameObject.activeSelf);
        Assert.AreEqual(0f, breadBuilding.CurrentSalary);
        Assert.IsNull(breadBuilding.GetComponentInChildren<DeliveryArrival>());
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ShakeKeepsVisualCenterFixedAndRestoresTheAuthoredPose(bool boxWobble)
    {
        GameObject view = MotionSprite();
        Transform parent = Root("MotionParent").transform;
        parent.localScale = new Vector3(1.4f, 0.8f, 1f);
        parent.rotation = Quaternion.Euler(0f, 0f, 23f);
        view.transform.SetParent(parent, false);
        view.transform.localPosition = new Vector3(2f, -1f, 0f);
        view.transform.localRotation = Quaternion.Euler(0f, 0f, 17f);
        view.transform.localScale = new Vector3(1.2f, 0.9f, 1f);
        Vector3 position = view.transform.localPosition;
        Quaternion rotation = view.transform.localRotation;
        Vector3 scale = view.transform.localScale;
        Vector3 center = view.GetComponent<Renderer>().bounds.center;
        if (boxWobble) DeliveryMotion.WobbleBox(view.transform);
        else DeliveryMotion.ShakeResource(view.transform);
        DeliveryMotion motion = view.GetComponent<DeliveryMotion>();
        Call(motion, "Advance", 0.025f);
        Assert.Greater(Quaternion.Angle(rotation, view.transform.localRotation), 0.1f);
        Assert.Less(Vector3.Distance(center, view.GetComponent<Renderer>().bounds.center), 0.00001f);
        Assert.AreEqual(scale, view.transform.localScale);
        Call(motion, "Advance", boxWobble ? 0.325f : 0.275f);
        Assert.IsFalse(motion.IsPlaying);
        Assert.AreEqual(position, view.transform.localPosition);
        Assert.AreEqual(rotation, view.transform.localRotation);
        Assert.AreEqual(scale, view.transform.localScale);
    }

    [Test]
    public void ReceiptEntranceStartsOneUnitAboveAndRepeatedOpensDoNotAccumulateOffsets()
    {
        Transform target = Root("AnimatedReceipt").transform;
        target.localPosition = new Vector3(2f, -3f, 0.5f);
        Vector3 rest = target.localPosition;
        DeliveryMotion.SlideReceipt(target);
        DeliveryMotion motion = target.GetComponent<DeliveryMotion>();
        Assert.AreEqual(rest + Vector3.up, target.localPosition);
        Call(motion, "Advance", 0.5f);
        Assert.Greater(target.localPosition.y, rest.y);
        Assert.Less(target.localPosition.y, rest.y + 1f);
        DeliveryMotion.SlideReceipt(target);
        Assert.AreEqual(rest + Vector3.up, target.localPosition);
        Call(motion, "Advance", 1f);
        Assert.IsFalse(motion.IsPlaying);
        Assert.AreEqual(rest, target.localPosition);
        DeliveryMotion.SlideReceipt(target);
        Call(motion, "Advance", 0.2f);
        DeliveryMotion.Reset(target);
        Assert.AreEqual(rest, target.localPosition);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void BackgroundEntranceRisesHalfAUnitAndReopensWithoutDrifting(bool useUi)
    {
        var root = new GameObject("BackgroundUp", useUi ? typeof(RectTransform) : typeof(Transform));
        roots.Add(root);
        Transform target = root.transform;
        target.localPosition = new Vector3(2f, 2.11f, 0.5f);
        Vector3 rest = target.localPosition;
        DeliveryMotion.SlideBackgroundUp(target);
        DeliveryMotion motion = root.GetComponent<DeliveryMotion>();
        Assert.AreEqual(rest + Vector3.down * 0.5f, target.localPosition);
        Call(motion, "Advance", 0.5f);
        Assert.That(target.localPosition.y, Is.EqualTo(rest.y - 0.0625f).Within(0.00001f));
        DeliveryMotion.SlideBackgroundUp(target);
        Assert.AreEqual(rest + Vector3.down * 0.5f, target.localPosition);
        Call(motion, "Advance", 1f);
        Assert.IsFalse(motion.IsPlaying);
        Assert.AreEqual(rest, target.localPosition);
        DeliveryMotion.SlideBackgroundUp(target);
        Call(motion, "Advance", 0.2f);
        Call(motion, "OnDisable");
        Assert.AreEqual(rest, target.localPosition);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PressingResourceShowsFeedbackWithoutChargingBeforeDrop(bool affordable)
    {
        StoreIn store = StorePanel();
        village.SaveData.currentEnergy = affordable ? 30 : 0;
        var instance = typeof(VillageManagement).GetProperty("Instance");
        VillageManagement previous = VillageManagement.Instance;
        instance.SetValue(null, village);
        try
        {
            bread.Bind(store, bread);
            Call(bread, "BeginDrag", Vector2.zero, -1);
            GameObject preview = (GameObject)typeof(ResourceItem).GetField("dragVisual", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(bread);
            DeliveryMotion shake = bread.GetComponent<DeliveryMotion>();
            Assert.AreEqual(affordable, preview != null);
            Assert.IsTrue(shake != null && shake.IsPlaying);
            if (preview != null)
            {
                DeliveryMotion previewMotion = preview.GetComponent<DeliveryMotion>();
                Assert.IsTrue(previewMotion == null || !previewMotion.IsPlaying);
                Call(DeliveryRuntime.Ensure(), "End", true);
            }
            Assert.AreEqual(0, receipt.Quantity);
            Assert.AreEqual(affordable ? 30 : 0, village.CurrentEnergy);
        }
        finally { instance.SetValue(null, previous); }
    }

    [TestCase(false, -1)]
    [TestCase(false, 7)]
    [TestCase(true, -1)]
    [TestCase(true, 7)]
    public void ResourceDragPreservesGrabPointSmoothsWithoutWaitingAndReusesPreview(bool overlay, int finger)
    {
        StoreIn store = StorePanel();
        Camera camera = Root("DragCamera").AddComponent<Camera>();
        camera.tag = "MainCamera";
        camera.orthographic = true;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        if (overlay)
        {
            var canvas = new GameObject("DragCanvas", typeof(RectTransform), typeof(Canvas));
            roots.Add(canvas);
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            bread.transform.SetParent(canvas.transform, false);
            bread.transform.localPosition = new Vector3(200f, 100f, 0f);
        }
        else bread.transform.position = new Vector3(2f, 1f, 0f);
        var instance = typeof(VillageManagement).GetProperty("Instance");
        VillageManagement previous = VillageManagement.Instance;
        instance.SetValue(null, village);
        try
        {
            bread.Bind(store, bread);
            Transform prepared = ((GameObject)typeof(ResourceItem).GetField("preparedDragVisual", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(bread)).transform;
            Assert.IsNotNull(prepared, "Prepare the visual before the first press");
            Assert.IsFalse(prepared.gameObject.activeSelf);
            Vector3 original = bread.transform.position;
            Vector2 start = overlay ? (Vector2)original : (Vector2)camera.WorldToScreenPoint(original);
            Vector2 press = start + new Vector2(12f, -8f);
            Call(bread, "BeginDrag", press, finger);
            Assert.AreEqual(original, prepared.position, "Pressing off-center must not snap the pivot to the pointer");
            Call(bread, "MoveDragPreview", press + new Vector2(120f, 60f));
            Call(bread, "AdvanceDrag", 1f / 60f);
            Vector2 first = overlay ? (Vector2)prepared.position : (Vector2)camera.WorldToScreenPoint(prepared.position);
            Assert.Greater(first.x, start.x);
            Assert.Less(first.x, start.x + 120f);
            Assert.Greater(first.y, start.y);
            Assert.Less(first.y, start.y + 60f);
            Vector3 firstPose = prepared.position;
            Call(bread, "AdvanceDrag", 0.2f);
            Vector2 settled = overlay ? (Vector2)prepared.position : (Vector2)camera.WorldToScreenPoint(prepared.position);
            Assert.Less(Vector2.Distance(start + new Vector2(120f, 60f), settled), 0.1f);
            Assert.AreEqual(30, village.CurrentEnergy);
            Assert.AreEqual(0, receipt.Quantity);
            Call(DeliveryRuntime.Ensure(), "End", true);
            Assert.IsFalse(prepared.gameObject.activeSelf);
            Assert.AreSame(bread.transform.parent, prepared.parent);

            Call(bread, "BeginDrag", press, finger);
            Assert.AreSame(prepared.gameObject, typeof(ResourceItem).GetField("dragVisual", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(bread));
            Assert.AreEqual(original, prepared.position);
            Call(bread, "MoveDragPreview", press + new Vector2(120f, 60f));
            Call(bread, "AdvanceDrag", 1f / 120f);
            Call(bread, "AdvanceDrag", 1f / 120f);
            Assert.Less(Vector3.Distance(firstPose, prepared.position), 0.0001f, "Follow speed should be independent of frame rate");
            store.Close();
            // Edit-mode objects do not receive play-mode lifecycle callbacks.
            bread.gameObject.SetActive(false);
            Call(bread, "OnDisable");
            Assert.IsFalse(prepared.gameObject.activeSelf);
            Assert.AreSame(bread.transform.parent, prepared.parent);
            Assert.AreEqual(30, village.CurrentEnergy);
        }
        finally { instance.SetValue(null, previous); }
    }

    [Test]
    public void OnlySuccessfulResourceDropsWobbleThePackingBox()
    {
        Camera camera = Root("Camera").AddComponent<Camera>();
        camera.tag = "MainCamera";
        camera.transform.position = new Vector3(0f, 0f, -10f);
        receipt.gameObject.AddComponent<BoxCollider2D>().size = Vector2.one * 2f;
        StoreIn store = Root("StoreIn").AddComponent<StoreIn>();
        Set(store, "receiptPrefab", receipt.gameObject);
        Set(store, "boxPrefab", MotionSprite());
        store.Initialize();
        GameObject box = (GameObject)typeof(StoreIn).GetField("packingBox", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(store);
        var instance = typeof(VillageManagement).GetProperty("Instance");
        VillageManagement previous = VillageManagement.Instance;
        instance.SetValue(null, village);
        try
        {
            Assert.IsFalse(store.TryBuyAt(bread, new Vector2(-10000f, -10000f)));
            Assert.IsNull(box.GetComponent<DeliveryMotion>());
            Assert.IsTrue(store.TryBuyAt(bread, camera.WorldToScreenPoint(receipt.transform.position)));
            DeliveryMotion motion = box.GetComponent<DeliveryMotion>();
            Assert.IsTrue(motion.IsPlaying);
            Assert.AreEqual(1, receipt.Quantity);
            Assert.AreEqual(27, village.CurrentEnergy);
            Call(motion, "Advance", 0.35f);
            for (int i = 0; i < 3; i++) Assert.IsTrue(receipt.TryAdd(bread, village));
            Assert.IsFalse(store.TryBuyAt(bread, camera.WorldToScreenPoint(receipt.transform.position)));
            Assert.IsTrue(motion.IsFullBoxShaking);
        }
        finally { instance.SetValue(null, previous); }
    }

    [Test]
    public void FullBoxShakeRepeatsLeftAndRightWithoutDriftingAndResetRestoresPose()
    {
        Transform box = MotionSprite().transform;
        box.localPosition = new Vector3(2f, -1f, 0.5f);
        box.localRotation = Quaternion.Euler(0f, 0f, 17f);
        Vector3 rest = box.localPosition;
        Quaternion rotation = box.localRotation;
        DeliveryMotion.ShakeFullBox(box);
        DeliveryMotion motion = box.GetComponent<DeliveryMotion>();
        Call(motion, "Advance", 0.175f);
        Assert.Greater(box.localPosition.x, rest.x);
        Vector3 right = box.localPosition;
        DeliveryMotion.ShakeFullBox(box);
        Assert.AreEqual(right, box.localPosition, "Refreshing full state must not restart the shake");
        Call(motion, "Advance", 0.35f);
        Assert.Less(box.localPosition.x, rest.x);
        Call(motion, "Advance", 70f);
        Assert.IsTrue(motion.IsFullBoxShaking);
        Assert.AreEqual(rest.y, box.localPosition.y);
        Assert.AreEqual(rest.z, box.localPosition.z);
        Assert.AreEqual(rotation, box.localRotation);
        DeliveryMotion.Reset(box);
        Assert.IsFalse(motion.IsPlaying);
        Assert.AreEqual(rest, box.localPosition);
        Assert.AreEqual(rotation, box.localRotation);
    }

    [TestCase(1)]
    [TestCase(4)]
    public void FullReceiptShakesUntilPackingAndResumesAfterReopenOrFailedPack(int quantity)
    {
        Set(bread, "requiredQuantity", quantity);
        Camera camera = Root("FullBoxCamera").AddComponent<Camera>();
        camera.tag = "MainCamera";
        camera.transform.position = new Vector3(0f, 0f, -10f);
        receipt.gameObject.AddComponent<BoxCollider2D>().size = Vector2.one * 2f;
        StoreIn store = Root("FullStore").AddComponent<StoreIn>();
        Set(store, "receiptPrefab", receipt.gameObject);
        Set(store, "boxPrefab", MotionSprite());
        store.Initialize();
        GameObject box = (GameObject)typeof(StoreIn).GetField("packingBox", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(store);
        Vector3 rest = box.transform.localPosition;
        var instance = typeof(VillageManagement).GetProperty("Instance");
        VillageManagement previous = VillageManagement.Instance;
        instance.SetValue(null, village);
        try
        {
            for (int i = 0; i < quantity; i++)
                Assert.IsTrue(store.TryBuyAt(bread, camera.WorldToScreenPoint(receipt.transform.position)));
            DeliveryMotion motion = box.GetComponent<DeliveryMotion>();
            Assert.IsTrue(motion.IsFullBoxShaking, "The last purchase must keep the full-state loop");
            Call(motion, "Advance", 2f);
            Assert.IsTrue(motion.IsFullBoxShaking);
            BoxIn inventory = Inventory(1);
            inventory.TryReserve(out object token);
            inventory.CommitReservation(token, Order());
            Set(store, "boxIn", inventory);
            Assert.IsFalse(store.TryPack());
            Assert.IsTrue(motion.IsFullBoxShaking, "Full inventory must leave the reminder running");
            store.Close();
            Call(store, "OnDisable");
            Assert.IsFalse(motion.IsPlaying);
            Assert.AreEqual(rest, box.transform.localPosition);
            store.gameObject.SetActive(true);
            Call(store, "OnEnable");
            Assert.IsTrue(motion.IsFullBoxShaking);
            Set(store, "packing", true);
            Call(store, "RefreshFullBoxMotion");
            Assert.IsFalse(motion.IsPlaying);
            Assert.AreEqual(rest, box.transform.localPosition);
            Set(store, "packAnimationSeconds", 0f);
            Set(store, "shrinkSeconds", 0f);
            IEnumerator pack = (IEnumerator)Call(store, "Pack", receipt.Snapshot());
            while (pack.MoveNext()) { }
            Assert.IsTrue(motion.IsFullBoxShaking, "Failed packing must resume the full reminder");
            receipt.Clear();
            Assert.IsFalse(motion.IsPlaying);
            Assert.AreEqual(rest, box.transform.localPosition);
        }
        finally { instance.SetValue(null, previous); }
    }

    [TestCase(0, -1)]
    [TestCase(0, 7)]
    [TestCase(2, -1)]
    [TestCase(2, 7)]
    public void InsufficientEnergyShakesHorizontallyWithoutDraggingOrSpendingAndRestoresPose(int energy, int finger)
    {
        StoreIn store = StorePanel();
        village.SaveData.currentEnergy = energy;
        var instance = typeof(VillageManagement).GetProperty("Instance");
        VillageManagement previous = VillageManagement.Instance;
        instance.SetValue(null, village);
        try
        {
            bread.Bind(store, bread);
            Vector3 rest = new Vector3(2f, -1f, 0.5f);
            Quaternion rotation = Quaternion.Euler(0f, 0f, 17f);
            bread.transform.localPosition = rest;
            bread.transform.localRotation = rotation;
            Call(bread, "BeginDrag", Vector2.zero, finger);
            DeliveryMotion motion = bread.GetComponent<DeliveryMotion>();
            Assert.IsTrue(motion.IsPlaying);
            Call(motion, "Advance", 0.025f);
            Assert.Greater(bread.transform.localPosition.x, rest.x);
            Call(motion, "Advance", 0.05f);
            Assert.Less(bread.transform.localPosition.x, rest.x);
            Assert.AreEqual(rest.y, bread.transform.localPosition.y);
            Assert.AreEqual(rest.z, bread.transform.localPosition.z);
            Assert.AreEqual(rotation, bread.transform.localRotation);
            Assert.IsNull(typeof(ResourceItem).GetField("dragVisual", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(bread));
            Assert.AreEqual(energy, village.CurrentEnergy);
            Assert.AreEqual(30, village.CurrentOxygen);
            Assert.AreEqual(0, receipt.Quantity);

            Call(bread, "BeginDrag", Vector2.zero, finger);
            Assert.AreEqual(rest, bread.transform.localPosition, "Repeated taps must not move the starting point");
            Call(motion, "Advance", 0.3f);
            Assert.IsFalse(motion.IsPlaying);
            Assert.AreEqual(rest, bread.transform.localPosition);
            village.SaveData.currentEnergy = bread.EnergyPrice;
            Call(bread, "BeginDrag", Vector2.zero, finger);
            Assert.IsNotNull(typeof(ResourceItem).GetField("dragVisual", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(bread));
            Call(DeliveryRuntime.Ensure(), "End", true);
            Assert.AreEqual(bread.EnergyPrice, village.CurrentEnergy);
        }
        finally { instance.SetValue(null, previous); }
    }

    [Test]
    public void StoreAndBoxButtonsSwitchPanelsAndKeepSameButtonToggleWorking()
    {
        StoreIn store = StorePanel();
        BoxIn inventory = Inventory(1);
        inventory.Close();
        Store storeButton = Root("StoreButton").AddComponent<Store>();
        Box boxButton = Root("BoxButton").AddComponent<Box>();
        Set(storeButton, "panel", store);
        Set(boxButton, "panel", inventory);
        store.Close();
        storeButton.Toggle();
        Assert.IsTrue(store.gameObject.activeSelf);
        Assert.IsFalse(inventory.gameObject.activeSelf);
        Assert.IsTrue(receipt.GetComponent<DeliveryMotion>().IsPlaying);
        DeliveryWindows.NotifyUiPressed(boxButton.gameObject);
        boxButton.Toggle();
        Assert.IsFalse(store.gameObject.activeSelf);
        Assert.IsTrue(inventory.gameObject.activeSelf);
        DeliveryWindows.NotifyUiPressed(boxButton.gameObject);
        Assert.IsTrue(inventory.gameObject.activeSelf);
        boxButton.Toggle();
        Assert.IsFalse(inventory.gameObject.activeSelf);
        storeButton.Toggle();
        DeliveryWindows.NotifyUiPressed(storeButton.gameObject);
        Assert.IsTrue(store.gameObject.activeSelf);
        storeButton.Toggle();
        Assert.IsFalse(store.gameObject.activeSelf);
    }

    [Test]
    public void ExternalUiClosesDeliveryWindowsButReceiptAndInventoryContentsDoNot()
    {
        StoreIn store = StorePanel();
        BoxIn inventory = Inventory(1);
        store.Show();
        DeliveryWindows.NotifyUiPressed(receipt.gameObject);
        Assert.IsTrue(store.gameObject.activeSelf);
        GameObject externalButton = Root("OtherUi");
        DeliveryWindows.NotifyUiPressed(externalButton);
        Assert.IsFalse(store.gameObject.activeSelf);
        inventory.Show();
        Transform slot = inventory.transform.GetChild(0);
        DeliveryWindows.NotifyUiPressed(slot.gameObject);
        Assert.IsTrue(inventory.gameObject.activeSelf);
        DeliveryWindows.NotifyUiPressed(externalButton);
        Assert.IsFalse(inventory.gameObject.activeSelf);
    }

    [Test]
    public void OtherWindowOpenedDuringDeliveryStaysOpenAfterCancelledDelivery()
    {
        StoreIn store = StorePanel();
        store.Close();
        BoxIn inventory = Inventory(1);
        inventory.TryReserve(out object token);
        inventory.CommitReservation(token, Order());
        boxComplete source = inventory.GetComponentInChildren<boxComplete>();
        Assert.IsTrue(inventory.BeginDelivery(source, Vector2.zero, -1));
        store.Show();
        Call(DeliveryRuntime.Ensure(), "End", true);
        Assert.IsTrue(store.gameObject.activeSelf);
        Assert.IsFalse(inventory.gameObject.activeSelf);
        Assert.AreEqual(1, inventory.Count);
        inventory.Show();
        Assert.IsTrue(inventory.gameObject.activeSelf);
        Assert.IsFalse(store.gameObject.activeSelf);
    }

    [Test]
    public void OpenStoreBlocksWorldEvenOutsideItsBoundsAndRestoresBuildingInputOnClose()
    {
        StoreIn store = StorePanel();
        Vector2 outside = new Vector2(-10000f, -10000f);
        Assert.IsTrue(DeliveryRuntime.BlocksWorldInput);
        Assert.IsFalse((bool)Call(breadBuilding, "CanStartPointerInteraction", outside));
        GameObject slotRoot = Root("WorldTurretSlot");
        slotRoot.AddComponent<BoxCollider2D>();
        TurretImplementation slot = slotRoot.AddComponent<TurretImplementation>();
        slot.HandleColliderPointerDown();
        Assert.IsFalse((bool)typeof(TurretImplementation).GetField("pointerHeld", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(slot));

        GameObject bankPanel = Root("BankPanel");
        bankPanel.SetActive(false);
        BankUI bankUi = Root("BankUI").AddComponent<BankUI>();
        Set(bankUi, "panel", bankPanel);
        GameObject bankRoot = Root("WorldBank");
        bankRoot.AddComponent<BoxCollider2D>();
        Bank bank = bankRoot.AddComponent<Bank>();
        Set(bank, "bankUI", bankUi);
        Call(bank, "OnMouseUpAsButton");
        Assert.IsFalse(bankPanel.activeSelf, "The bank behind the popup cannot open its own UI");
        Assert.IsTrue(store.gameObject.activeSelf);

        store.Close();
        Assert.IsFalse(DeliveryModal.IsOpen);
        Assert.IsFalse(DeliveryRuntime.BlocksWorldInput);
        Assert.IsTrue((bool)Call(breadBuilding, "CanStartPointerInteraction", outside));
    }

    [Test]
    public void ClosingPopupCannotPassItsGestureOrReleaseThroughToWorld()
    {
        StoreIn store = StorePanel();
        DeliveryRuntime runtime = DeliveryRuntime.Ensure();
        Call(runtime, "Press", new Vector2(-10000f, -10000f), -1);
        store.Close();
        Assert.IsFalse(DeliveryModal.IsOpen);
        Assert.IsTrue(DeliveryRuntime.BlocksWorldInput, "The UI owns the complete gesture after it closes");
        Assert.IsTrue(VillagePointerCapture.HasActiveCapture);
        Call(runtime, "End", false);
        Assert.IsFalse(VillagePointerCapture.HasActiveCapture);
        Assert.IsTrue(DeliveryRuntime.BlocksWorldInput, "Release cannot click a world collider in this frame");
    }

    [Test]
    public void SettingsPanelBlocksWorldUntilHiddenIncludingAncestorDisable()
    {
        GameObject panel = Root("SettingsPanel");
        panel.SetActive(false);
        EditUI settings = Root("Settings").AddComponent<EditUI>();
        Set(settings, "panel", panel);
        settings.Open();
        Assert.IsTrue(DeliveryModal.IsOpen);
        Assert.IsFalse((bool)Call(breadBuilding, "CanStartPointerInteraction", Vector2.zero));
        GameObject parent = Root("SettingsParent");
        panel.transform.SetParent(parent.transform);
        parent.SetActive(false);
        Assert.IsFalse(DeliveryModal.IsOpen);
        parent.SetActive(true);
        Assert.IsTrue(DeliveryModal.IsOpen);
        settings.Close();
        Assert.IsFalse(DeliveryModal.IsOpen);
    }

    [Test]
    public void AllFourWindowsSwitchInEveryDirectionAndKeepTheirInternalUi()
    {
        var property = typeof(VillageManagement).GetProperty("Instance");
        VillageManagement previous = VillageManagement.Instance;
        property.SetValue(null, village);
        try
        {
            GameObject canvasRoot = Root("WindowCanvas");
            canvasRoot.AddComponent<Canvas>();
            GameObject shopRoot = Root("ShopButton"); shopRoot.transform.SetParent(canvasRoot.transform);
            Shop shop = shopRoot.AddComponent<Shop>();
            GameObject playRoot = Root("PlayButton"); playRoot.transform.SetParent(canvasRoot.transform);
            Play play = playRoot.AddComponent<Play>();
            shop.OpenShop();
            play.OpenPlayMenu();
            GameObject shopPanel = ((RectTransform)typeof(Shop).GetField("panelRoot", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(shop)).gameObject;
            GameObject playPanel = ((RectTransform)typeof(Play).GetField("panelRoot", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(play)).gameObject;
            StoreIn store = StorePanel();
            BoxIn inventory = Inventory(1); inventory.Initialize();
            var open = new System.Action[] { shop.OpenShop, play.OpenPlayMenu, store.Show, inventory.Show };
            GameObject[] panels = { shopPanel, playPanel, store.gameObject, inventory.gameObject };
            for (int from = 0; from < panels.Length; from++)
                for (int to = 0; to < panels.Length; to++)
                {
                    open[from](); open[to]();
                    for (int i = 0; i < panels.Length; i++) Assert.AreEqual(i == to, panels[i].activeSelf, from + " -> " + to);
                    DeliveryWindows.NotifyUiPressed(panels[to].transform.GetChild(0).gameObject);
                    Assert.IsTrue(panels[to].activeSelf, "Its own internal UI keeps the window open");
                }
            foreach (var show in open)
            {
                show();
                DeliveryWindows.NotifyUiPressed(Root("AnotherUiButton"));
                foreach (GameObject panel in panels) Assert.IsFalse(panel.activeSelf);
            }
        }
        finally { property.SetValue(null, previous); }
    }

    [Test]
    public void ReapplyingNumberStyleReusesGlyphsAndDetectsChangesInsideSpriteArray()
    {
        Sprite[] digits = new Sprite[10];
        for (int i = 0; i < digits.Length; i++) digits[i] = DigitSprite("Digit" + i);
        GameObject root = SpriteNumberLabel.Create(Root("NumberPosition").transform, digits, 1f);
        SpriteNumberLabel label = root.GetComponent<SpriteNumberLabel>();
        label.SetValue("123");
        GameObject original = root.transform.GetChild(0).gameObject;
        for (int i = 0; i < 100; i++) label.Configure(digits, 1f);
        Assert.AreSame(original, root.transform.GetChild(0).gameObject);
        digits[1] = DigitSprite("ReplacementDigit");
        label.Configure(digits, 1f);
        Assert.AreSame(digits[1], root.transform.GetChild(0).GetComponentInChildren<SpriteRenderer>().sprite);
        Assert.AreEqual("123", label.Value);
    }

    [Test]
    public void PlayReusesUnchangedButtonsAndDefersHiddenListChangesUntilReopen()
    {
        var property = typeof(VillageManagement).GetProperty("Instance");
        VillageManagement previous = VillageManagement.Instance;
        property.SetValue(null, village);
        try
        {
            Play play = Root("Play").AddComponent<Play>();
            Set(play, "arcadeScenes", new List<Play.ArcadeSceneEntry> { new Play.ArcadeSceneEntry() });
            play.OpenPlayMenu();
            var players = (List<Button>)typeof(Play).GetField("playerButtons", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(play);
            var scenes = (List<Button>)typeof(Play).GetField("sceneButtons", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(play);
            Button firstPlayer = players[0]; Button firstScene = scenes[0];
            for (int i = 0; i < 20; i++) Call(play, "HandleSaveDataChanged", village.SaveData);
            Assert.AreSame(firstPlayer, players[0]); Assert.AreSame(firstScene, scenes[0]);
            int originalCount = players.Count;
            play.ClosePlayMenu();
            village.ArcadePlayers[0].available = false;
            Call(play, "HandleSaveDataChanged", village.SaveData);
            Assert.AreSame(firstPlayer, players[0]); Assert.AreEqual(originalCount, players.Count);
            play.OpenPlayMenu();
            Assert.AreEqual(originalCount - 1, players.Count);
            Assert.AreSame(firstScene, scenes[0]);
        }
        finally { property.SetValue(null, previous); }
    }

    private StoreIn StorePanel()
    {
        StoreIn store = Root("StoreIn").AddComponent<StoreIn>();
        receipt.transform.SetParent(store.transform, false);
        Set(store, "receiptPrefab", receipt.gameObject);
        store.Initialize();
        return store;
    }

    private GameObject MotionSprite()
    {
        Texture2D texture = new Texture2D(20, 12);
        testTextures.Add(texture);
        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 20f, 12f), Vector2.zero, 20f, 0, SpriteMeshType.FullRect);
        testSprites.Add(sprite);
        GameObject root = Root("MotionSprite");
        root.AddComponent<SpriteRenderer>().sprite = sprite;
        return root;
    }

    private void FillReceipt() { for (int i = 0; i < 4; i++) Assert.IsTrue(receipt.TryAdd(bread, village)); }
    private static Slider AttachGauge(Building building)
    {
        var canvas = new GameObject("Gauge", typeof(RectTransform), typeof(Canvas));
        canvas.transform.SetParent(building.transform, false);
        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Slider));
        fill.transform.SetParent(canvas.transform, false);
        Set(building, "boxStockGauge", canvas.GetComponent<Canvas>());
        Call(building, "ResolveStatusBar");
        Call(building, "RefreshStatusBar");
        return fill.GetComponent<Slider>();
    }
    private ReceiptData Order() => new ReceiptData(new[] { new ReceiptData.Line { resourceId = bread.ResourceId, buildingId = breadBuilding.BuildingId, quantity = 4 } });
    private GameObject Root(string name) { var root = new GameObject(name); roots.Add(root); return root; }
    private Building Building(string name)
    {
        GameObject root = Root(name);
        new GameObject("BottomAnchor").transform.SetParent(root.transform);
        Building result = root.AddComponent<Building>();
        Set(result, "isPlaced", true);
        return result;
    }
    private BoxIn Inventory(int capacity)
    {
        BoxIn result = Root("BoxIn").AddComponent<BoxIn>();
        var positions = new List<Transform>();
        for (int i = 0; i < capacity; i++)
        {
            Transform slot = new GameObject("Slot" + i).transform;
            slot.SetParent(result.transform);
            positions.Add(slot);
        }
        Set(result, "boxCompletePositions", positions);
        Set(result, "boxCompletePrefab", Root("BoxCompleteTemplate"));
        GameObject delivered = Root("DeliveredBoxTemplate");
        delivered.AddComponent<SpriteRenderer>().color = new Color(1f, 1f, 1f, 0.7f);
        Set(result, "deliveredBoxPrefab", delivered);
        return result;
    }
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
}
#endif
