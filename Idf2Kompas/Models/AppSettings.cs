namespace Idf2Kompas.Models
{
    public sealed class AppSettings
    {
        public string BrdPath { get; set; }
        public string ProPath { get; set; }
        public string CsvPath { get; set; }

        public string BomRefDesName { get; set; } = "Designator";
        public string BomPNName { get; set; } = "Stock Code";
        public string BomCommentName { get; set; } = "Comment";
        public string BomBodyName { get; set; } = "Body";
        public string BomFootprintName { get; set; } = "Footprint";
        public string BomDescriptionName { get; set; } = "Description";

        public string ModelNameSource { get; set; } = "Body";
        public double SignalHoleMinDiaMm { get; set; } = 0.0;
        public string LibDir { get; set; }
        public string SaveBoardDir { get; set; }
        public string SaveAsmDir { get; set; }
    }
}
