using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Idf2Kompas.Models;

namespace Idf2Kompas.Services
{
    public sealed class IdfWriter
    {
        public void WriteV3(IdfBoard board, string outBrd, string outPro, double signalHoleMinDiaMm)
        {
            var sb = new StringBuilder();
            sb.AppendLine("IDF 3.0");
            sb.AppendLine(".HEADER");
            sb.AppendLine(board.Outline.HeaderUnits == "THOU" ? "THOU" : "MM");
            sb.AppendLine(".END_HEADER");
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(board.Outline.RawBoardOutline))
            {
                sb.AppendLine(".BOARD_OUTLINE");
                sb.Append(board.Outline.RawBoardOutline.TrimEnd());
                sb.AppendLine();
                sb.AppendLine(".END_BOARD_OUTLINE");
                sb.AppendLine();
            }

            if (board.Holes != null && board.Holes.Count > 0)
            {
                var lines = new StringBuilder();
                foreach (var h in board.Holes)
                {
                    bool isMount = string.Equals(h.Type, "MTG", StringComparison.OrdinalIgnoreCase);
                    bool isSignal = !isMount;
                    if (isSignal && h.Dia < signalHoleMinDiaMm) continue;

                    var plating = string.IsNullOrWhiteSpace(h.Plating) ? "NPTH" : h.Plating;
                    lines.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0:0.###} {1:0.###} {2:0.###} {3} {4}",
                        h.X, h.Y, h.Dia, plating, (h.Type ?? "OTHER")));
                }
                var txt = lines.ToString();
                if (!string.IsNullOrWhiteSpace(txt))
                {
                    sb.AppendLine(".DRILLED_HOLES");
                    sb.Append(txt.TrimEnd());
                    sb.AppendLine();
                    sb.AppendLine(".END_DRILLED_HOLES");
                    sb.AppendLine();
                }
            }

            sb.AppendLine(".PLACEMENT");
            foreach (var p in board.Placements)
            {
                var geom = string.IsNullOrWhiteSpace(p.FootprintFromBom) ? p.FootprintFromIdf : p.FootprintFromBom;
                var part = string.IsNullOrWhiteSpace(p.Comment) ? p.PartNameFromIdf : p.Comment;
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0} \"{1}\" {2}", geom, part, p.RefDes));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:0.###} {1:0.###} {2:0.###} {3:0.###} {4} ECAD", p.X, p.Y, p.Z, p.RotDeg, p.Side));
            }
            sb.AppendLine(".END_PLACEMENT");

            File.WriteAllText(outBrd, sb.ToString(), Encoding.ASCII);

            var uniq = board.Placements
                .Select(p => string.IsNullOrWhiteSpace(p.FootprintFromBom) ? p.FootprintFromIdf : p.FootprintFromBom)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            var sp = new StringBuilder();
            sp.AppendLine("IDF 3.0");
            sp.AppendLine(".HEADER");
            sp.AppendLine(board.Outline.HeaderUnits == "THOU" ? "THOU" : "MM");
            sp.AppendLine(".END_HEADER");

            foreach (var fp in uniq)
            {
                var h = board.Placements
                    .FirstOrDefault(p => (string.IsNullOrWhiteSpace(p.FootprintFromBom) ? p.FootprintFromIdf : p.FootprintFromBom)
                                         .Equals(fp, StringComparison.OrdinalIgnoreCase) && p.HeightFromEmp.HasValue)
                    ?.HeightFromEmp ?? 1.0;

                sp.AppendLine(".ELECTRICAL");
                sp.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} \"GEN\" MM {1:0.###}", fp, h));
                sp.AppendLine("0 0  1 0");
                sp.AppendLine("0 0  0 1");
                sp.AppendLine("0 1  1 1");
                sp.AppendLine("1 0  1 1");
                sp.AppendLine(".END_ELECTRICAL");
            }

            File.WriteAllText(outPro, sp.ToString(), Encoding.ASCII);
        }
    }
}
