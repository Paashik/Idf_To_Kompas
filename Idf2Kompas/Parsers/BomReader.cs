using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text;
using Idf2Kompas.Models;

namespace Idf2Kompas.Parsers
{
    public sealed class BomReader
    {
        public DataTable LoadCsv(string path, BomColumnMap map, Encoding enc)
        {
            var dt = new DataTable();
            if (!File.Exists(path)) return dt;

            var lines = File.ReadAllLines(path, enc);
            if (lines.Length == 0) return dt;

            var header = SplitCsvLine(lines[0]);
            foreach (var h in header) dt.Columns.Add(h);

            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var cells = SplitCsvLine(lines[i]);
                while (cells.Count < dt.Columns.Count) cells.Add("");
                dt.Rows.Add(cells.ToArray());
            }
            return dt;
        }

        public static void EnrichWithBom(IdfBoard board, DataTable bom)
        {
            if (board == null || bom == null) return;

            var cols = new System.Collections.Generic.Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < bom.Columns.Count; i++) cols[bom.Columns[i].ColumnName] = i;

            int idxRef = FindColumnIndex(cols, "Designator", "Position", "RefDes");
            int idxPN = FindColumnIndex(cols, "Stock Code", "PN", "PartNumber");
            int idxCmt = FindColumnIndex(cols, "Comment", "Value", "Name");
            int idxBody = FindColumnIndex(cols, "Body", "Package", "Model");
            int idxFpr = FindColumnIndex(cols, "Footprint", "Footprint(BOM)");
            int idxDesc = FindColumnIndex(cols, "Description", "PartName");

            if (idxRef < 0) return;

            var map = new System.Collections.Generic.Dictionary<string, DataRow>(StringComparer.OrdinalIgnoreCase);
            foreach (DataRow r in bom.Rows)
            {
                var refdes = Get(r, idxRef);
                if (!string.IsNullOrWhiteSpace(refdes) && !map.ContainsKey(refdes))
                    map[refdes] = r;
            }

            foreach (var p in board.Placements)
            {
                DataRow r;
                if (!string.IsNullOrWhiteSpace(p.RefDes) && map.TryGetValue(p.RefDes, out r))
                {
                    p.PN = Get(r, idxPN);
                    p.Comment = Get(r, idxCmt);
                    p.Body = string.IsNullOrWhiteSpace(p.Body) ? Get(r, idxBody) : p.Body;
                    p.FootprintFromBom = Get(r, idxFpr);
                    p.PartNameFromIdf = string.IsNullOrWhiteSpace(p.PartNameFromIdf) ? Get(r, idxDesc) : p.PartNameFromIdf;
                }
            }
        }

        private static int FindColumnIndex(System.Collections.Generic.Dictionary<string, int> cols, params string[] names)
        {
            foreach (var n in names)
                if (cols.TryGetValue(n, out var i)) return i;
            return -1;
        }

        private static string Get(DataRow r, int idx) => (idx >= 0) ? (r[idx]?.ToString() ?? "") : "";

        private static System.Collections.Generic.List<string> SplitCsvLine(string line)
        {
            var res = new System.Collections.Generic.List<string>();
            if (line == null) return res;
            var sb = new StringBuilder();
            bool inQ = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '\"')
                {
                    if (inQ && i + 1 < line.Length && line[i + 1] == '\"') { sb.Append('\"'); i++; }
                    else inQ = !inQ;
                }
                else if (c == ',' && !inQ)
                {
                    res.Add(sb.ToString()); sb.Clear();
                }
                else sb.Append(c);
            }
            res.Add(sb.ToString());
            return res;
        }
    }
}
