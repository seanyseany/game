using System;
using System.Collections.Generic;

namespace Tycoon
{
    // Stable IDs only. Unity instance IDs and prefab references do not survive a restart.
    [Serializable]
    public sealed class SavedBoxResource
    {
        public string resourceId;
        public string buildingId;
        public int quantity;
    }

    [Serializable]
    public sealed class SavedBoxOrder
    {
        public string id;
        public List<SavedBoxResource> resources = new List<SavedBoxResource>();

        internal bool IsValid => !string.IsNullOrEmpty(id) && resources != null && resources.Count > 0 &&
            resources.TrueForAll(line => line != null && !string.IsNullOrEmpty(line.resourceId) &&
                !string.IsNullOrEmpty(line.buildingId) && line.quantity > 0);

        internal static SavedBoxOrder FromReceipt(string id, ReceiptData receipt)
        {
            var order = new SavedBoxOrder { id = id };
            foreach (ReceiptData.Line line in receipt.Lines)
                order.resources.Add(new SavedBoxResource { resourceId = line.resourceId, buildingId = line.buildingId, quantity = line.quantity });
            return order;
        }

        internal ReceiptData ToReceipt()
        {
            var lines = new List<ReceiptData.Line>(resources.Count);
            foreach (SavedBoxResource resource in resources)
                lines.Add(new ReceiptData.Line { resourceId = resource.resourceId, buildingId = resource.buildingId, quantity = resource.quantity });
            return new ReceiptData(lines);
        }

        internal SavedBoxOrder Copy() => FromReceipt(id, ToReceipt());
    }

    [Serializable]
    public sealed class BoxInventoryState
    {
        public string inventoryId;
        public List<SavedBoxOrder> boxes = new List<SavedBoxOrder>();
    }
}
