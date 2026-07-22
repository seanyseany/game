using System.Collections.Generic;
using UnityEngine;

public class Way : MonoBehaviour
{
    [System.Serializable]
    public class RouteSequence
    {
        public List<Transform> nodes = new List<Transform>();
    }

    [Header("Route")]
    [SerializeField] private List<RouteSequence> routeSequences = new List<RouteSequence>();
    [SerializeField] private bool loopRoute = true;

    [Header("Connections")]
    [SerializeField] private List<Path> connectedPaths = new List<Path>();
    [SerializeField] private List<Entrance> connectedEntrances = new List<Entrance>();

    public IReadOnlyList<Path> ConnectedPaths => connectedPaths;
    public IReadOnlyList<Entrance> ConnectedEntrances => connectedEntrances;
    public IReadOnlyList<RouteSequence> RouteSequences => routeSequences;
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

    public Vector3 GetRandomRoamWorldPoint()
    {
        Collider2D roamCollider = GetComponent<Collider2D>();
        if (roamCollider != null)
        {
            Bounds bounds = roamCollider.bounds;
            for (int i = 0; i < 12; i++)
            {
                Vector3 candidate = new Vector3(
                    Random.Range(bounds.min.x, bounds.max.x),
                    Random.Range(bounds.min.y, bounds.max.y),
                    0f);

                if (roamCollider.OverlapPoint(candidate))
                    return candidate;
            }
        }

        int sequenceIndex = GetRandomRouteSequenceIndex();
        int firstIndex = GetFirstRouteNodeIndex(sequenceIndex);
        if (sequenceIndex != int.MinValue &&
            firstIndex >= 0 &&
            TryGetRouteNode(sequenceIndex, firstIndex, out Vector3 routePoint))
            return routePoint;

        return transform.position;
    }

    public int GetRandomRouteSequenceIndex()
    {
        List<int> validIndices = new List<int>();
        for (int i = 0; i < routeSequences.Count; i++)
        {
            if (GetFirstRouteNodeIndex(i) >= 0)
                validIndices.Add(i);
        }

        return validIndices.Count > 0 ? validIndices[Random.Range(0, validIndices.Count)] : int.MinValue;
    }

    public bool TryGetRouteNode(int sequenceIndex, int nodeIndex, out Vector3 worldPoint)
    {
        List<Transform> nodes = GetSequenceNodes(sequenceIndex);
        if (nodeIndex >= 0 && nodeIndex < nodes.Count && nodes[nodeIndex] != null)
        {
            worldPoint = nodes[nodeIndex].position;
            return true;
        }

        worldPoint = transform.position;
        return false;
    }

    public int GetFirstRouteNodeIndex(int sequenceIndex)
    {
        List<Transform> nodes = GetSequenceNodes(sequenceIndex);
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] != null)
                return i;
        }

        return -1;
    }

    public int GetNextRouteNodeIndex(int sequenceIndex, int currentIndex)
    {
        List<Transform> nodes = GetSequenceNodes(sequenceIndex);
        int nextIndex = currentIndex + 1;
        while (nextIndex < nodes.Count)
        {
            if (nodes[nextIndex] != null)
                return nextIndex;

            nextIndex++;
        }

        if (!loopRoute)
            return -1;

        return GetFirstRouteNodeIndex(sequenceIndex);
    }

    public int GetNextRouteNodeIndexNoLoop(int sequenceIndex, int currentIndex)
    {
        List<Transform> nodes = GetSequenceNodes(sequenceIndex);
        int nextIndex = currentIndex + 1;
        while (nextIndex < nodes.Count)
        {
            if (nodes[nextIndex] != null)
                return nextIndex;

            nextIndex++;
        }

        return -1;
    }

    public int GetPreviousRouteNodeIndex(int sequenceIndex, int currentIndex)
    {
        List<Transform> nodes = GetSequenceNodes(sequenceIndex);
        int previousIndex = currentIndex - 1;
        while (previousIndex >= 0)
        {
            if (nodes[previousIndex] != null)
                return previousIndex;

            previousIndex--;
        }

        return -1;
    }

    public int GetPreviousRouteNodeIndexNoLoop(int sequenceIndex, int currentIndex)
    {
        List<Transform> nodes = GetSequenceNodes(sequenceIndex);
        int previousIndex = currentIndex - 1;
        while (previousIndex >= 0)
        {
            if (nodes[previousIndex] != null)
                return previousIndex;

            previousIndex--;
        }

        return -1;
    }

    public int GetLastRouteNodeIndex(int sequenceIndex)
    {
        List<Transform> nodes = GetSequenceNodes(sequenceIndex);
        for (int i = nodes.Count - 1; i >= 0; i--)
        {
            if (nodes[i] != null)
                return i;
        }

        return -1;
    }

    public int GetClosestRouteNodeIndex(int sequenceIndex, Vector3 worldPosition)
    {
        List<Transform> nodes = GetSequenceNodes(sequenceIndex);
        int bestIndex = -1;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < nodes.Count; i++)
        {
            Transform node = nodes[i];
            if (node == null)
                continue;

            float distance = (node.position - worldPosition).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private List<Transform> GetSequenceNodes(int sequenceIndex)
    {
        if (sequenceIndex >= 0 && sequenceIndex < routeSequences.Count && routeSequences[sequenceIndex] != null)
            return routeSequences[sequenceIndex].nodes;

        return s_emptyNodes;
    }

    private static readonly List<Transform> s_emptyNodes = new List<Transform>();

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.1f, 0.8f, 0.9f, 1f);
        if (routeSequences == null || routeSequences.Count == 0)
            return;

        for (int i = 0; i < routeSequences.Count; i++)
        {
            DrawSequence(routeSequences[i] != null ? routeSequences[i].nodes : null);
        }
    }

    private void DrawSequence(List<Transform> nodes)
    {
        if (nodes == null)
            return;

        Transform previous = null;
        Transform first = null;
        for (int i = 0; i < nodes.Count; i++)
        {
            Transform node = nodes[i];
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
