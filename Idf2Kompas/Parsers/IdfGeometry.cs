using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Idf2Kompas.Parsers
{
    public static class IdfGeometry
    {
        // Точка контура (мм)
        public struct Pt
        {
            public double X, Y;
            public Pt(double x, double y) { X = x; Y = y; }
        }

        // Контур платы + толщина
        public sealed class OutlineRings
        {
            // Толщина платы (мм)
            public double ThicknessMm;

            // Список колец; каждое кольцо — замкнутая полилиния из Pt
            public List<List<Pt>> Rings = new List<List<Pt>>();

            // Сырой текст блока .BOARD_OUTLINE — для диагностики
            public string RawBoardOutline { get; set; }
        }

        // Определение масштаба (MM / THOU / INCH → коэффициент к мм)
        public static double DetectUnitsScale(string brdText)
        {
            if (string.IsNullOrWhiteSpace(brdText)) return 1.0;
            var t = brdText.ToUpperInvariant();
            var i = t.IndexOf(".HEADER", StringComparison.Ordinal);
            if (i >= 0)
            {
                var j = t.IndexOf(".END_HEADER", i, StringComparison.Ordinal);
                var header = (j > i) ? t.Substring(i, j - i) : t.Substring(i);
                if (header.Contains("UNITS MM")) return 1.0;
                if (header.Contains("UNITS INCH")) return 25.4;
                if (header.Contains("UNITS THOU") || header.Contains("UNITS MIL")) return 0.0254;
            }
            return 1.0;
        }

        // Вырезает блок по тегам
        public static string ExtractBlock(string text, string startTag, string endTag)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            var i = text.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return string.Empty;
            var j = text.IndexOf(endTag, i, StringComparison.OrdinalIgnoreCase);
            if (j < 0) j = text.Length;
            return text.Substring(i + startTag.Length, j - (i + startTag.Length));
        }

        private static double ParseDoubleAny(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return double.NaN;
            s = s.Trim();
            double v;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            var s2 = s.Replace(',', '.');
            if (double.TryParse(s2, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out v)) return v;
            return double.NaN;
        }

        // === Парсер .BOARD_OUTLINE c поддержкой OWNED/UNOWNED, толщины и дуг (C# 7.3) ===
        public static OutlineRings ParseBoardOutline(string block, double scale)
        {
            var res = new OutlineRings
            {
                Rings = new List<List<Pt>>(),
                ThicknessMm = 0.0,
                RawBoardOutline = block
            };

            if (string.IsNullOrWhiteSpace(block))
                return res;

            // 1) Нормализуем строки (без комментариев)
            var lines = new List<string>();
            using (var sr = new StringReader(block))
            {
                string ln;
                while ((ln = sr.ReadLine()) != null)
                {
                    ln = ln.Trim();
                    if (ln.Length == 0) continue;
                    if (ln.StartsWith("#")) continue;
                    lines.Add(ln);
                }
            }
            if (lines.Count == 0) return res;

            int i = 0;

            // 2) OWNED/UNOWNED (опционально)
            if (i < lines.Count && IsOwnershipFlag(lines[i]))
                i++;

            // 3) Толщина — первая строка, содержащая РОВНО одно число
            for (; i < lines.Count; i++)
            {
                double tVal;
                if (TryParseSingleDouble(lines[i], out tVal))
                {
                    res.ThicknessMm = tVal * scale;
                    i++; // переходим к данным контура
                    break;
                }
            }

            // 4) Группируем точки по Loop: loop  x  y  [angle]
            var byLoop = new Dictionary<int, List<(double X, double Y, double A)>>();
            for (; i < lines.Count; i++)
            {
                var toks = Regex.Split(lines[i], @"\s+");
                if (toks.Length < 3) continue;

                int loop;
                double x, y, a = 0.0;

                if (!int.TryParse(toks[0], out loop)) continue;
                if (!double.TryParse(toks[1], NumberStyles.Float, CultureInfo.InvariantCulture, out x)) continue;
                if (!double.TryParse(toks[2], NumberStyles.Float, CultureInfo.InvariantCulture, out y)) continue;
                if (toks.Length >= 4)
                    double.TryParse(toks[3], NumberStyles.Float, CultureInfo.InvariantCulture, out a);

                x *= scale; y *= scale;

                List<(double X, double Y, double A)> list;
                if (!byLoop.TryGetValue(loop, out list))
                {
                    list = new List<(double X, double Y, double A)>();
                    byLoop[loop] = list;
                }
                list.Add((x, y, a));
            }

            // 5) Разворачиваем линии/дуги в полилинии
            foreach (var kv in byLoop)
            {
                var pts = kv.Value;
                if (pts == null || pts.Count < 2) continue;

                // Проверим, есть ли «дубликат замыкания» (последняя точка = первая)
                bool hasClosingDup =
                    Math.Abs(pts[0].X - pts[pts.Count - 1].X) < 1e-9 &&
                    Math.Abs(pts[0].Y - pts[pts.Count - 1].Y) < 1e-9;

                // Угол замыкания хранится в ПOСЛЕДНЕЙ строке (дуга от предпоследней к первой)
                double closingAngle = hasClosingDup ? pts[pts.Count - 1].A : 0.0;

                // Начинаем полилинию с первой точки
                var ring = new List<Pt>();
                ring.Add(new Pt(pts[0].X, pts[0].Y));

                // Основной проход: от второй до (последней или предпоследней, если есть дубликат)
                int lastIndex = hasClosingDup ? (pts.Count - 2) : (pts.Count - 1);
                for (int k = 1; k <= lastIndex; k++)
                {
                    var prev = pts[k - 1];
                    var cur = pts[k];

                    if (Math.Abs(cur.A) < 1e-12)
                    {
                        // Отрезок prev → cur
                        Pt last = ring[ring.Count - 1];
                        if (last.X != cur.X || last.Y != cur.Y)
                            ring.Add(new Pt(cur.X, cur.Y));
                    }
                    else
                    {
                        // Дуга prev → cur с включённым углом cur.A (знак: +CCW, −CW)
                        var arcPts = TesselateArcByChord(prev.X, prev.Y, cur.X, cur.Y, cur.A, 5.0, 0.2);
                        for (int m = 1; m < arcPts.Count; m++) // первая совпадает с prev
                            ring.Add(arcPts[m]);
                    }
                }

                // ЯВНО обработаем замыкающий сегмент (предпоследняя → первая) по углу последней строки
                if (hasClosingDup)
                {
                    var prev = pts[pts.Count - 2];  // предпоследняя
                    var first = pts[0];             // первая

                    if (Math.Abs(closingAngle) < 1e-12)
                    {
                        // Прямая prev → first
                        Pt last = ring[ring.Count - 1];
                        if (Math.Abs(last.X - first.X) > 1e-9 || Math.Abs(last.Y - first.Y) > 1e-9)
                            ring.Add(new Pt(first.X, first.Y));
                    }
                    else
                    {
                        // Дуга prev → first с включённым углом closingAngle
                        var arcPts = TesselateArcByChord(prev.X, prev.Y, first.X, first.Y, closingAngle, 5.0, 0.2);
                        for (int m = 1; m < arcPts.Count; m++)
                            ring.Add(arcPts[m]);
                    }
                }

                // На всякий случай — замкнём, если вдруг не замкнулось
                if (ring.Count >= 2)
                {
                    Pt f = ring[0];
                    Pt l = ring[ring.Count - 1];
                    if (Math.Abs(f.X - l.X) > 1e-9 || Math.Abs(f.Y - l.Y) > 1e-9)
                        ring.Add(new Pt(f.X, f.Y));
                }

                ring = SanitizeRing(ring, 1e-9);
                if (ring.Count >= 3)
                    res.Rings.Add(ring);
            }

            return res;
        }

        // ———— Вспомогательные ————

        private static bool IsOwnershipFlag(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;

            double dummy;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out dummy))
                return false;

            var up = s.Trim().ToUpperInvariant();
            return up == "OWNED" || up == "UNOWNED";
        }

        private static bool TryParseSingleDouble(string s, out double value)
        {
            value = 0.0;
            if (string.IsNullOrEmpty(s)) return false;

            var toks = Regex.Split(s.Trim(), @"\s+");
            if (toks.Length != 1) return false;

            return double.TryParse(toks[0], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        // Аппроксимация дуги по хорде и включённому углу (ОСТАВИТЬ ОДНУ версию!)
        private static List<Pt> TesselateArcByChord(
            double x0, double y0, double x1, double y1,
            double includeAngleDeg,
            double maxDegStep,
            double maxChord)
        {
            var result = new List<Pt>();

            double phi = Math.Abs(includeAngleDeg) * Math.PI / 180.0;
            if (phi < 1e-9)
            {
                result.Add(new Pt(x0, y0));
                result.Add(new Pt(x1, y1));
                return result;
            }

            double vx = x1 - x0, vy = y1 - y0;
            double c = Math.Sqrt(vx * vx + vy * vy);
            if (c < 1e-12)
            {
                result.Add(new Pt(x0, y0));
                result.Add(new Pt(x1, y1));
                return result;
            }

            double R = c / (2.0 * Math.Sin(phi / 2.0));
            double mx = (x0 + x1) * 0.5, my = (y0 + y1) * 0.5;
            double nx = -vy / c, ny = vx / c;
            double d = Math.Sqrt(Math.Max(R * R - (c * 0.5) * (c * 0.5), 0.0));
            double sgn = includeAngleDeg >= 0 ? 1.0 : -1.0;
            double cx = mx + sgn * d * nx;
            double cy = my + sgn * d * ny;

            double a0 = Math.Atan2(y0 - cy, x0 - cx);
            double a1 = a0 + (includeAngleDeg * Math.PI / 180.0);

            double stepByDeg = Math.Min(maxDegStep, Math.Abs(includeAngleDeg));
            int nByDeg = Math.Max(2, (int)Math.Ceiling(Math.Abs(includeAngleDeg) / stepByDeg));

            double maxDeltaByChord = 2.0 * Math.Asin(Math.Min(1.0, maxChord / (2.0 * R)));
            if (maxDeltaByChord < 1e-6) maxDeltaByChord = 1e-6;
            int nByChord = Math.Max(2, (int)Math.Ceiling(Math.Abs(a1 - a0) / maxDeltaByChord));

            int n = Math.Max(nByDeg, nByChord);

            for (int i = 0; i <= n; i++)
            {
                double t = (double)i / n;
                double a = a0 + t * (a1 - a0);
                result.Add(new Pt(cx + R * Math.Cos(a), cy + R * Math.Sin(a)));
            }

            return result;
        }

        // Чистка кольца от нулевых сегментов / дублей (ОСТАВИТЬ ОДНУ версию!)
        private static List<Pt> SanitizeRing(List<Pt> ring, double eps)
        {
            var res = new List<Pt>();
            if (ring == null) return res;

            Pt? prev = null;
            for (int i = 0; i < ring.Count; i++)
            {
                var p = ring[i];
                if (!prev.HasValue ||
                    Math.Abs(p.X - prev.Value.X) > eps ||
                    Math.Abs(p.Y - prev.Value.Y) > eps)
                {
                    res.Add(p);
                    prev = p;
                }
            }

            if (res.Count >= 2)
            {
                var f = res[0];
                var l = res[res.Count - 1];
                if (Math.Abs(f.X - l.X) <= eps && Math.Abs(f.Y - l.Y) <= eps)
                    res.RemoveAt(res.Count - 1);
            }
            return res;
        }

        // Необязательный вспомогательный (не используется прямым кодом, но не конфликтует)
        private static IEnumerable<Pt> ApproxArc(Pt a, Pt b, double angleDeg, double maxChordMm)
        {
            var pts = new List<Pt>();
            double ang = angleDeg * Math.PI / 180.0;
            double chord = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            if (Math.Abs(ang) < 1e-6 || chord < 1e-9) { pts.Add(b); return pts; }

            double R = chord / (2.0 * Math.Sin(Math.Abs(ang) / 2.0));
            double mx = (a.X + b.X) / 2.0, my = (a.Y + b.Y) / 2.0;
            double vx = (b.X - a.X) / chord, vy = (b.Y - a.Y) / chord;
            double nx = -vy, ny = vx;
            double h = Math.Sqrt(Math.Max(R * R - (chord * chord) / 4.0, 0.0));
            double cx = mx + Math.Sign(ang) * h * nx;
            double cy = my + Math.Sign(ang) * h * ny;

            double a0 = Math.Atan2(a.Y - cy, a.X - cx);
            double step = Math.Acos(Math.Max(-1.0, Math.Min(1.0, 1.0 - (maxChordMm * maxChordMm) / (2 * R * R))));
            if (double.IsNaN(step) || step <= 0) step = 5.0 * Math.PI / 180.0;

            int n = Math.Max(1, (int)Math.Ceiling(Math.Abs(ang) / step));
            for (int i = 1; i <= n; i++)
            {
                double t = a0 + (ang * i) / n;
                pts.Add(new Pt(cx + R * Math.Cos(t), cy + R * Math.Sin(t)));
            }
            return pts;
        }

        // Парсер отверстий (X Y Dia) с простым хейуристическим свитчем формата
        public static List<(double X, double Y, double Dia)> ParseHoles(string holesBlock, double scaleToMm)
        {
            var list = new List<(double, double, double)>();
            if (string.IsNullOrWhiteSpace(holesBlock)) return list;

            var lines = holesBlock.Replace("\r\n", "\n").Replace("\r", "\n")
                                  .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var raw in lines)
            {
                var s = raw.Trim();
                if (s.Length == 0 || s.StartsWith(".")) break;

                var nums = Regex.Matches(s, @"-?\d+(?:\.\d+)?", RegexOptions.CultureInvariant);
                if (nums.Count < 3) continue;

                double a = double.Parse(nums[0].Value, CultureInfo.InvariantCulture) * scaleToMm;
                double b = double.Parse(nums[1].Value, CultureInfo.InvariantCulture) * scaleToMm;
                double c = double.Parse(nums[2].Value, CultureInfo.InvariantCulture) * scaleToMm;

                double dia, x, y;
                if (a < 20 && b > a && c > a) { dia = a; x = b; y = c; } // Dia X Y
                else { x = a; y = b; dia = c; }                          // X Y Dia

                list.Add((x, y, dia));
            }
            return list;
        }
    }
}
