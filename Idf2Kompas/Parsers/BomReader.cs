using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using Idf2Kompas.Models;

namespace Idf2Kompas.Parsers
{
    public sealed class BomReader
    {
        // ===================== ЗАГРУЗКА CSV =====================
        // Возвращает DataTable с исходными заголовками (ничего не переименовываем)
        public DataTable LoadCsv(string path, BomColumnMap map, Encoding enc)
        {
            var dt = new DataTable();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return dt;

            using (var sr = new StreamReader(path, enc ?? Encoding.UTF8, true))
            {
                string header = sr.ReadLine();
                if (header == null) return dt;

                var headers = SplitCsvLine(header);
                foreach (var h in headers)
                    dt.Columns.Add(h);

                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    var cells = SplitCsvLine(line);
                    // выравнивание размеров
                    while (cells.Count < dt.Columns.Count) cells.Add(string.Empty);
                    while (cells.Count > dt.Columns.Count) cells.RemoveAt(cells.Count - 1);
                    dt.Rows.Add(cells.ToArray());
                }
            }
            return dt;
        }

        // ================= ОБОГАЩЕНИЕ ИЗ BOM ==================
        // Не трогаем BRD-поля (Comment/FootprintFromIdf), только добавляем BOM-поля.
        public static void EnrichWithBom(IdfBoard board, DataTable bom)
        {
            if (board == null || bom == null || bom.Columns.Count == 0 || board.Placements == null)
                return;

            // Нормализация заголовков: убираем пробелы/знаки, нижний регистр
            string Norm(string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return string.Empty;
                var sb = new StringBuilder(s.Length);
                foreach (var ch in s)
                    if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
                return sb.ToString();
            }

            // Карта "нормализованное имя" -> индекс
            var colByNorm = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < bom.Columns.Count; i++)
                colByNorm[Norm(bom.Columns[i].ColumnName)] = i;

            int FindCol(params string[] candidates)
            {
                foreach (var c in candidates)
                {
                    var key = Norm(c);
                    if (colByNorm.TryGetValue(key, out var idx)) return idx;
                }
                return -1;
            }

            // Находим ключевые колонки
            int idxRef = FindCol("Designator", "RefDes", "Reference", "Ref", "Позиция", "Обозначение");
            int idxPN = FindCol("Stock Code", "StockCode", "PN", "PartNumber", "Part No", "PartNo", "Код", "Артикул");
            int idxMPN = FindCol("Manufacturer P/N", "ManufacturerPN", "Mfr P/N", "MPN", "Производитель P/N");
            int idxDesc = FindCol("Description", "Наименование", "Name", "Описание");
            int idxBody = FindCol("Body", "Корпус", "Типоразмер");
            int idxType = FindCol("Type", "Тип");

            // Строим быстрый индекс по designator → DataRow
            // Учтём, что в одной ячейке может быть "R1,R2" или "R1 R2"
            var index = new Dictionary<string, DataRow>(StringComparer.OrdinalIgnoreCase);
            if (idxRef >= 0)
            {
                foreach (DataRow r in bom.Rows)
                {
                    var cell = (r[idxRef] ?? string.Empty).ToString();
                    var tokens = cell
                        .Replace(';', ' ').Replace(',', ' ')
                        .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var t in tokens)
                    {
                        var key = t.Trim();
                        if (!index.ContainsKey(key))
                            index[key] = r; // берём первую встречу
                    }
                }
            }

            string Get(DataRow r, int idx)
            {
                if (r == null || idx < 0 || idx >= r.Table.Columns.Count) return null;
                var v = r[idx];
                return v == null ? null : v.ToString().Trim();
            }

            foreach (var p in board.Placements)
            {
                if (p == null || string.IsNullOrWhiteSpace(p.RefDes)) continue;

                if (index.TryGetValue(p.RefDes.Trim(), out var row))
                {
                    // Только дополняем BOM-поля. Ничего из BRD НЕ затираем.
                    var pn = Get(row, idxPN);
                    var mpn = Get(row, idxMPN);
                    var desc = Get(row, idxDesc);
                    var body = Get(row, idxBody);
                    var type = Get(row, idxType);

                    if (!string.IsNullOrWhiteSpace(pn)) p.PN = pn;
                    if (!string.IsNullOrWhiteSpace(mpn)) p.ManufacturerPN = mpn;
                    if (!string.IsNullOrWhiteSpace(desc)) p.Description = desc;
                    if (!string.IsNullOrWhiteSpace(body)) p.Body = body;
                    if (!string.IsNullOrWhiteSpace(type)) p.Type = type;

                    // Footprint(BOM) и Comment(BOM) намеренно НЕ заполняем,
                    // чтобы не перезатереть значения из BRD.
                }
            }
        }

        // ====================== CSV split ======================
        private static List<string> SplitCsvLine(string line)
        {
            var res = new List<string>();
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
                else if (c == ',' && !inQ) { res.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            res.Add(sb.ToString());
            return res;
        }
    }
}
