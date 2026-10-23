using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tycoon
{
    // A packed order owns a snapshot; clearing the store receipt cannot change it.
    [Serializable]
    public sealed class ReceiptData
    {
        [Serializable]
        public sealed class Line
        {
            public string resourceId;
            public string buildingId;
            public int quantity;
            public GameObject receiptPhotoPrefab;
        }

        [SerializeField] private List<Line> lines = new List<Line>();
        public IReadOnlyList<Line> Lines => lines;

        public ReceiptData(IEnumerable<Line> source)
        {
            foreach (Line line in source)
                lines.Add(new Line
                {
                    resourceId = line.resourceId,
                    buildingId = line.buildingId,
                    quantity = line.quantity,
                    receiptPhotoPrefab = line.receiptPhotoPrefab
                });
        }

        public bool CanDeliverTo(Building building)
        {
            if (!DeliveryView.IsReady(building) || lines.Count == 0)
                return false;
            // A mixed receipt can only be delivered to a building shared by every line.
            foreach (Line line in lines)
                if (line.quantity <= 0 || line.buildingId != building.BuildingId)
                    return false;
            return true;
        }
    }
}
