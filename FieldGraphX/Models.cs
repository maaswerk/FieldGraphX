using System;
using System.Collections.Generic;
using System.Drawing;

namespace FieldGraphX.Models
{
    public class FlowUsage
    {
        public string FlowName { get; set; }
        public bool IsFieldUsedAsTrigger { get; set; }
        public bool IsFieldSet { get; set; }
        public string SetField { get; set; }
        public string FlowUrl { get; set; }
        public Trigger Trigger { get; set; }
        public List<FlowUsage> Parents { get; set; } = new List<FlowUsage>();
        public Guid FlowID { get; set; }
        public RectangleF Button { get; internal set; }
    }

    public class Trigger
    {
        public string Name { get; set; }
        public string Entity { get; set; }
        public string Field { get; set; }
    }

    public class FlowHierarchyNode
    {
        public FlowUsage Flow { get; set; }
        public List<FlowHierarchyNode> ChildNodes { get; set; } = new List<FlowHierarchyNode>();
    }

    public class FlowHierarchy
    {
        public string EntityName { get; set; }
        public string FieldName { get; set; }
        public List<FlowHierarchyNode> RootNodes { get; set; } = new List<FlowHierarchyNode>();
    }

    public class PositionedFlow
    {
        public FlowUsage Flow { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
    }
}
