using System.Collections.Generic;
using UnityEngine;

public class Way : MonoBehaviour
{
    [Header("Route")]
    [SerializeField] private List<Transform> routeNodes = new List<Transform>();
    [SerializeField] private bool loopRoute = true;

    [Header("Connections")]
    [SerializeField] private List<Path> connectedPaths = new List<Path>();
    [SerializeField] private List<Entrance> connectedEntrances = new List<Entrance>();

    public IReadOnlyList<Path> ConnectedPaths => connectedPaths;
    public IReadOnlyList<Entrance> ConnectedEntrances => connectedEntrances;
    public IReadOnlyList<Transform> RouteNodes => routeNodes;
    public bool LoopRoute => loopRoute;

    public float GetTrafficScore()
    {
        float score = 0f;
        for (int i = 0; i < connectedPaths.Count; i++)
        {
            Path path = connectedPaths[i];
            if (path == null)
                continue;

            score += path.GetActivationScore();
        }

        return score;
    }

    public bool TryGetSpawnEntrance(out Entrance entrance)
    {
        List<Entrance> validEntrances = new List<Entrance>();
        for (int i = 0; i < connectedEntrances.Count; i++)
        {
            if (connectedEntrances[i] != null)
                validEntrances.Add(connectedEntrances[i]);
        }

        if (validEntrances.Count == 0)
        {
            entrance = null;
            return false;
        }

        entrance = validEntrances[Random.Range(0, validEntrances.Count)];
        return true;
    }

    public bool TryGetActivePath(out Path path)
    {
        List<Path> validPaths = new List<Path>();
        float totalScore = 0f;

        for (int i = 0; i < connectedPaths.Count; i++)
        {
            Path candidate = connectedPaths[i];
            if (candidate == null)
                continue;

            float score = candidate.GetActivationScore();
            if (score <= 0f)
                continue;

            validPaths.Add(candidate);
            totalScore += score;
        }

        if (validPaths.Count == 0 || totalScore <= 0f)
        {
            path = null;
            return false;
        }

        float pick = Random.Range(0f, totalScore);
        float cumulative = 0f;
        for (int i = 0; i < validPaths.Count; i++)
        {
            cumulative += validPaths[i].GetActivationScore();
            if (pick <= cumulative)
            {
                path = validPaths[i];
                return true;
            }
        }

        path = validPaths[validPaths.Count - 1];
        return true;
    }

    public bool TryGetAnyPath(out Path path)
    {
        List<Path> validPaths = new List<Path>();
        for (int i = 0; i < connectedPaths.Count; i++)
        {
            if (connectedPaths[i] != null)
                validPaths.Add(connectedPaths[i]);
        }

        if (validPaths.Count == 0)
        {
            path = null;
            return false;
        }

        path = validPaths[Random.Range(0, validPaths.Count)];
        return true;
    }

    public bool TryGetRouteNode(int index, out Vector3 worldPoint)
    {
        if (index >= 0 && index < routeNodes.Count && routeNodes[index] != null)
        {
            worldPoint = routeNodes[index].position;
            return true;
        }

        worldPoint = transform.position;
        return false;
    }

    public int GetFirstRouteNodeIndex()
    {
        for (int i = 0; i < routeNodes.Count; i++)
        {
            if (routeNodes[i] != null)
                return i;
        }

        return -1;
    }

    public int GetNextRouteNodeIndex(int currentIndex)
    {
        int nextIndex = currentIndex + 1;
        while (nextIndex < routeNodes.Count)
        {
            if (routeNodes[nextIndex] != null)
                return nextIndex;

            nextIndex++;
        }

        if (!loopRoute)
            return -1;

        return GetFirstRouteNodeIndex();
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.1f, 0.8f, 0.9f, 1f);

        Transform previous = null;
        Transform first = null;
        for (int i = 0; i < routeNodes.Count; i++)
        {
            Transform node = routeNodes[i];
            if (node == null)
                continue;

            if (first == null)
                first = node;

            Gizmos.DrawSphere(node.position, 0.12f);
            if (previous != null)
                Gizmos.DrawLine(previous.position, node.position);

            previous = node;
        }

        if (loopRoute && previous != null && first != null && previous != first)
            Gizmos.DrawLine(previous.position, first.position);
    }
}
