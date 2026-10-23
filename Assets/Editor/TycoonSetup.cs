using System;
using System.Linq;
using Tycoon;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using ResourceItem = Tycoon.Resources;

// This menu keeps existing art and creates editable anchor children for the user.
public static class TycoonSetup
{
    private const string Folder = "Assets/Prefabs/Tycoon/";
    private static readonly string[] ResourceNames = { "BreadResource", "ChickenResource", "CoffeeResource", "FruitResource", "IcecreamResource", "PharmacyResource", "SushiResource" };
    private static readonly string[] PhotoNames = { "bread", "chicken", "coffee", "Fruit", "icecream", "medicine ", "sushi" };
    private static readonly string[] BoxPhotoNames = { "breadBoxComplete", "chickenBoxComplete Variant", "coffeeBoxComplete Variant", "FruitBoxComplete Variant", "icecreamBoxComplete", "medicine BoxComplete Variant", "sushi BoxComplete" };
    private static readonly string[] BuildingNames = { "Bread", "ChickenTruck", "Coffee", "Fruit", "Icecream", "Pharmacy", "Fish" };

    [MenuItem("Tools/Tycoon/1. Prepare prefabs")]
    public static void PreparePrefabs()
    {
        for (int i = 0; i < ResourceNames.Length; i++)
        {
            int index = i;
            Edit(ResourceNames[i], root =>
            {
                ResourceItem resource = root.GetComponent<ResourceItem>();
                bool added = resource == null;
                if (added) resource = root.AddComponent<ResourceItem>();
                var so = new SerializedObject(resource);
                if (added) so.FindProperty("resourceId").stringValue = ResourceNames[index];
                SetIfEmpty(so, "buildingPrefab", Building(index)?.gameObject);
                SetIfEmpty(so, "receiptPhotoPrefab", Prefab(PhotoNames[index]));
                so.ApplyModifiedPropertiesWithoutUndo();
            });
        }

        Edit("Receipt", root =>
        {
            Receipt receipt = root.GetComponent<Receipt>();
            if (receipt == null) receipt = root.AddComponent<Receipt>();
            var so = new SerializedObject(receipt);
            SetIfEmpty(so, "boxPosition", Anchor(root, "BoxPosition", new Vector3(1.4f, 0f)));
            SetIfEmpty(so, "quantityPosition", Anchor(root, "QuantityPosition", new Vector3(-1.9f, -1.1f)));
            SerializedProperty entries = so.FindProperty("resourcePhotos");
            if (entries.arraySize == 0)
            {
                entries.arraySize = ResourceNames.Length;
                for (int i = 0; i < ResourceNames.Length; i++)
                {
                    SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                    entry.FindPropertyRelative("resourcePrefab").objectReferenceValue = Prefab(ResourceNames[i]);
                    SerializedProperty positions = entry.FindPropertyRelative("photoPositions");
                    positions.arraySize = 1;
                    // One representative photo; quantity is shown separately as 1/4, 2/4...
                    positions.GetArrayElementAtIndex(0).objectReferenceValue = Anchor(root, "PhotoPosition", new Vector3(-1.9f, 0.35f));
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        });

        Edit("boxComplete", root =>
        {
            boxComplete completed = root.GetComponent<boxComplete>();
            if (completed == null) completed = root.AddComponent<boxComplete>();
            var so = new SerializedObject(completed);
            SerializedProperty entries = so.FindProperty("receiptPhotos");
            if (entries.arraySize == 0)
            {
                entries.arraySize = ResourceNames.Length;
                Transform position = Anchor(root, "PhotoPosition", new Vector3(0f, 0.6f));
                position.localScale = Vector3.one * 0.5f;
                for (int i = 0; i < ResourceNames.Length; i++)
                {
                    SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                    entry.FindPropertyRelative("receiptPhotoPrefab").objectReferenceValue = Prefab(BoxPhotoNames[i]);
                    entry.FindPropertyRelative("buildingPrefab").objectReferenceValue = Building(i)?.gameObject;
                    entry.FindPropertyRelative("photoPosition").objectReferenceValue = position;
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        });

        CreatePadlock();
        CreateBoxAnimation();
        CreateNumbers();
        CreatePanels();
        AssetDatabase.SaveAssets();
        Debug.Log("Tycoon 프리팹 준비 완료. StoreIn / BoxIn의 위치 자식을 조정하고 자원 가격/수량을 설정하세요. SushiResource는 기존 Fish 건물에 연결했습니다.");
    }

    [MenuItem("Tools/Tycoon/2. Connect Store and Box buttons in open scene")]
    public static void ConnectScene()
    {
        if (Prefab("StoreIn") == null || Prefab("BoxIn") == null) PreparePrefabs();
        foreach (Button button in UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (button.name == "Store")
            {
                Store store = button.GetComponent<Store>();
                if (store == null) store = Undo.AddComponent<Store>(button.gameObject);
                var so = new SerializedObject(store);
                SetIfEmpty(so, "storeInPrefab", Prefab("StoreIn"));
                so.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(button.gameObject.scene);
            }
            if (button.name == "Box")
            {
                Tycoon.Box box = button.GetComponent<Tycoon.Box>();
                if (box == null) box = Undo.AddComponent<Tycoon.Box>(button.gameObject);
                var so = new SerializedObject(box);
                SetIfEmpty(so, "boxInPrefab", Prefab("BoxIn"));
                Transform number = button.transform.Find("NumberPosition");
                if (number == null)
                {
                    var numberObject = new GameObject("NumberPosition", typeof(RectTransform));
                    Undo.RegisterCreatedObjectUndo(numberObject, "Create box count position");
                    numberObject.transform.SetParent(button.transform, false);
                    number = numberObject.transform;
                    ((RectTransform)number).anchoredPosition = new Vector2(65f, 65f);
                }
                SetIfEmpty(so, "numberPosition", number);
                SerializedProperty numbers = so.FindProperty("numberPrefabs");
                if (numbers.arraySize < 7) numbers.arraySize = 7;
                for (int i = 0; i < 7; i++)
                    if (numbers.GetArrayElementAtIndex(i).objectReferenceValue == null)
                        numbers.GetArrayElementAtIndex(i).objectReferenceValue = Prefab("Number" + (i + 1));
                so.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(button.gameObject.scene);
            }
        }
        Debug.Log("현재 씬의 Store / Box 버튼 연결 완료. 변경사항을 저장하세요. Button OnClick에는 별도 함수를 추가할 필요가 없습니다.");
    }

    private static void CreatePanels()
    {
        if (Prefab("StoreIn") == null)
        {
            var root = new GameObject("StoreIn");
            try
            {
                StoreIn store = root.AddComponent<StoreIn>();
                AddBackground(root, false);
                GameObject close = InstantiateAt(Prefab("X"), root.transform, new Vector3(6f, 3.4f));
                var so = new SerializedObject(store);
                so.FindProperty("xPrefab").objectReferenceValue = close;
                so.FindProperty("padlockPrefab").objectReferenceValue = Prefab("Padlock");
                so.FindProperty("receiptPrefab").objectReferenceValue = Prefab("Receipt");
                so.FindProperty("receiptPosition").objectReferenceValue = Anchor(root, "ReceiptPosition", new Vector3(0f, -2f));
                so.FindProperty("boxPrefab").objectReferenceValue = Prefab("Box");
                SerializedProperty slots = so.FindProperty("resources");
                slots.arraySize = ResourceNames.Length;
                for (int i = 0; i < ResourceNames.Length; i++)
                {
                    float x = (i % 4 - 1.5f) * 2.7f;
                    float y = 2.4f - i / 4 * 2.1f;
                    SerializedProperty slot = slots.GetArrayElementAtIndex(i);
                    slot.FindPropertyRelative("resourcePrefab").objectReferenceValue = Prefab(ResourceNames[i]);
                    slot.FindPropertyRelative("resourcePosition").objectReferenceValue = Anchor(root, ResourceNames[i] + "Position", new Vector3(x, y));
                    slot.FindPropertyRelative("pricePosition").objectReferenceValue = Anchor(root, ResourceNames[i] + "PricePosition", new Vector3(x, y - 0.9f));
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, Folder + "StoreIn.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        if (Prefab("BoxIn") == null)
        {
            var root = new GameObject("BoxIn");
            try
            {
                BoxIn inventory = root.AddComponent<BoxIn>();
                AddBackground(root, true);
                GameObject close = InstantiateAt(Prefab("X"), root.transform, new Vector3(6f, 2f));
                var so = new SerializedObject(inventory);
                so.FindProperty("xPrefab").objectReferenceValue = close;
                so.FindProperty("boxCompletePrefab").objectReferenceValue = Prefab("boxComplete");
                so.FindProperty("deliveredBoxPrefab").objectReferenceValue = Prefab("Box");
                SerializedProperty positions = so.FindProperty("boxCompletePositions");
                positions.arraySize = 7;
                for (int i = 0; i < 7; i++)
                    positions.GetArrayElementAtIndex(i).objectReferenceValue = Anchor(root, "BoxCompletePosition" + (i + 1), new Vector3((i - 3) * 1.6f, 0f));
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, Folder + "BoxIn.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }

    private static void CreatePadlock()
    {
        if (Prefab("Padlock") != null) return;
        Sprite sprite = AssetDatabase.LoadAllAssetsAtPath("Assets/sprites/Tycoon/B20001.png").OfType<Sprite>()
            .FirstOrDefault(s => s.rect.x > 1150 && s.rect.x < 1300 && s.rect.y > 150 && s.rect.y < 200);
        var root = new GameObject("Padlock", typeof(SpriteRenderer));
        try
        {
            root.GetComponent<SpriteRenderer>().sprite = sprite;
            root.GetComponent<SpriteRenderer>().sortingOrder = 1100;
            PrefabUtility.SaveAsPrefabAsset(root, Folder + "Padlock.prefab");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void AddBackground(GameObject root, bool shelf)
    {
        var background = new GameObject("Background", typeof(SpriteRenderer), typeof(BoxCollider2D));
        background.transform.SetParent(root.transform, false);
        Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath("Assets/sprites/Tycoon/B20001.png").OfType<Sprite>().ToArray();
        Sprite sprite = sprites.FirstOrDefault(s => s.rect.width > 1200 && (shelf ? s.rect.height > 300 : s.rect.height < 300));
        SpriteRenderer renderer = background.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 1000;
        background.transform.localScale = shelf ? Vector3.one : new Vector3(1f, 3.2f, 1f);
        if (sprite != null) background.GetComponent<BoxCollider2D>().size = sprite.bounds.size;
        if (sprite != null) background.GetComponent<BoxCollider2D>().offset = sprite.bounds.center;
    }

    private static void CreateNumbers()
    {
        Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath("Assets/sprites/Tycoon/B20001.png").OfType<Sprite>().ToArray();
        for (int i = 0; i < 7; i++)
        {
            string name = "Number" + (i + 1);
            if (Prefab(name) != null) continue;
            var root = new GameObject(name, typeof(SpriteRenderer));
            try
            {
                root.GetComponent<SpriteRenderer>().sprite = sprites.FirstOrDefault(s => s.name == "B20001_" + (25 + i));
                root.GetComponent<SpriteRenderer>().sortingOrder = 2200;
                PrefabUtility.SaveAsPrefabAsset(root, Folder + name + ".prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }

    private static void CreateBoxAnimation()
    {
        const string controllerPath = Folder + "PackingBox.controller";
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (controller == null || controller.layers.Length == 0)
        {
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            else controller.AddLayer("Base Layer");
            controller.AddParameter("pack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("boxopen", AnimatorControllerParameterType.Trigger);
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState idle = machine.AddState("Idle");
            idle.motion = BoxClip("Idle", new[] { 24, 24 });
            machine.defaultState = idle;
            int[] frames = { 24, 23, 22, 21, 9, 10, 11, 12, 13, 14, 15, 16 };
            AnimatorState pack = machine.AddState("Pack");
            pack.motion = BoxClip("Pack", frames);
            AnimatorState open = machine.AddState("Open");
            open.motion = BoxClip("Open", frames.Reverse().ToArray());
            AnimatorStateTransition packTransition = machine.AddAnyStateTransition(pack);
            packTransition.hasExitTime = false;
            packTransition.duration = 0f;
            packTransition.canTransitionToSelf = false;
            packTransition.AddCondition(AnimatorConditionMode.If, 0f, "pack");
            AnimatorStateTransition openTransition = machine.AddAnyStateTransition(open);
            openTransition.hasExitTime = false;
            openTransition.duration = 0f;
            openTransition.canTransitionToSelf = true;
            openTransition.AddCondition(AnimatorConditionMode.If, 0f, "boxopen");
            EditorUtility.SetDirty(idle);
            EditorUtility.SetDirty(pack);
            EditorUtility.SetDirty(open);
            EditorUtility.SetDirty(packTransition);
            EditorUtility.SetDirty(openTransition);
            EditorUtility.SetDirty(machine);
            EditorUtility.SetDirty(controller);
        }
        Edit("Box", root =>
        {
            Animator animator = root.GetComponent<Animator>();
            if (animator == null) animator = root.AddComponent<Animator>();
            if (animator.runtimeAnimatorController == null) animator.runtimeAnimatorController = controller;
            animator.updateMode = AnimatorUpdateMode.UnscaledTime;
        });
    }

    private static AnimationClip BoxClip(string name, int[] frames)
    {
        AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "Box" + name + ".anim");
        if (existing != null) return existing;
        Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath("Assets/sprites/Tycoon/B20001.png").OfType<Sprite>().ToArray();
        var clip = new AnimationClip { name = name, frameRate = 24f };
        var keys = new ObjectReferenceKeyframe[frames.Length];
        for (int i = 0; i < frames.Length; i++)
            keys[i] = new ObjectReferenceKeyframe { time = i / 24f, value = sprites.FirstOrDefault(s => s.name == "B20001_" + frames[i]) };
        AnimationUtility.SetObjectReferenceCurve(clip, EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite"), keys);
        AssetDatabase.CreateAsset(clip, Folder + "Box" + name + ".anim");
        return clip;
    }

    private static GameObject Prefab(string name) => AssetDatabase.LoadAssetAtPath<GameObject>(Folder + name + ".prefab");
    private static Building Building(int index)
    {
        GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Village/" + BuildingNames[index] + ".prefab");
        return root != null ? root.GetComponent<Building>() : null;
    }

    private static Transform Anchor(GameObject root, string name, Vector3 position)
    {
        Transform existing = root.transform.Find(name);
        if (existing != null) return existing;
        var child = new GameObject(name);
        child.transform.SetParent(root.transform, false);
        child.transform.localPosition = position;
        return child.transform;
    }

    private static GameObject InstantiateAt(GameObject prefab, Transform parent, Vector3 position)
    {
        if (prefab == null) return null;
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        instance.transform.localPosition = position;
        return instance;
    }

    private static void SetIfEmpty(SerializedObject so, string name, UnityEngine.Object value)
    {
        SerializedProperty property = so.FindProperty(name);
        if (property.objectReferenceValue == null) property.objectReferenceValue = value;
    }

    private static void Edit(string name, Action<GameObject> action)
    {
        if (Prefab(name) == null) { Debug.LogWarning("누락된 프리팹: " + Folder + name + ".prefab"); return; }
        GameObject root = PrefabUtility.LoadPrefabContents(Folder + name + ".prefab");
        try
        {
            action(root);
            PrefabUtility.SaveAsPrefabAsset(root, Folder + name + ".prefab");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
