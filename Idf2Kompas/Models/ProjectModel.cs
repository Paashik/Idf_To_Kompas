using System.Collections.Generic;

namespace Idf2Kompas.Models
{
    public sealed class ProjectModel
    {
        public string AssemblyDesignation { get; set; }
        public string AssemblyName { get; set; }
        public string BoardDesignation { get; set; }
        public string BoardName { get; set; }
        public double BoardThickness { get; set; }
        public List<ProjectPlacement> Placements { get; } = new List<ProjectPlacement>();
        public List<ProjectBomRow> BomRows { get; } = new List<ProjectBomRow>();

    }

    public sealed class ProjectPlacement
    {
        public string Designator { get; set; }
        public string Body { get; set; }
        public double Xmm { get; set; }
        public double Ymm { get; set; }
        public double RotationDeg { get; set; }
        public bool IsBottomSide { get; set; }
    }

    public sealed class ProjectBomRow
    {
        public string Designator { get; set; }
        public string Body { get; set; }
        public string Comment { get; set; }
        public string StockCode { get; set; }
        public string Description { get; set; }
    }
}
