// .NET Framework 4.7.2, C# 7.3
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

using Kompas6API5;            // API5: ksDocument3D, ksPart, ksPlacement, эскизы/экструзии
using Kompas6Constants3D;     // Obj3dType, Direction_Type, End_Type
using Kompas6Constants;       // Part_Type

using static Idf2Kompas.Parsers.IdfGeometry; // OutlineRings, Pt

using Idf2Kompas.Models;     // ProjectModel, AppSettings, ProjectPlacement, IdfBoard, ...
using Idf2Kompas.Parsers;    // IdfGeometry

namespace Idf2Kompas.Services
{
    public static class KompasService
    {
        // ============================= ПУБЛИЧНЫЙ ВХОД =============================
        public static void BuildAssemblyInKompas(
            ProjectModel model,
            AppSettings settings,
            OutlineRings outline,
            List<(double X, double Y, double Dia)> holes,
            string boardOutPath,
            string asmOutPath)
        {
            if (outline == null || outline.Rings == null || outline.Rings.Count == 0)
            {
                MessageBox.Show("Контур платы пуст.", "КОМПАС", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var usedThickness = outline.ThicknessMm > 0 ? outline.ThicknessMm : model.BoardThickness;
            if (usedThickness <= 0)
            {
                MessageBox.Show("Толщина платы не задана.", "КОМПАС", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Фильтр отверстий по минимальному диаметру
            var filtHoles = new List<(double X, double Y, double Dia)>();
            double minDia = settings != null ? Math.Max(0.0, settings.SignalHoleMinDiaMm) : 0.0;
            if (holes != null)
            {
                for (int i = 0; i < holes.Count; i++)
                {
                    var h = holes[i];
                    if (h.Dia >= minDia) filtHoles.Add(h);
                }
            }

            // Запуск Kompas API5
            KompasObject kompas = null;
            try
            {
                var t = Type.GetTypeFromProgID("KOMPAS.Application.5");
                kompas = (KompasObject)Activator.CreateInstance(t);
                kompas.Visible = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось запустить Kompas API5: " + ex.Message, "КОМПАС", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 1) Деталь платы
            string boardM3D = BuildBoardM3D(kompas, outline, usedThickness, filtHoles, boardOutPath);
            if (string.IsNullOrEmpty(boardM3D) || !File.Exists(boardM3D))
            {
                MessageBox.Show("Не удалось построить/сохранить деталь платы (*.m3d).", "КОМПАС", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 2) Документ сборки API5
            var doc3D = (ksDocument3D)kompas.Document3D();
            if (!doc3D.Create(false, false))
            {
                MessageBox.Show("Не удалось создать документ сборки.", "КОМПАС", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var placeLog = new StringBuilder();
            LogAppend(placeLog, "=== PLACEMENT TRACE (API5 placement) ===");

            // Инфо по плате
            double minX, minY, maxX, maxY;
            ComputeOutlineBBox(outline, out minX, out minY, out maxX, out maxY);
            LogAppend(placeLog, string.Format("Board BBox mm: X=[{0:0.###}..{1:0.###}] Y=[{2:0.###}..{3:0.###}] T={4:0.###}",
                                              minX, maxX, minY, maxY, usedThickness));

            // Вставим плату как компонент
            var boardComp = (ksPart)doc3D.GetPart((short)Part_Type.pNew_Part);
            bool okInsBoard = false;
            try { okInsBoard = doc3D.SetPartFromFileEx(boardM3D, boardComp, true, true); } catch { }
            if (!okInsBoard) { try { okInsBoard = doc3D.SetPartFromFile(boardM3D, boardComp); } catch { } }
            if (!okInsBoard)
            {
                MessageBox.Show("Не удалось вставить плату как компонент.", "КОМПАС", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Корпуса
            int placed = 0, skipped = 0;
            if (model.Placements != null)
            {
                for (int i = 0; i < model.Placements.Count; i++)
                {
                    var p = model.Placements[i];

                    // === ВАЖНО: выбор имени модели строго по настройке ===
                    // (в ProjectPlacement гарантированно есть Body из BOM; полей из BRD тут нет)
                    var modelName = ResolveModelNameBySettings(p, settings);
                    var libPath = global::Idf2Kompas.Services.LibraryHelper.FindModelPath(settings.LibDir, modelName);

                    LogAppend(placeLog, string.Format(
                        "RAW→ASM: {0} body={1} modelName={2} Xmm={3} Ymm={4} Z={5} Side={6} RotCW={7}",
                        p.Designator, p.Body, modelName, p.Xmm, p.Ymm,
                        p.IsBottomSide ? -usedThickness : 0.0,
                        p.IsBottomSide ? "BOTTOM" : "TOP",
                        p.RotationDeg));

                    if (string.IsNullOrWhiteSpace(libPath) || !File.Exists(libPath))
                    {
                        LogAppend(placeLog, "SKIP(no model): " + p.Designator + " → " + (modelName ?? ""));
                        skipped++;
                        continue;
                    }

                    // Вставка API5
                    var comp = (ksPart)doc3D.GetPart((short)Part_Type.pNew_Part);
                    bool ok5 = false;
                    try { ok5 = doc3D.SetPartFromFileEx(libPath, comp, true, true); } catch { }
                    if (!ok5) { try { ok5 = doc3D.SetPartFromFile(libPath, comp); } catch { } }
                    if (!ok5)
                    {
                        LogAppend(placeLog, "FAIL(API5 insert): " + p.Designator);
                        skipped++;
                        continue;
                    }

                    // Позиционируем через ksPlacement
                    double z = p.IsBottomSide ? -usedThickness : 0.0;
                    bool placedOk = TryPlaceComponentApi5_Typed(comp, p.Xmm, p.Ymm, z, p.RotationDeg, placeLog);
                    if (placedOk)
                        placed++;
                    else
                    {
                        LogAppend(placeLog, "OK5 inserted, PLACE FAIL (stays at origin): " + p.Designator);
                        placed++; // компонент вставлен, но остался в (0,0,0)
                    }
                }
            }

            // Сохраняем сборку
            try
            {
                if (!string.IsNullOrWhiteSpace(asmOutPath))
                {
                    var asmDir = Path.GetDirectoryName(asmOutPath);
                    if (!string.IsNullOrEmpty(asmDir))
                        Directory.CreateDirectory(asmDir);
                    doc3D.SaveAs(asmOutPath);
                }
            }
            catch { }

            try { doc3D.RebuildDocument(); } catch { }

            // Лог рядом со сборкой
            try
            {
                if (!string.IsNullOrWhiteSpace(asmOutPath))
                {
                    var asmDir = Path.GetDirectoryName(asmOutPath);
                    var logPath = string.IsNullOrEmpty(asmDir)
                        ? ".placement.log.txt"
                        : Path.Combine(asmDir, ".placement.log.txt");
                    File.WriteAllText(logPath, placeLog.ToString(), Encoding.UTF8);
                }
            }
            catch { }

            MessageBox.Show(
                "Сборка создана.\n" +
                string.Format("Плата: T = {0:0.###} мм, контуров: {1}\n", usedThickness, outline.Rings.Count) +
                string.Format("Отверстий (после фильтра): {0}\n", filtHoles.Count) +
                string.Format("Корпусов: вставлено {0}, пропущено {1}\n", placed, skipped) +
                string.Format("Файлы:\n Плата: {0}\n Сборка: {1}", boardM3D, asmOutPath),
                "КОМПАС — готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ======================== ПОСТРОЕНИЕ ДЕТАЛИ ПЛАТЫ ========================
        private static string BuildBoardM3D(
            KompasObject kompas,
            OutlineRings outline,
            double thicknessMm,
            List<(double X, double Y, double Dia)> holes,
            string boardOutPath)
        {
            int FindOuterRingIndexLocal(List<List<Pt>> rings)
            {
                if (rings == null || rings.Count == 0) return -1;
                int idx = -1;
                double bestAbsArea = -1.0;
                for (int r = 0; r < rings.Count; r++)
                {
                    var ring = rings[r];
                    if (ring == null || ring.Count < 3) continue;
                    double area2 = 0.0;
                    for (int i = 0; i < ring.Count; i++)
                    {
                        var a = ring[i];
                        var b = ring[(i + 1) % ring.Count];
                        area2 += (a.X * b.Y - b.X * a.Y);
                    }
                    double absA = Math.Abs(area2) * 0.5;
                    if (absA > bestAbsArea) { bestAbsArea = absA; idx = r; }
                }
                return idx;
            }

            void DrawClosedPolylineLocal(ksDocument2D d3, List<Pt> ring)
            {
                if (d3 == null || ring == null || ring.Count < 2) return;
                for (int i = 0; i < ring.Count; i++)
                {
                    var a = ring[i];
                    var b = ring[(i + 1) % ring.Count];
                    if (Math.Abs(a.X - b.X) < 1e-9 && Math.Abs(a.Y - b.Y) < 1e-9) continue;
                    d3.ksLineSeg(a.X, a.Y, b.X, b.Y, 1);
                }
            }

            if (outline == null || outline.Rings == null || outline.Rings.Count == 0)
            {
                MessageBox.Show("Контур платы пуст.", "КОМПАС", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            double depth = Math.Max(0.001, thicknessMm);

            var partDoc = (ksDocument3D)kompas.Document3D();
            if (!partDoc.Create(false, true)) return null;

            var part = (ksPart)partDoc.GetPart((short)Part_Type.pTop_Part);
            var planeXOY = (ksEntity)part.GetDefaultEntity((short)Obj3dType.o3d_planeXOY);

            // --- Эскиз базового контура: внешний контур ---
            int outerIdx = FindOuterRingIndexLocal(outline.Rings);
            if (outerIdx < 0)
            {
                MessageBox.Show("Не найден внешний контур.", "КОМПАС", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            var sketchEnt = (ksEntity)part.NewEntity((short)Obj3dType.o3d_sketch);
            var sketchDef = (ksSketchDefinition)sketchEnt.GetDefinition();
            sketchDef.SetPlane(planeXOY);
            if (!sketchEnt.Create()) return null;

            var d2 = (ksDocument2D)sketchDef.BeginEdit();
            if (d2 == null) return null;
            DrawClosedPolylineLocal(d2, outline.Rings[outerIdx]);
            sketchDef.EndEdit();

            try { partDoc.RebuildDocument(); } catch { }

            // --- Базовая экструзия в −Z ---
            bool baseOk = false;
            {
                var extr = (ksEntity)part.NewEntity((short)Obj3dType.o3d_baseExtrusion);
                var extrDef = (ksBaseExtrusionDefinition)extr.GetDefinition();
                extrDef.SetSketch(sketchEnt);
                extrDef.directionType = (short)Direction_Type.dtReverse;
                extrDef.SetSideParam(false, (short)End_Type.etBlind, depth, 0.0, false);
                extrDef.SetSideParam(true, (short)End_Type.etBlind, 0.0, 0.0, false);
                extrDef.SetThinParam(false, 0, 0, 0);
                baseOk = extr.Create();
            }
            if (!baseOk)
            {
                var extr = (ksEntity)part.NewEntity((short)Obj3dType.o3d_baseExtrusion);
                var extrDef = (ksBaseExtrusionDefinition)extr.GetDefinition();
                extrDef.SetSketch(sketchEnt);
                extrDef.directionType = (short)Direction_Type.dtNormal;
                extrDef.SetSideParam(false, (short)End_Type.etBlind, 0.0, 0.0, false);
                extrDef.SetSideParam(true, (short)End_Type.etBlind, depth, 0.0, false);
                extrDef.SetThinParam(false, 0, 0, 0);
                baseOk = extr.Create();
            }
            if (!baseOk)
            {
                MessageBox.Show("Не удалось выполнить базовую экструзию платы. Проверьте замкнутость профиля.",
                    "КОМПАС", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }

            // --- Внутренние вырезы (контуры) в −Z ---
            for (int r = 0; r < outline.Rings.Count; r++)
            {
                if (r == outerIdx) continue;
                var ring = outline.Rings[r];
                if (ring == null || ring.Count < 3) continue;

                var cutSketch = (ksEntity)part.NewEntity((short)Obj3dType.o3d_sketch);
                var cutDef = (ksSketchDefinition)cutSketch.GetDefinition();
                cutDef.SetPlane(planeXOY);
                if (!cutSketch.Create()) continue;

                var d2cut = (ksDocument2D)cutDef.BeginEdit();
                if (d2cut == null) continue;
                DrawClosedPolylineLocal(d2cut, ring);
                cutDef.EndEdit();

                var cut = (ksEntity)part.NewEntity((short)Obj3dType.o3d_cutExtrusion);
                var cdef = (ksCutExtrusionDefinition)cut.GetDefinition();
                cdef.directionType = (short)Direction_Type.dtNormal;
                cdef.SetSideParam(false, (short)End_Type.etBlind, 0.0, 0.0, false);
                cdef.SetSideParam(true, (short)End_Type.etBlind, depth + 0.1, 0.0, false);
                cdef.SetSketch(cutSketch);
                cut.Create();
            }

            // --- Круглые отверстия (эскиз окружностей) в −Z ---
            if (holes != null && holes.Count > 0)
            {
                var hSketch = (ksEntity)part.NewEntity((short)Obj3dType.o3d_sketch);
                var hDef = (ksSketchDefinition)hSketch.GetDefinition();
                hDef.SetPlane(planeXOY);
                if (hSketch.Create())
                {
                    var h2 = (ksDocument2D)hDef.BeginEdit();
                    if (h2 != null)
                    {
                        int n = 0;
                        for (int i = 0; i < holes.Count; i++)
                        {
                            var h = holes[i];
                            if (h.Dia <= 0) continue;
                            h2.ksCircle(h.X, h.Y, h.Dia * 0.5, 1);
                            n++;
                        }
                        hDef.EndEdit();

                        if (n > 0)
                        {
                            var cut = (ksEntity)part.NewEntity((short)Obj3dType.o3d_cutExtrusion);
                            var cd = (ksCutExtrusionDefinition)cut.GetDefinition();
                            cd.directionType = (short)Direction_Type.dtNormal;
                            double cutDepth = depth + 0.2;
                            cd.SetSideParam(false, (short)End_Type.etBlind, 0.0, 0.0, false);
                            cd.SetSideParam(true, (short)End_Type.etBlind, cutDepth, 0.0, false);
                            cd.SetSketch(hSketch);
                            cut.Create();
                        }
                    }
                }
            }

            // --- Сохранение детали ---
            try
            {
                var dir = Path.GetDirectoryName(boardOutPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                ((ksDocument3D)partDoc).SaveAs(boardOutPath);

                try
                {
                    var mi = partDoc.GetType().GetMethod("Close", Type.EmptyTypes);
                    if (mi != null) mi.Invoke(partDoc, null);
                }
                catch { }
                try { Marshal.FinalReleaseComObject(partDoc); } catch { }

                return boardOutPath;
            }
            catch
            {
                return null;
            }
        }

        // =============================== ВСПОМОГАТЕЛЬНЫЕ ===============================
        private static void ComputeOutlineBBox(OutlineRings outline, out double minX, out double minY, out double maxX, out double maxY)
        {
            minX = 0; minY = 0; maxX = 0; maxY = 0;
            bool init = false;
            for (int r = 0; r < outline.Rings.Count; r++)
            {
                var ring = outline.Rings[r];
                for (int i = 0; i < ring.Count; i++)
                {
                    var p = ring[i];
                    if (!init)
                    {
                        minX = maxX = p.X; minY = maxY = p.Y; init = true;
                    }
                    else
                    {
                        if (p.X < minX) minX = p.X;
                        if (p.X > maxX) maxX = p.X;
                        if (p.Y < minY) minY = p.Y;
                        if (p.Y > maxY) maxY = p.Y;
                    }
                }
            }
        }

        private static void LogAppend(StringBuilder log, string line)
        {
            try { if (log != null) log.AppendLine(line); } catch { }
        }

        // ---------- Размещение API5 через ksPlacement (типизированно) ----------
        private static bool TryPlaceComponentApi5_Typed(ksPart comp, double x, double y, double z, double rotCWdeg, StringBuilder log)
        {
            try
            {
                if (comp == null) { LogAppend(log, "Place5: comp is null"); return false; }

                // 1) получить ksPlacement типобезопасно
                ksPlacement placement = null;
                try
                {
                    placement = (ksPlacement)comp.GetPlacement(); // обычный путь
                }
                catch
                {
                    // если RCW «сырое», пробуем через IUnknown → ksPlacement
                    object raw = comp.GetPlacement();
                    if (raw != null)
                    {
                        IntPtr unk = IntPtr.Zero;
                        try
                        {
                            unk = Marshal.GetIUnknownForObject(raw);
                            placement = (ksPlacement)Marshal.GetTypedObjectForIUnknown(unk, typeof(ksPlacement));
                        }
                        finally
                        {
                            if (unk != IntPtr.Zero) Marshal.Release(unk);
                        }
                    }
                }
                if (placement == null)
                {
                    LogAppend(log, "Place5: ksPlacement cast FAILED");
                    return false;
                }

                // 2) Определяем тип трансформации: если z < 0 (bottom слой), нужно отразить
                bool isBottom = z < 0;

                // 3) Собираем матрицу с учетом слоя
                double a = -rotCWdeg * Math.PI / 180.0;
                double c = Math.Cos(a), s = Math.Sin(a);
                double[] m;

                if (isBottom)
                {
                    // Для bottom: отражение по X + поворот + разворот по Z
                    m = new double[]
                    {
                        c,  s,  0,  x,
                        s, -c,  0,  y,
                        0,  0, -1,  z,
                        0,  0,  0,  1
                    };
                    LogAppend(log, $"Place5: Bottom component - mirrored and rotated {rotCWdeg}°");
                }
                else
                {
                    // Для top: обычная матрица
                    m = new double[]
                    {
                        c, -s,  0,  x,
                        s,  c,  0,  y,
                        0,  0,  1,  z,
                        0,  0,  0,  1
                    };
                    LogAppend(log, $"Place5: Top component - rotated {rotCWdeg}°");
                }

                // 4) пробуем матрицей
                if (TryPlacementSetMatrix(placement, m))
                {
                    comp.SetPlacement(placement);
                    try { comp.UpdatePlacement(); } catch { }
                    LogAppend(log, $"Place5: Success with matrix at ({x}, {y}, {z})");
                    return true;
                }

                // 5) fallback: origin+axes
                bool okOrigin = TryPlacementSetOrigin(placement, x, y, z);
                bool okAxes;
                if (isBottom)
                {
                    okAxes = TryPlacementSetAxes(placement, c, s, 0.0, s, -c, 0.0, 0.0, 0.0, -1.0);
                }
                else
                {
                    okAxes = TryPlacementSetAxes(placement, c, -s, 0.0, s, c, 0.0, 0.0, 0.0, 1.0);
                }

                if (okOrigin || okAxes)
                {
                    comp.SetPlacement(placement);
                    try { comp.UpdatePlacement(); } catch { }
                    LogAppend(log, $"Place5: Success with origin/axes at ({x}, {y}, {z})");
                    return true;
                }

                LogAppend(log, "Place5: no matrix/origin/axes methods on ksPlacement");
                return false;
            }
            catch (Exception ex)
            {
                LogAppend(log, "Place5 EX: " + ex.Message);
                return false;
            }
        }

        // ===== ksPlacement helpers (типизированные) =====
        private static bool TryPlacementSetMatrix(ksPlacement pl, double[] m)
        {
            try
            {
                try
                {
                    pl.GetType().InvokeMember("SetMatrix3D",
                        System.Reflection.BindingFlags.InvokeMethod | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                        null, pl, new object[] { m });
                    return true;
                }
                catch { }

                try
                {
                    pl.GetType().InvokeMember("SetMatrix",
                        System.Reflection.BindingFlags.InvokeMethod | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                        null, pl, new object[] { m });
                    return true;
                }
                catch { }

                try
                {
                    pl.GetType().InvokeMember("PutMatrix3D",
                        System.Reflection.BindingFlags.InvokeMethod | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                        null, pl, new object[] { m });
                    return true;
                }
                catch { }

                try
                {
                    pl.GetType().InvokeMember("PutMatrix",
                        System.Reflection.BindingFlags.InvokeMethod | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                        null, pl, new object[] { m });
                    return true;
                }
                catch { }

                try
                {
                    pl.GetType().InvokeMember("SetTransform",
                        System.Reflection.BindingFlags.InvokeMethod | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                        null, pl, new object[] { m });
                    return true;
                }
                catch { }

                return false;
            }
            catch { return false; }
        }

        private static bool TryPlacementSetOrigin(ksPlacement pl, double x, double y, double z)
        {
            try
            {
                string[] names = { "SetOrigin", "InitOrigin", "SetPosition", "SetLocation", "SetBasePoint", "SetCoord", "SetCoords" };
                for (int i = 0; i < names.Length; i++)
                {
                    try
                    {
                        pl.GetType().InvokeMember(names[i],
                            System.Reflection.BindingFlags.InvokeMethod | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                            null, pl, new object[] { x, y, z });
                        return true;
                    }
                    catch { /* следующий */ }
                }
                return false;
            }
            catch { return false; }
        }

        private static bool TryPlacementSetAxes(ksPlacement pl,
                                               double xdx, double xdy, double xdz,
                                               double ydx, double ydy, double ydz,
                                               double zdx, double zdy, double zdz)
        {
            string[] xNames = { "SetAxisX", "SetXAxis", "SetAxisOX", "SetXDirection" };
            string[] yNames = { "SetAxisY", "SetYAxis", "SetAxisOY", "SetYDirection" };
            string[] zNames = { "SetAxisZ", "SetZAxis", "SetAxisOZ", "SetZDirection" };
            bool ok = false;
            for (int i = 0; i < xNames.Length; i++)
                ok |= Invoke3(pl, xNames[i], xdx, xdy, xdz);
            for (int i = 0; i < yNames.Length; i++)
                ok |= Invoke3(pl, yNames[i], ydx, ydy, ydz);
            for (int i = 0; i < zNames.Length; i++)
                ok |= Invoke3(pl, zNames[i], zdx, zdy, zdz);
            return ok;
        }

        private static bool Invoke3(object obj, string method, double a, double b, double c)
        {
            try
            {
                obj.GetType().InvokeMember(method,
                    System.Reflection.BindingFlags.InvokeMethod | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                    null, obj, new object[] { a, b, c });
                return true;
            }
            catch { return false; }
        }

        // === Выбор имени модели строго из ProjectPlacement + AppSettings ===
        private static string ResolveModelNameBySettings(ProjectPlacement p, AppSettings settings)
        {
            if (p == null) return null;
            string NN(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

            var src = (settings?.ModelNameSource ?? "").Trim().ToUpperInvariant();

            // В ProjectPlacement надёжно доступно: Body (из BOM)
            // Полей PartNameFromIdf/FootprintFromIdf здесь НЕТ, не используем.
            switch (src)
            {
                case "BODY":
                case "FOOTPRINT":
                case "NAME":
                case "COMMENT":
                default:
                    return NN(p.Body);
            }
        }
    }
}
