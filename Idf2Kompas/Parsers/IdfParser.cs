using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Idf2Kompas.Models;

namespace Idf2Kompas.Parsers
{
    public sealed class IdfParser
    {
        public IdfBoard Parse(string brdPath, string proPath, IdfParseOptions opts)
        {
            var board = new IdfBoard();
            var proHeights = ParseEmpHeights(proPath);
            ParseBrd(brdPath, board, proHeights, opts ?? new IdfParseOptions());
            return board;
        }

        private static Dictionary<string, double> ParseEmpHeights(string proPath)
        {
            var dict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(proPath) || !File.Exists(proPath))
                return dict;

            var text = File.ReadAllText(proPath, Encoding.GetEncoding(1251));
            var rx = new Regex(
                @"\.(?:ELECTRICAL|MECHANICAL)\s*(?<geom>\S+)\s+""(?:[^""]*)""\s+(?:MM|THOU)\s+(?<h>[0-9]+(?:\.[0-9]+)?)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);

            foreach (Match m in rx.Matches(text))
            {
                var geom = m.Groups["geom"].Value.Trim();
                var h = double.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture);
                dict[geom] = h;
            }
            return dict;
        }

        private static void ParseBrd(string brdPath, IdfBoard board, Dictionary<string, double> proHeights, IdfParseOptions opts)
        {
            if (string.IsNullOrWhiteSpace(brdPath) || !File.Exists(brdPath))
                return;

            var text = File.ReadAllText(brdPath, Encoding.GetEncoding(1251));

            // HEADER → единицы
            var mh = Regex.Match(text, @"\.HEADER\s*(?<hbody>.*?)\.END_HEADER", RegexOptions.Singleline);
            if (mh.Success)
            {
                var hbody = mh.Groups["hbody"].Value;
                var un = Regex.Match(hbody, @"\b(MM|THOU)\b", RegexOptions.IgnoreCase);
                if (un.Success)
                    board.Outline.HeaderUnits = un.Groups[1].Value.ToUpperInvariant() == "THOU" ? "THOU" : "MM";
            }

            // BOARD_OUTLINE → толщина (первая числовая строка блока)
            var mo = Regex.Match(text, @"\.BOARD_OUTLINE(?<b>.*?)\.END_BOARD_OUTLINE", RegexOptions.Singleline);
            if (mo.Success)
            {
                board.Outline.RawBoardOutline = mo.Groups["b"].Value;

                using (var sr = new StringReader(board.Outline.RawBoardOutline))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        line = line.Trim();
                        if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(".")) continue;

                        // Собираем ВСЕ числа в строке
                        var nums = Regex.Matches(line, @"[-+]?\d+(?:[.,]\d+)?");
                        if (nums.Count == 0) continue;

                        // Если в строке 2+ числа — в IDF часто: <line_width> <board_thickness>
                        // Берём второе как толщину, иначе первое.
                        double val;
                        var raw = (nums.Count >= 2 ? nums[1].Value : nums[0].Value).Replace(',', '.');
                        if (double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out val))
                        {
                            if (string.Equals(board.Outline.HeaderUnits, "THOU", StringComparison.OrdinalIgnoreCase))
                                val *= 0.0254; // thou→мм

                            board.Outline.ThicknessMm = val;
                        }
                        break; // толщина найдена — выходим
                    }
                }
            }

            // DRILLED_HOLES → список отверстий
            var mhole = Regex.Match(text, @"\.DRILLED_HOLES(?<h>.*?)\.END_DRILLED_HOLES", RegexOptions.Singleline);
            if (mhole.Success)
            {
                board.Outline.RawDrilledHoles = mhole.Groups["h"].Value;
                using (var sr = new StringReader(board.Outline.RawDrilledHoles))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        line = line.Trim();
                        if (line.Length == 0 || line.StartsWith("#")) continue;
                        var toks = Regex.Split(line, @"\s+");
                        if (toks.Length < 3) continue;

                        if (!double.TryParse(toks[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) continue;
                        if (!double.TryParse(toks[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) continue;
                        if (!double.TryParse(toks[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) continue;

                        string plating = null, typ = null;
                        if (toks.Length >= 4) plating = toks[3].ToUpperInvariant();
                        if (toks.Length >= 5) typ = toks[4].ToUpperInvariant();

                        if (plating == "PLATED" || plating == "PTH") plating = "PTH";
                        else if (plating == "NON_PLATED" || plating == "NPTH") plating = "NPTH";
                        else plating = null;

                        if (typ == "MOUNT" || typ == "MOUNTING") typ = "MTG";
                        else if (typ == "TOOLING") typ = "TOOL";
                        else if (typ == "THRU_PIN") typ = "PIN";
                        else if (typ == "VIA" || typ == "PIN" || typ == "MTG" || typ == "TOOL") { }
                        else typ = "OTHER";

                        board.Holes.Add(new IdfHole { X = x, Y = y, Dia = d, Plating = plating, Type = typ });
                    }
                }
            }

            // PLACEMENT → по двум строкам на компонент
            var mplace = Regex.Match(text, @"\.PLACEMENT(?<body>.*?)\.END_PLACEMENT", RegexOptions.Singleline | RegexOptions.CultureInvariant);
            if (mplace.Success)
            {
                var body = mplace.Groups["body"].Value;
                var lines = new System.Collections.Generic.List<string>();
                using (var sr = new StringReader(body))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        line = line.Trim();
                        if (line.Length == 0) continue;
                        lines.Add(line);
                    }
                }

                for (int i = 0; i < lines.Count - 1; i += 2)
                {
                    var head = lines[i];
                    var coords = lines[i + 1];

                    var mhdr = Regex.Match(head, @"^(?<geom>\S+)\s+""(?<part>[^""\r\n]+)""\s+(?<ref>\S+)");
                    if (!mhdr.Success) continue;

                    var geom = mhdr.Groups["geom"].Value.Trim();
                    var part = mhdr.Groups["part"].Value.Trim();
                    var rf = mhdr.Groups["ref"].Value.Trim();

                    var cs = coords.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    double x = 0, y = 0, z = 0, rot = 0; string side = "TOP";
                    if (cs.Length >= 5)
                    {
                        double.TryParse(cs[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x);
                        double.TryParse(cs[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y);
                        double.TryParse(cs[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z);
                        double.TryParse(cs[3], NumberStyles.Float, CultureInfo.InvariantCulture, out rot);
                        side = cs[4].ToUpperInvariant();
                    }

                    var p = new IdfPlacement
                    {
                        FootprintFromIdf = geom,
                        PartNameFromIdf = part,
                        RefDes = rf,
                        X = x,
                        Y = y,
                        Z = z,
                        RotDeg = rot,
                        Side = side,
                        HeightFromEmp = (proHeights != null && proHeights.TryGetValue(geom, out var h)) ? (double?)h : null
                    };

                    if (p.Side == "BOTTOM")
                    {
                        switch ((opts?.BottomRotationMode ?? "AsIs"))
                        {
                            case "Negate": p.RotDeg = -p.RotDeg; break;
                            case "360Minus": p.RotDeg = 360.0 - p.RotDeg; break;
                            default: break;
                        }
                    }
                    board.Placements.Add(p);
                }
            }
        }
    }
}
