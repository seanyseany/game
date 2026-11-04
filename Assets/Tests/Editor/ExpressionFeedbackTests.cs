#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class ExpressionFeedbackTestPlayer : Player
{
    protected override bool UsesArcadeSystems => false;
    protected override void Awake() { }
    protected override void Start() { }
    protected override void Update() { }
    protected override void LateUpdate() { }
}

public sealed class ExpressionFeedbackTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private Player player;
    private ArcadeFeedback feedback;
    private ArcadePopup ordinary;
    private ArcadePopup damage;
    private Expression expression;
    private Random.State randomState;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        randomState = Random.state;
        player = new GameObject("Expression test player").AddComponent<ExpressionFeedbackTestPlayer>();
        player.lives = 3;
        feedback = player.gameObject.AddComponent<ArcadeFeedback>();
        ordinary = LoadPopup("Emoji Happy");
        damage = LoadPopup("Emoji Crying");
        SetField(feedback, "damageEmojiPrefabs", new[] { damage });
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/Feedback/Expression(!).prefab");
        expression = Object.Instantiate(prefab).GetComponent<Expression>();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        Random.state = randomState;
        if (feedback != null) feedback.ResetFeedback();
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator SingleConfiguredEmojiReplacesPreviousExpressionEmoji()
    {
        feedback.ShowRandomEmoji(new[] { ordinary }, true);
        ArcadePopup previous = ActiveEmoji;
        CompleteExpression();
        Assert.IsFalse(previous.gameObject.activeSelf);
        Assert.AreEqual("Emoji ExclaimationMark(Clone)", ActiveEmoji.name);
        Assert.AreEqual(1, ActiveEmoji.transform.parent.GetComponentsInChildren<ArcadePopup>().Length);
        ArcadePopup current = ActiveEmoji;
        expression.EnterEnd(player);
        Assert.AreSame(current, ActiveEmoji, "Repeated End contacts must not recreate the emoji.");
        yield return null;
    }

    [UnityTest]
    public IEnumerator SingleConfiguredEmojiIsSelectedForEveryRandomSeed()
    {
        for (int seed = 0; seed < 16; seed++)
        {
            feedback.ResetFeedback();
            expression.gameObject.SetActive(false);
            expression.gameObject.SetActive(true);
            Random.InitState(seed);
            CompleteExpression();
            Assert.AreEqual("Emoji ExclaimationMark(Clone)", ActiveEmoji.name);
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator DamageEmojiTakesPriorityUntilItExpires()
    {
        feedback.ShowRandomEmoji(new[] { ordinary }, true);
        ArcadePopup previous = ActiveEmoji;
        feedback.NotifyLifeLost();
        Assert.IsFalse(previous.gameObject.activeSelf);
        ArcadePopup hurt = ActiveEmoji;
        Assert.AreEqual(damage.name + "(Clone)", hurt.name);
        CompleteExpression();
        Assert.AreSame(hurt, ActiveEmoji);
        Object.Destroy(hurt.gameObject);
        yield return null;
        expression.gameObject.SetActive(false);
        expression.gameObject.SetActive(true);
        CompleteExpression();
        Assert.AreEqual("Emoji ExclaimationMark(Clone)", ActiveEmoji.name);
    }

    [UnityTest]
    public IEnumerator DamageInsideExpressionPreventsItsSuccessEmoji()
    {
        expression.EnterStart(player);
        feedback.NotifyLifeLost();
        ArcadePopup hurt = ActiveEmoji;
        expression.EnterEnd(player);
        Assert.AreSame(hurt, ActiveEmoji);
        yield return null;
    }

    private ArcadePopup ActiveEmoji => (ArcadePopup)typeof(ArcadeFeedback)
        .GetField("activePlayerEmoji", Private).GetValue(feedback);

    private void CompleteExpression()
    {
        expression.EnterStart(player);
        expression.EnterEnd(player);
    }

    private static ArcadePopup LoadPopup(string name) => AssetDatabase
        .LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/Feedback/" + name + ".prefab")
        .GetComponent<ArcadePopup>();

    private static void SetField(object target, string name, object value) => target.GetType()
        .GetField(name, Private).SetValue(target, value);
}
#endif
