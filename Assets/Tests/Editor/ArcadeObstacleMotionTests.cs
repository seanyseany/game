#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class ArcadeObstacleMotionTests
{
    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator ScrollingStage421RopesDisplayContinuousSwingBetweenPhysicsSteps()
    {
        yield return new EnterPlayMode();
        int previousLimit = Application.targetFrameRate;
        int previousVSync = QualitySettings.vSyncCount;
        float previousStep = Time.fixedDeltaTime;
        PlayerLoopSystem previousLoop = PlayerLoop.GetCurrentPlayerLoop();
        var phase = new GameObject("ScrollingRopeMotionCheck");
        phase.SetActive(false);
        try
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            Time.fixedDeltaTime = 0.02f;
            phase.AddComponent<PhaseLayoutSnapshot>();
            phase.AddComponent<Mover>().baseSpeed = 6f;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/stage 4/stage 4-21.prefab");
            SwingMotor[] originals = prefab.GetComponentsInChildren<SwingMotor>(true);
            Assert.AreEqual(9, originals.Length);
            var motors = new SwingMotor[originals.Length];
            var previousAngles = new float[originals.Length];
            for (int i = 0; i < originals.Length; i++)
            {
                Transform anchor = originals[i].transform.parent;
                GameObject rope = Object.Instantiate(anchor.gameObject, phase.transform, false);
                rope.transform.SetLocalPositionAndRotation(anchor.localPosition, anchor.localRotation);
                motors[i] = rope.GetComponentInChildren<SwingMotor>();
            }
            phase.SetActive(true);

            float warmUntil = Time.unscaledTime + 0.5f;
            while (Time.unscaledTime < warmUntil) yield return null;
            for (int i = 0; i < motors.Length; i++) previousAngles[i] = motors[i].transform.eulerAngles.z;
            float startRootX = phase.transform.position.x;
            float startBodyX = motors[0].rb.position.x;
            Rigidbody2D anchorBody = motors[0].GetComponent<HingeJoint2D>().connectedBody;
            float startAnchorX = anchorBody.position.x;
            float anchorOffsetX = startAnchorX - phase.GetComponent<Rigidbody2D>().position.x;
            float until = Time.unscaledTime + 6f;
            int moving = 0, stationary = 0;
            // Sample after the game loop's interpolation and LateUpdate, rather than
            // the editor test runner, which can resume before the displayed pose is ready.
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            for (int system = 0; system < loop.subSystemList.Length; system++)
            {
                if (loop.subSystemList[system].type != typeof(UnityEngine.PlayerLoop.PreLateUpdate)) continue;
                PlayerLoopSystem late = loop.subSystemList[system];
                var children = new PlayerLoopSystem[late.subSystemList.Length + 1];
                System.Array.Copy(late.subSystemList, children, late.subSystemList.Length);
                children[children.Length - 1] = new PlayerLoopSystem
                {
                    type = typeof(ArcadeObstacleMotionTests),
                    updateDelegate = () =>
                    {
                        for (int i = 0; i < motors.Length; i++)
                        {
                            SwingMotor motor = motors[i];
                            float angle = motor.transform.eulerAngles.z;
                            if (Mathf.Abs(motor.rb.angularVelocity) > 0.3f)
                            {
                                moving++;
                                if (Mathf.Abs(Mathf.DeltaAngle(previousAngles[i], angle)) < 0.00001f) stationary++;
                            }
                            previousAngles[i] = angle;
                        }
                    }
                };
                late.subSystemList = children;
                loop.subSystemList[system] = late;
                break;
            }
            PlayerLoop.SetPlayerLoop(loop);
            while (Time.unscaledTime < until) yield return null;
            Debug.Log("[ARCADE ROPE MOTION] moving=" + moving + ", stationary_display_frames=" + stationary);
            float rootTravel = phase.transform.position.x - startRootX;
            Assert.That(rootTravel, Is.EqualTo(-36f).Within(0.3f));
            Assert.That(anchorBody.position.x - startAnchorX, Is.EqualTo(rootTravel).Within(0.3f),
                "The static hinge anchor must scroll in physics as well as on screen");
            Assert.That(motors[0].rb.position.x - startBodyX, Is.EqualTo(rootTravel).Within(3f),
                "The saw must not stay behind while its phase scrolls away");
            Assert.Greater(moving, 300);
            Assert.Less((double)stationary / moving, 0.08,
                "Swinging obstacles should interpolate their displayed pose between the 50 Hz physics steps");

            Mover mover = phase.GetComponent<Mover>();
            Rigidbody2D scrollingBody = phase.GetComponent<Rigidbody2D>();
            Vector2 physicsPosition = scrollingBody.position;
            mover.enabled = false;
            mover.enabled = true;
            Assert.That(Vector2.Distance(physicsPosition, scrollingBody.position), Is.LessThan(0.00001f),
                "Resuming a component pause must not copy an interpolated display position into physics");

            phase.SetActive(false);
            phase.transform.position = new Vector3(100f, 0f, 0f);
            phase.SetActive(true);
            Assert.That(scrollingBody.position.x, Is.EqualTo(100f).Within(0.00001f),
                "Pool activation must reset the scrolling body's world position to its new spawn point");
            float resumeUntil = Time.fixedTime + Time.fixedDeltaTime * 2f;
            while (Time.fixedTime < resumeUntil) yield return null;
            Assert.That(anchorBody.position.x - scrollingBody.position.x, Is.EqualTo(anchorOffsetX).Within(0.3f),
                "The hinge must retain its slot after the whole phase is reactivated at a different position");
        }
        finally
        {
            PlayerLoop.SetPlayerLoop(previousLoop);
            Object.Destroy(phase);
            Application.targetFrameRate = previousLimit;
            QualitySettings.vSyncCount = previousVSync;
            Time.fixedDeltaTime = previousStep;
        }
    }
}
#endif
