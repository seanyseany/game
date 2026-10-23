#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Tycoon;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;
using ResourceItem = Tycoon.Resources;

public sealed class TycoonPointerPlayTests
{
    private static string BoxSaveTestPath => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TycoonBoxPersistenceTest.json");

    [UnityTearDown] public IEnumerator Cleanup()
    {
        if (Application.isPlaying) yield return new ExitPlayMode();
        if (System.IO.File.Exists(BoxSaveTestPath)) System.IO.File.Delete(BoxSaveTestPath);
    }
    [UnityTest] public IEnumerator NativeResourceAndBoxesReceivePointerPresses()
    {
        yield return new EnterPlayMode();
        var check=PrepareNativePointerFlow(false);
        yield return null;
        yield return null;
        check(0);
        yield return new WaitForSecondsRealtime(1.1f);
        check(1);
        yield return null;
        yield return null;
        check(2);
    }

    [UnityTest] public IEnumerator LevelTwoGaugesStayUnchangedAfterTenSecondsAndStillRefillByDelivery()
    {
        yield return new EnterPlayMode();
        System.Action check = PrepareNoAutomaticGaugeRefill();
        yield return new WaitForSeconds(10.2f);
        check();
    }

    private static System.Action PrepareNoAutomaticGaugeRefill()
    {
        PrepareNativePointerFlow(false);
        Object.FindFirstObjectByType<StoreIn>().Close();
        Building[] buildings = Object.FindObjectsByType<Building>(FindObjectsSortMode.None);
        Assert.GreaterOrEqual(buildings.Length, 2);
        Building empty = buildings[0];
        Building partial = buildings[1];
        foreach (Building building in new[] { empty, partial })
        {
            Set(building, "level2StockConsumptionIntervalSeconds", 0f);
            Set(building, "stockConsumedPerCustomer", 0);
            building.SetLevel(2);
        }
        empty.SetSalary(0f, 300);
        partial.SetSalary(75f, 300);
        return () =>
        {
            Assert.AreEqual(0f, empty.CurrentSalary, "An empty level-two gauge must not refill automatically");
            Assert.AreEqual(75f, partial.CurrentSalary, "A partially filled gauge must not refill automatically");
            Assert.IsFalse(empty.IsWorking);
            Assert.IsTrue(empty.TryReceiveDelivery());
            Assert.That(empty.CurrentSalary, Is.EqualTo(100f).Within(0.0001f));
            Assert.IsTrue(empty.IsWorking);
        };
    }

    [UnityTest] public IEnumerator ReceiptRestoresPartialAndFullContentsAcrossSceneUnloadsWithoutChargingAgain()
    {
        yield return new EnterPlayMode();
        Scene original = SceneManager.GetActiveScene();
        Scene villageScene = SceneManager.CreateScene("ReceiptSessionVillage");
        SceneManager.SetActiveScene(villageScene);
        StoreIn store = PrepareReceiptSessionStore();
        var definition = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Tycoon/BreadResource.prefab").GetComponent<ResourceItem>();
        Assert.IsTrue(store.CurrentReceipt.TryAdd(definition, VillageManagement.Instance));
        Assert.IsTrue(store.CurrentReceipt.TryAdd(definition, VillageManagement.Instance));
        AssertReceiptSessionContents(store, 2, false);
        store.Close();
        SceneManager.SetActiveScene(original);
        yield return SceneManager.UnloadSceneAsync(villageScene);

        Scene otherScene = SceneManager.CreateScene("ReceiptSessionOther");
        SceneManager.SetActiveScene(otherScene);
        StoreIn otherStore = PrepareReceiptSessionStore();
        AssertReceiptSessionContents(otherStore, 0, false);
        SceneManager.SetActiveScene(original);
        yield return SceneManager.UnloadSceneAsync(otherScene);

        villageScene = SceneManager.CreateScene("ReceiptSessionVillage");
        SceneManager.SetActiveScene(villageScene);
        store = PrepareReceiptSessionStore();
        AssertReceiptSessionContents(store, 2, false);
        Assert.AreEqual(1000, VillageManagement.Instance.CurrentEnergy, "Restoring paid items must not charge again");
        for (int i = 2; i < definition.RequiredQuantity; i++)
            Assert.IsTrue(store.CurrentReceipt.TryAdd(definition, VillageManagement.Instance));
        AssertReceiptSessionContents(store, definition.RequiredQuantity, true);
        SceneManager.SetActiveScene(original);
        yield return SceneManager.UnloadSceneAsync(villageScene);

        villageScene = SceneManager.CreateScene("ReceiptSessionVillage");
        SceneManager.SetActiveScene(villageScene);
        store = PrepareReceiptSessionStore();
        AssertReceiptSessionContents(store, definition.RequiredQuantity, true);
        Assert.AreEqual(1000, VillageManagement.Instance.CurrentEnergy);
        store.CurrentReceipt.Clear();
        SceneManager.SetActiveScene(original);
        yield return SceneManager.UnloadSceneAsync(villageScene);

        villageScene = SceneManager.CreateScene("ReceiptSessionVillage");
        SceneManager.SetActiveScene(villageScene);
        store = PrepareReceiptSessionStore();
        AssertReceiptSessionContents(store, 0, false);
        SceneManager.SetActiveScene(original);
        yield return SceneManager.UnloadSceneAsync(villageScene);
    }

    [UnityTest] public IEnumerator PackedBoxesSurviveSceneChangesAndRestartWhileUnpackedReceiptResetsOnRestart()
    {
        if (System.IO.File.Exists(BoxSaveTestPath)) System.IO.File.Delete(BoxSaveTestPath);
        yield return new EnterPlayMode();
        Scene original = SceneManager.GetActiveScene();
        StoreIn store = PreparePersistentBoxStore(false);
        Scene villageScene = SceneManager.GetActiveScene();
        BoxIn inventory = Object.FindFirstObjectByType<BoxIn>(FindObjectsInactive.Include);
        foreach (string resource in new[] { "BreadResource", "CoffeeResource" })
        {
            ResourceItem definition = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Tycoon/" + resource + ".prefab").GetComponent<ResourceItem>();
            for (int i = 0; i < definition.RequiredQuantity; i++) Assert.IsTrue(store.CurrentReceipt.TryAdd(definition, VillageManagement.Instance));
            Assert.IsTrue(inventory.TryReserve(out object token));
            Assert.IsTrue(inventory.CommitReservation(token, store.CurrentReceipt.Snapshot()));
            store.CurrentReceipt.Clear();
        }
        ResourceItem bread = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Tycoon/BreadResource.prefab").GetComponent<ResourceItem>();
        Assert.IsTrue(store.CurrentReceipt.TryAdd(bread, VillageManagement.Instance));
        Assert.IsTrue(store.CurrentReceipt.TryAdd(bread, VillageManagement.Instance));
        int energy = VillageManagement.Instance.CurrentEnergy;
        store.Close(); store.Show();
        AssertReceiptSessionContents(store, 2, false);
        AssertSavedBoxOrder(inventory, "BreadResource", "CoffeeResource");
        SceneManager.SetActiveScene(original);
        yield return SceneManager.UnloadSceneAsync(villageScene);

        store = PreparePersistentBoxStore(true);
        villageScene = SceneManager.GetActiveScene();
        inventory = Object.FindFirstObjectByType<BoxIn>(FindObjectsInactive.Include);
        AssertReceiptSessionContents(store, 2, false);
        Assert.AreEqual(energy, VillageManagement.Instance.CurrentEnergy, "Restoring a receipt must not charge energy again");
        AssertSavedBoxOrder(inventory, "BreadResource", "CoffeeResource");
        BeginFirstSavedDelivery(inventory);
        Assert.AreEqual(1, inventory.Count);
        Assert.AreEqual(2, ReadSavedBoxCount(), "Keep the backup until the building has actually received its stock");
        SceneManager.SetActiveScene(original);
        yield return SceneManager.UnloadSceneAsync(villageScene);

        store = PreparePersistentBoxStore(true);
        inventory = Object.FindFirstObjectByType<BoxIn>(FindObjectsInactive.Include);
        AssertSavedBoxOrder(inventory, "BreadResource", "CoffeeResource");
        store.Show();
        AssertReceiptSessionContents(store, 2, false);
        yield return new ExitPlayMode();
        yield return new EnterPlayMode();

        store = PreparePersistentBoxStore(true);
        inventory = Object.FindFirstObjectByType<BoxIn>(FindObjectsInactive.Include);
        AssertSavedBoxOrder(inventory, "BreadResource", "CoffeeResource");
        AssertReceiptSessionContents(store, 0, false);
        Building target = BeginFirstSavedDelivery(inventory);
        Call(target.GetComponentInChildren<DeliveryArrival>(), "Advance", 4.3f);
        Assert.That(target.CurrentSalary, Is.EqualTo(100f).Within(0.0001f));
        Assert.AreEqual(1, ReadSavedBoxCount(), "A completed delivery must also be removed from the save file");
        Set(VillageManagement.Instance, "loaded", false);
        VillageManagement.Instance.Load();
        AssertSavedBoxOrder(inventory, "CoffeeResource");
        VillageManagement.Instance.ResetSaveData();
        Assert.AreEqual(0, inventory.Count, "Resetting village progress must clear saved boxes and their visuals");
        Assert.AreEqual(0, ReadSavedBoxCount());
        System.IO.File.WriteAllText(BoxSaveTestPath, "{\"currentEnergy\":1000,\"energyCapacity\":1000}");
        Set(VillageManagement.Instance, "loaded", false);
        VillageManagement.Instance.Load();
        Assert.AreEqual(0, inventory.Count, "Older save files without box inventories must still load");
        LogAssert.NoUnexpectedReceived();
    }

    private static StoreIn PreparePersistentBoxStore(bool load)
    {
        SceneManager.SetActiveScene(SceneManager.CreateScene("BoxPersistenceVillage"));
        PrepareNativePointerFlow(false);
        VillageManagement village = VillageManagement.Instance;
        Set(village, "saveFileName", BoxSaveTestPath);
        Set(village, "autoSaveOnChange", true);
        if (load) { Set(village, "loaded", false); village.Load(); }
        else village.Save();
        return Object.FindFirstObjectByType<StoreIn>();
    }

    private static void AssertSavedBoxOrder(BoxIn inventory, params string[] resources)
    {
        Assert.AreEqual(resources.Length, inventory.Count);
        inventory.Show();
        boxComplete[] boxes = inventory.GetComponentsInChildren<boxComplete>();
        Assert.AreEqual(resources.Length, boxes.Length, "Restoring must not create duplicate boxes");
        var positions = (System.Collections.Generic.List<Transform>)typeof(BoxIn).GetField("boxCompletePositions", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(inventory);
        for (int i = 0; i < resources.Length; i++)
        {
            ResourceItem definition = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Tycoon/" + resources[i] + ".prefab").GetComponent<ResourceItem>();
            Assert.AreEqual(definition.ResourceId, boxes[i].Data.Lines[0].resourceId);
            Assert.AreEqual(definition.BuildingPrefab.BuildingId, boxes[i].Data.Lines[0].buildingId);
            Assert.AreEqual(definition.RequiredQuantity, boxes[i].Data.Lines[0].quantity);
            Assert.AreSame(positions[i], boxes[i].transform.parent);
            var photos = (System.Collections.Generic.List<GameObject>)typeof(boxComplete).GetField("spawnedPhotos", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(boxes[i]);
            Assert.AreEqual(1, photos.Count, "The restored box must include its receipt photo");
            var mappings = (System.Collections.Generic.List<boxComplete.PhotoMapping>)typeof(boxComplete).GetField("receiptPhotos", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(boxes[i]);
            var mapping = mappings.Find(entry => entry.BuildingPrefab.BuildingId == definition.BuildingPrefab.BuildingId);
            Assert.AreEqual(mapping.receiptPhotoPrefab.GetComponent<SpriteRenderer>().sprite, photos[0].GetComponent<SpriteRenderer>().sprite);
        }
    }

    private static Building BeginFirstSavedDelivery(BoxIn inventory)
    {
        inventory.Show();
        boxComplete source = inventory.GetComponentInChildren<boxComplete>();
        Building target = System.Array.Find(Object.FindObjectsByType<Building>(FindObjectsSortMode.None), building => source.Data.CanDeliverTo(building));
        Assert.IsNotNull(target);
        Set(target, "level1StockConsumptionIntervalSeconds", 0f); target.SetLevel(1); target.SetSalary(0f, 300);
        Vector2 screen = Camera.main.WorldToScreenPoint(target.GetComponentInChildren<SpriteRenderer>().bounds.center);
        Assert.IsTrue(inventory.BeginDelivery(source, screen, 7));
        Call(DeliveryRuntime.Ensure(), "End", false);
        Assert.IsNotNull(target.GetComponentInChildren<DeliveryArrival>());
        return target;
    }

    private static int ReadSavedBoxCount()
    {
        var data = JsonUtility.FromJson<VillageManagement.VillageSaveData>(System.IO.File.ReadAllText(BoxSaveTestPath));
        int count = 0;
        foreach (BoxInventoryState state in data.boxInventories) count += state.boxes.Count;
        return count;
    }

    private static StoreIn PrepareReceiptSessionStore()
    {
        PrepareNativePointerFlow(false);
        return Object.FindFirstObjectByType<StoreIn>();
    }

    private static void AssertReceiptSessionContents(StoreIn store, int quantity, bool full)
    {
        Receipt receipt = store.CurrentReceipt;
        Assert.AreEqual(quantity, receipt.Quantity);
        Assert.AreEqual(full, receipt.IsFull);
        var photos = (System.Collections.IList)typeof(Receipt).GetField("photos", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(receipt);
        Assert.AreEqual(quantity == 0 ? 0 : 1, photos.Count);
        if (quantity > 0) Assert.IsTrue(((GameObject)photos[0]).activeInHierarchy);
        GameObject label = (GameObject)typeof(Receipt).GetField("quantityLabel", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(receipt);
        Assert.AreEqual(quantity == 0 ? "0" : quantity + "/" + receipt.RequiredQuantity, label.GetComponent<SpriteNumberLabel>().Value);
        GameObject box = (GameObject)typeof(StoreIn).GetField("packingBox", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(store);
        DeliveryMotion motion = box.GetComponent<DeliveryMotion>();
        Assert.AreEqual(full, motion != null && motion.IsFullBoxShaking);
    }

    [UnityTest] public IEnumerator StoreReplaysBackgroundEntranceAndRandomOwnerTriggerOnEveryShow()
    {
        yield return new EnterPlayMode();
        VerifyBackgroundEntrances();
        yield return null;
    }

    private static void VerifyBackgroundEntrances()
    {
        PrepareNativePointerFlow(false);
        StoreIn store = Object.FindFirstObjectByType<StoreIn>();
        GameObject up = (GameObject)typeof(StoreIn).GetField("backgroundUp", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(store);
        GameObject owner = (GameObject)typeof(StoreIn).GetField("backgroundOwner", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(store);
        Assert.IsNotNull(up, "The supplied prefab registers BackgroundUp");
        Assert.IsNotNull(owner, "The supplied prefab registers BackgroundOwner");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Tycoon/StoreIn.prefab");
        Vector3 rest = prefab.transform.Find("BackgroundUp").localPosition;
        Animator animator = owner.GetComponentInChildren<Animator>();
        var seen = new System.Collections.Generic.HashSet<int>();
        Random.State previous = Random.state;
        string[] states = { "StoreOwnerStanding", "StoreOwnerNapping", "StoreOwnerSleeping" };
        try
        {
            for (int seed = 1; seed <= 24; seed++)
            {
                Random.InitState(seed);
                int expected = Random.Range(0, 3);
                Random.InitState(seed);
                store.Show();
                seen.Add(expected);
                Assert.AreEqual(rest + Vector3.down * 0.5f, up.transform.localPosition);
                animator.Update(0f); animator.Update(0f);
                Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName(states[expected]), "Wrong owner state for seed " + seed + "; expected=" + states[expected] + "; state=" + animator.GetCurrentAnimatorStateInfo(0).shortNameHash + "; parameters=" + string.Join(",", System.Array.ConvertAll(animator.parameters, parameter => parameter.name)));
                animator.Update(0.25f);
                Random.InitState(seed);
                store.Show();
                animator.Update(0f); animator.Update(0f);
                Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName(states[expected]));
                Assert.Less(animator.GetCurrentAnimatorStateInfo(0).normalizedTime, 0.1f, "Choosing the same trigger restarts the animation");
                store.Close();
                Assert.AreEqual(rest, up.transform.localPosition);
            }
            Assert.AreEqual(3, seen.Count);
        }
        finally { Random.state = previous; }
    }

    [UnityTest] public IEnumerator StoreReplaysBoxOpenOnEveryShowWithoutReplacingBoxOrReceipt()
    {
        yield return new EnterPlayMode();
        var check = PrepareStoreReopen();
        yield return null;
        yield return null;
        check(0);
        yield return null;
        yield return null;
        check(1);
        yield return null;
        yield return null;
        check(2);
    }

    [UnityTest] public IEnumerator DeliveryShrinksThenHoldsRegisteredPrefabForThreeSecondsAndFadesBeforeRefill()
    {
        yield return new EnterPlayMode();
        var check = PrepareArrivalTimeline();
        yield return new WaitForSecondsRealtime(0.1f);
        check(0);
        yield return new WaitForSecondsRealtime(0.25f);
        check(1);
        yield return new WaitForSecondsRealtime(2.5f);
        check(2);
        yield return new WaitForSecondsRealtime(0.65f);
        check(3);
        yield return new WaitForSecondsRealtime(0.95f);
        check(4);
    }

    private static System.Action<int> PrepareArrivalTimeline()
    {
        PrepareNativePointerFlow(false);
        StoreIn store = Object.FindFirstObjectByType<StoreIn>();
        var definition = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Tycoon/BreadResource.prefab").GetComponent<ResourceItem>();
        var inventory = Object.FindFirstObjectByType<BoxIn>(FindObjectsInactive.Include);
        for (int i = 0; i < definition.RequiredQuantity; i++) Assert.IsTrue(store.CurrentReceipt.TryAdd(definition, VillageManagement.Instance));
        ReceiptData data = store.CurrentReceipt.Snapshot(); store.CurrentReceipt.Clear();
        var target = System.Array.Find(Object.FindObjectsByType<Building>(FindObjectsSortMode.None), building => data.CanDeliverTo(building));
        Set(target, "level1StockConsumptionIntervalSeconds", 0f); target.SetLevel(1); target.SetSalary(0f, 300);
        var visual = new GameObject("CustomDeliveredBox", typeof(SpriteRenderer), typeof(BoxCollider2D));
        visual.GetComponent<SpriteRenderer>().sprite = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Tycoon/Box.prefab").GetComponent<SpriteRenderer>().sprite;
        visual.GetComponent<SpriteRenderer>().color = new Color(0.7f, 0.8f, 0.9f, 0.8f);
        visual.transform.localScale = new Vector3(0.4f, 0.45f, 1f); visual.SetActive(false);
        Set(inventory, "deliveredBoxPrefab", visual);
        inventory.TryReserve(out object token); Assert.IsTrue(inventory.CommitReservation(token, data)); inventory.Show();
        boxComplete source = inventory.GetComponentInChildren<boxComplete>();
        Transform photo = source.GetComponentsInChildren<SpriteRenderer>()[1].transform;
        int deliveries = 0; inventory.Delivered += (_, building) => { Assert.AreSame(target, building); deliveries++; };
        Vector2 screen = Camera.main.WorldToScreenPoint(target.GetComponentInChildren<SpriteRenderer>().bounds.center);
        Assert.IsTrue(inventory.BeginDelivery(source, screen, 7));
        Call(DeliveryRuntime.Ensure(), "End", false);
        Assert.AreEqual(0, inventory.Count); Assert.IsFalse(inventory.gameObject.activeSelf);
        DeliveryArrival arrival = target.GetComponentInChildren<DeliveryArrival>(); Assert.IsNotNull(arrival);
        Vector3 originalScale = source.transform.localScale;
        return phase =>
        {
            if (phase == 0)
            {
                Assert.IsTrue(source.gameObject.activeInHierarchy); Assert.IsTrue(photo.gameObject.activeInHierarchy);
                Assert.Less(source.transform.localScale.x, originalScale.x); Assert.Greater(source.transform.localScale.x, 0f);
            }
            else if (phase < 4)
            {
                Assert.IsTrue(source == null);
                GameObject delivered = (GameObject)typeof(DeliveryArrival).GetField("delivered", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(arrival);
                Assert.IsNotNull(delivered); Assert.AreEqual("CustomDeliveredBox(Clone)", delivered.name);
                Assert.AreSame(target.Line2Point, delivered.transform.parent); Assert.AreEqual(Vector3.zero, delivered.transform.localPosition);
                Assert.IsNull(delivered.GetComponent<boxComplete>()); Assert.IsFalse(delivered.GetComponent<BoxCollider2D>().enabled);
                Assert.AreEqual(visual.transform.localScale, delivered.transform.localScale);
                float alpha = delivered.GetComponent<SpriteRenderer>().color.a;
                if (phase < 3) Assert.AreEqual(0.8f, alpha, 0.0001f);
                else { Assert.Greater(alpha, 0f); Assert.Less(alpha, 0.8f); }
                if (phase == 1) target.transform.position += new Vector3(0.2f, 0.1f, 0f);
            }
            if (phase < 4) { Assert.AreEqual(0f, target.CurrentSalary); Assert.AreEqual(0, deliveries); }
            else
            {
                Assert.IsTrue(arrival == null); Assert.AreEqual(1, deliveries);
                Assert.That(target.CurrentSalary, Is.EqualTo(100f).Within(0.0001f));
            }
            Assert.IsFalse(inventory.gameObject.activeSelf, "Arrival continues after BoxIn closes");
        };
    }

    private static System.Action<int> PrepareStoreReopen()
    {
        PrepareNativePointerFlow(false);
        StoreIn store = Object.FindFirstObjectByType<StoreIn>();
        GameObject box = (GameObject)typeof(StoreIn).GetField("packingBox", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(store);
        Animator animator = box.GetComponent<Animator>();
        var definition = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Tycoon/BreadResource.prefab").GetComponent<ResourceItem>();
        Assert.IsTrue(store.CurrentReceipt.TryAdd(definition, VillageManagement.Instance));
        return phase =>
        {
            Assert.AreSame(box, typeof(StoreIn).GetField("packingBox", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(store));
            Assert.AreEqual(1, store.CurrentReceipt.Quantity);
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            Assert.IsTrue(state.IsName("Open"), "First show and each later show enter the opening animation");
            Assert.Less(state.normalizedTime, 0.75f, "Opening animation restarts rather than staying on its final frame");
            if (phase == 2) return;
            animator.Play("Open", 0, 1f);
            animator.Update(0f);
            if (phase == 0) store.Close();
            store.Show();
        };
    }

    [UnityTest] public IEnumerator DesktopFrameLimitPreservesExplicitSettingsAndRestoresUncappedDefault()
    {
        yield return new EnterPlayMode();
        int originalLimit = Application.targetFrameRate;
        int originalVSync = QualitySettings.vSyncCount;
        try
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            DeliveryRuntime runtime = DeliveryRuntime.Ensure();
            Assert.AreEqual(60, Application.targetFrameRate);
            Object.DestroyImmediate(runtime.gameObject);
            Assert.AreEqual(-1, Application.targetFrameRate);

            Application.targetFrameRate = 30;
            runtime = DeliveryRuntime.Ensure();
            Assert.AreEqual(30, Application.targetFrameRate);
            Object.DestroyImmediate(runtime.gameObject);
            Assert.AreEqual(30, Application.targetFrameRate);

            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 1;
            runtime = DeliveryRuntime.Ensure();
            Assert.AreEqual(-1, Application.targetFrameRate);
            Object.DestroyImmediate(runtime.gameObject);
        }
        finally
        {
            Application.targetFrameRate = originalLimit;
            QualitySettings.vSyncCount = originalVSync;
        }
    }

    [UnityTest] public IEnumerator DecorationsDoNotBlockResourcesPackingOrCompletedBoxes()
    {
        yield return new EnterPlayMode();
        var check=PrepareNativePointerFlow(true);
        yield return null;
        yield return null;
        check(0);
        yield return new WaitForSecondsRealtime(1.1f);
        check(1);
        yield return null;
        yield return null;
        check(2);
    }
    static System.Action<int> PrepareNativePointerFlow(bool decorated)
    {
        foreach(var existing in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) if(existing.CompareTag("MainCamera")) existing.tag="Untagged";
        var camera=new GameObject("Camera",typeof(Camera)).GetComponent<Camera>();
        camera.tag="MainCamera"; camera.orthographic=true; camera.orthographicSize=8; camera.transform.position=new Vector3(0,0,-10);
        var villageRoot=new GameObject("Village"); villageRoot.SetActive(false);
        var village=villageRoot.AddComponent<VillageManagement>();
        Set(village,"dontDestroyOnLoad",false); Set(village,"autoSaveOnChange",false); Set(village,"loaded",true);
        village.SaveData.energyCapacity=1000; village.SaveData.currentEnergy=1000; villageRoot.SetActive(true);
        new GameObject("EventSystem",typeof(UnityEngine.EventSystems.EventSystem));
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Tycoon/StoreIn.prefab");
        var definitions=(System.Collections.Generic.List<StoreIn.ResourceSlot>)typeof(StoreIn).GetField("resources",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(prefab.GetComponent<StoreIn>());
        foreach(var slot in definitions)
        {
            var definition=slot.resourcePrefab.GetComponent<ResourceItem>();
            var root=new GameObject(definition.BuildingPrefab.name+"Path",typeof(BoxCollider2D)); root.transform.position=new Vector3(50,0,0);
            var path=root.AddComponent<global::Path>();
            var building=Object.Instantiate(definition.BuildingPrefab,path.transform,false);
            building.AssignSlot(path.PathId); building.MarkPlaced(true);
        }
        var inventory=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Tycoon/BoxIn.prefab")).GetComponent<BoxIn>(); inventory.Initialize(); inventory.Close();
        var store=Object.Instantiate(prefab).GetComponent<StoreIn>(); Set(store,"boxIn",inventory); store.Show();
        GameObject decorationCanvas=null;
        if (decorated)
        {
            decorationCanvas=new GameObject("PanelDecorations",typeof(RectTransform),typeof(Canvas),typeof(UnityEngine.UI.GraphicRaycaster));
            decorationCanvas.transform.SetParent(store.transform,false); decorationCanvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            decorationCanvas.GetComponent<Canvas>().sortingOrder=31000;
            var decoration=new GameObject("DecorationOverResources",typeof(RectTransform),typeof(UnityEngine.UI.Image)); decoration.transform.SetParent(decorationCanvas.transform,false);
            var rect=decoration.GetComponent<RectTransform>(); rect.anchorMin=Vector2.zero; rect.anchorMax=Vector2.one; rect.sizeDelta=Vector2.zero;
        }
        var slots=(System.Collections.Generic.List<StoreIn.ResourceSlot>)typeof(StoreIn).GetField("resources",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(store);
        var runtime=DeliveryRuntime.Ensure();
        UnityEngine.UI.Button otherButton=null;
        int otherClicks=0;
        return phase =>
        {
            if(phase==0)
            {
                DeliveryMotion.Reset(store.CurrentReceipt.transform);
                foreach(int finger in new[] { -1, 7 })
                foreach(var slot in slots)
                {
                    var resource=(GameObject)typeof(StoreIn.ResourceSlot).GetField("resource",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(slot);
                    var definition=slot.resourcePrefab.GetComponent<ResourceItem>();
                    Assert.IsTrue(store.CanBuy(definition),definition.name+" available: energy="+village.CurrentEnergy+", instance="+(VillageManagement.Instance==village)+", active="+store.isActiveAndEnabled+", full="+store.CurrentReceipt.IsFull);
                    Vector2 screen=camera.WorldToScreenPoint(resource.transform.position);
                    var found=(DeliveryPointer)typeof(DeliveryPointer).GetMethod("Find",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{screen});
                    var ui=(GameObject)typeof(DeliveryPointer).GetMethod("FindUiTarget",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{screen});
                    Assert.IsNotNull(found,definition.name+" blocked by UI "+(ui!=null?ui.name:"none"));
                    Assert.AreSame(resource,found.gameObject,definition.name+" should receive the pointer");
                    Call(runtime,"Press",screen,finger);
                    Assert.IsNotNull(typeof(ResourceItem).GetField("dragVisual",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(resource.GetComponent<ResourceItem>()));
                    Assert.IsTrue(store.gameObject.activeSelf,"Decorations must not close StoreIn");
                    int energy=village.CurrentEnergy;
                    Set(runtime,"lastPosition",(Vector2)camera.WorldToScreenPoint(store.CurrentReceipt.transform.position));
                    Call(runtime,"End",false);
                    Assert.AreEqual(1,store.CurrentReceipt.Quantity,definition.name+" drag reaches receipt");
                    Assert.AreEqual(energy-definition.EnergyPrice,village.CurrentEnergy);
                    store.CurrentReceipt.Clear();
                }
                var first=slots[0]; var firstDefinition=first.resourcePrefab.GetComponent<ResourceItem>();
                var firstResource=(GameObject)typeof(StoreIn.ResourceSlot).GetField("resource",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(first);
                foreach(int finger in new[] { -1, 7 })
                {
                    Transform prepared=((GameObject)typeof(ResourceItem).GetField("preparedDragVisual",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(firstResource.GetComponent<ResourceItem>())).transform;
                    Assert.IsNotNull(prepared);
                    Call(runtime,"Press",(Vector2)camera.WorldToScreenPoint(firstResource.transform.position),finger);
                    Assert.IsTrue(prepared.gameObject.activeInHierarchy);
                    store.Close();
                    Assert.IsFalse(prepared.gameObject.activeSelf,"Closing StoreIn cancels and parks the drag visual");
                    Assert.AreSame(firstResource.transform.parent,prepared.parent);
                    Assert.IsFalse((bool)typeof(DeliveryRuntime).GetField("captured",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime));
                    store.Show();
                    DeliveryMotion.Reset(store.CurrentReceipt.transform);
                }
                for(int i=0;i<firstDefinition.RequiredQuantity;i++) Assert.IsTrue(store.CurrentReceipt.TryAdd(firstDefinition,village));
                var box=(GameObject)typeof(StoreIn).GetField("packingBox",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(store);
                DeliveryMotion fullMotion=box.GetComponent<DeliveryMotion>();
                Assert.IsTrue(fullMotion.IsFullBoxShaking);
                store.Close();
                Assert.IsFalse(fullMotion.IsPlaying);
                store.Show();
                DeliveryMotion.Reset(store.CurrentReceipt.transform);
                Assert.IsTrue(fullMotion.IsFullBoxShaking,"Reopening a full receipt resumes its box reminder");
                Vector2 boxScreen=camera.WorldToScreenPoint(box.transform.position);
                var foundBox=(DeliveryPointer)typeof(DeliveryPointer).GetMethod("Find",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{boxScreen});
                Assert.IsNotNull(foundBox,"Packing box receives input"); Assert.AreSame(box,foundBox.gameObject);
                Call(runtime,"Press",boxScreen,decorated ? 7 : -1); Assert.IsTrue(store.IsPacking,"Clicking the full box starts packing"); Call(runtime,"End",false);
                Assert.IsFalse(fullMotion.IsPlaying,"Packing stops the full-state shake before the animation");
            }
            else if(phase==1)
            {
                if(decorationCanvas!=null) { decorationCanvas.transform.SetParent(inventory.transform,false); decorationCanvas.SetActive(true); }
                Assert.AreEqual(1,inventory.Count,"Click packed a box into inventory"); inventory.Show();
                var complete=inventory.GetComponentInChildren<boxComplete>();
                Vector2 screen=camera.WorldToScreenPoint(complete.transform.position);
                var found=(DeliveryPointer)typeof(DeliveryPointer).GetMethod("Find",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{screen});
                Assert.IsNotNull(found); Assert.AreSame(complete.gameObject,found.gameObject,"Completed box receives input");
                foreach(int finger in new[] { -1, 7 })
                {
                    Transform photo=complete.GetComponentsInChildren<SpriteRenderer>()[1].transform;
                    Vector3 photoScale=photo.lossyScale;
                    Call(runtime,"Press",screen,finger); Assert.IsTrue(inventory.IsDelivering,"Mouse/touch begins box delivery"); Assert.IsFalse(inventory.gameObject.activeSelf);
                    Assert.IsTrue(photo.gameObject.activeInHierarchy);
                    Assert.That(Vector3.Distance(photo.lossyScale,photoScale*1.1f),Is.LessThan(0.0001f));
                    Call(runtime,"End",true); Assert.IsTrue(inventory.gameObject.activeSelf); Assert.AreEqual(1,inventory.Count);
                }
                var buttonCanvas=new GameObject("OtherButtons",typeof(RectTransform),typeof(Canvas),typeof(UnityEngine.UI.GraphicRaycaster));
                buttonCanvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
                buttonCanvas.GetComponent<Canvas>().sortingOrder=32000;
                var buttonRoot=new GameObject("OtherButtonOverBox",typeof(RectTransform),typeof(UnityEngine.UI.Image),typeof(UnityEngine.UI.Button));
                buttonRoot.transform.SetParent(buttonCanvas.transform,false);
                var buttonRect=buttonRoot.GetComponent<RectTransform>(); buttonRect.anchorMin=Vector2.zero; buttonRect.anchorMax=Vector2.one; buttonRect.sizeDelta=Vector2.zero;
                otherButton=buttonRoot.GetComponent<UnityEngine.UI.Button>(); otherButton.onClick.AddListener(()=>otherClicks++);
            }
            else
            {
                var complete=inventory.GetComponentInChildren<boxComplete>();
                Vector2 screen=camera.WorldToScreenPoint(complete.transform.position);
                Call(runtime,"Press",screen,-1);
                Assert.IsFalse(inventory.IsDelivering,"Real UI button keeps priority over completed box");
                UnityEngine.EventSystems.ExecuteEvents.Execute(otherButton.gameObject,new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current){button=UnityEngine.EventSystems.PointerEventData.InputButton.Left},UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
                Assert.AreEqual(1,otherClicks); Assert.IsFalse(inventory.gameObject.activeSelf);
                Call(runtime,"End",false); otherButton.gameObject.SetActive(false);
                inventory.Show();
                Call(runtime,"Press",screen,7); Assert.IsTrue(inventory.IsDelivering);
                var target=System.Array.Find(Object.FindObjectsByType<Building>(FindObjectsSortMode.None),building=>complete.Data.CanDeliverTo(building));
                Assert.IsNotNull(target);
                Set(runtime,"lastPosition",(Vector2)camera.WorldToScreenPoint(target.GetComponentInChildren<SpriteRenderer>().bounds.center));
                Call(runtime,"End",false);
                Assert.AreEqual(0,inventory.Count,"Touch delivery removes completed box");
                Assert.IsFalse(inventory.gameObject.activeSelf,"Delivering the final box keeps the empty inventory closed");
            }
        };
    }
    private static void Set(object target,string field,object value) => target.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
    private static object Call(object target,string method,params object[] args) => target.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,args);
}
#endif
