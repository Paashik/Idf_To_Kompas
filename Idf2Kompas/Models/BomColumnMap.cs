namespace Idf2Kompas.Models
{
    public sealed class BomColumnMap
    {
        public string RefDes { get; set; }
        public string PN { get; set; }                  // Stock Code
        public string Comment { get; set; }
        public string Body { get; set; }
        public string Footprint { get; set; }
        public string Description { get; set; }
        public string ManufacturerPN { get; set; }      // Manufacturer P/N
        public string Type { get; set; }                // Type
    }
}
