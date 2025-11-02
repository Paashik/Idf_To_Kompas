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
using Idf2Kompas.Models;      // ProjectModel, AppSettings
using Idf2Kompas.Parsers;     // IdfGeometry

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
            LogAppend(placeLog, "=== FAST PLACEMENT (CopyPart + bottom flip + single rebuild) ===");

            // Инфо по плате
            double minX, minY, maxX, maxY;
            ComputeOutlineBBox(outline, out minX, out minY, out maxX, out maxY);
            LogAppend(placeLog, $"Board BBox mm: X=[{minX:0.###}..{maxX:0.###}] Y=[{minY:0.###}..{maxY:0.###}] T={usedThickness:0.###}");

            // Вставим плату как компонент (базовая деталь)
            var boardComp = (ksPart)doc3D.GetPart((short)Part_Type.pNew_Part);
            bool okInsBoard = false;
            try { okInsBoard = doc3D.SetPartFromFileEx(boardM3D, boardComp, true, true); } catch { }
            if (!okInsBoard) { try { okInsBoard = doc3D.SetPartFromFile(boardM3D, boardComp); } catch { } }
            if (!okInsBoard)
            {
                MessageBox.Show("Не удалось вставить плату как компонент.", "КОМПАС", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // ===== УСКОРЕНИЕ: кеш прототипов и размещение через CopyPart =====
            var srcCache = new Dictionary<string, ksPart>(StringComparer.OrdinalIgnoreCase);
            char bottomFlipAxis = 'X'; // при необходимости смените на 'Y'

            int placed = 0, skipped = 0;
            if (model.Placements != null)
            {
                for (int i = 0; i < model.Placements.Count; i++)
                {
                    var p = model.Placements[i];
                    var libPath = LibraryHelper.FindModelPath(settings.LibDir, p.Body);
                    double targetZ = p.IsBottomSide ? -usedThickness : 0.0;

                    LogAppend(placeLog,
                        $"RAW→ASM: {p.Designator} body={p.Body} X={p.Xmm} Y={p.Ymm} Z={targetZ} Side={(p.IsBottomSide ? "BOTTOM" : "TOP")} RotCW={p.RotationDeg}");

                    if (string.IsNullOrWhiteSpace(libPath) || !File.Exists(libPath))
                    {
                        LogAppend(placeLog, "SKIP(no model): " + p.Designator + " → " + (p.Body ?? ""));
                        skipped++;
                        continue;
                    }

                    try
                    {
                        ksPart srcPart;
                        if (!srcCache.TryGetValue(libPath, out srcPart))
                        {
                            // Первое появление этой модели → вставка из файла с корректным плейсментом (с учётом bottom)
                            srcPart = InsertOnceFromFile_WithPlacement_BottomAware(
                                doc3D, libPath,
                                p.Xmm, p.Ymm, targetZ,
                                p.RotationDeg, p.IsBottomSide, bottomFlipAxis);

                            srcCache[libPath] = srcPart;
                            placed++;
                            LogAppend(placeLog, $"OK[FIRST]: {p.Designator} at ({p.Xmm:0.###},{p.Ymm:0.###},{targetZ:0.###})");
                        }
                        else
                        {
                            // Повторные экземпляры → быстрые копии
                            var pl = CreatePlacement_BottomAware(
                                doc3D,
                                p.Xmm, p.Ymm, targetZ,
                                p.RotationDeg, p.IsBottomSide, bottomFlipAxis);

                            var comp = TryCopyPart(doc3D, srcPart, pl);
                            if (comp == null)
                            {
                                // Фолбэк, если CopyPart недоступен
                                comp = InsertOnceFromFile_WithPlacement_BottomAware(
                                    doc3D, libPath, p.Xmm, p.Ymm, targetZ,
                                    p.RotationDeg, p.IsBottomSide, bottomFlipAxis);
                            }

                            placed++;
                            LogAppend(placeLog, $"OK[COPY]:  {p.Designator} at ({p.Xmm:0.###},{p.Ymm:0.###},{targetZ:0.###})");
                        }
                    }
                    catch (Exception ex)
                    {
                        skipped++;
                        LogAppend(placeLog, "FAIL(place): " + p.Designator + " → " + ex.Message);
                    }
                }
            }

            // ===== ОДИН раз перестраиваем документ =====
            try { doc3D.RebuildDocument(); } catch { }

            // Сохранение сборки
            try
            {
                if (!string.IsNullOrWhiteSpace(asmOutPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(asmOutPath) ?? "");
                    doc3D.SaveAs(asmOutPath);
                }
            }
            catch { }

            // Лог рядом со сборкой
            try
            {
                var logPath = Path.Combine(Path.GetDirectoryName(asmOutPath) ?? "", ".placement.log.txt");
                File.WriteAllText(logPath, placeLog.ToString(), Encoding.UTF8);
            }
            catch { }

            MessageBox.Show(
                "Сборка создана (ускоренный режим с переворотом нижнего слоя).\n" +
                $"Плата: T = {usedThickness:0.###} мм, контуров: {outline.Rings.Count}\n" +
                $"Отверстий (после фильтра): {filtHoles.Count}\n" +
                $"Корпусов: вставлено {placed}, пропущено {skipped}\n" +
                $"Файлы:\n Плата: {boardM3D}\n Сборка: {asmOutPath}",
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

        private static void TryUpdatePlacement(ksPart part)
        {
            try { part.UpdatePlacement(); } catch { }
        }

        // ======================= УСКОРЕННЫЕ ВСТАВКИ (с bottom flip) =======================

        /// <summary>
        /// Построить матрицу R (3x3) для TOP/BOTTOM: Z-поворот + при необходимости разворот 180° по оси.
        /// Возвращает элементы R (row-major).
        /// </summary>
        private static void BuildRotationBottomAware(double rotCWdeg, bool isBottom, char bottomFlipAxis,
                                                     out double r00, out double r01, out double r02,
                                                     out double r10, out double r11, out double r12,
                                                     out double r20, out double r21, out double r22)
        {
            // TOP: rzCCW = -CW; BOTTOM: rzCCW = +CW
            double rzCCW = isBottom ? +rotCWdeg : -rotCWdeg;
            double a = rzCCW * Math.PI / 180.0;
            double cz = Math.Cos(a), sz = Math.Sin(a);

            // Базовый Rz
            r00 = cz; r01 = -sz; r02 = 0;
            r10 = sz; r11 = cz; r12 = 0;
            r20 = 0; r21 = 0; r22 = 1;

            if (isBottom)
            {
                // Чистый Rx(pi) или Ry(pi) справа: инверсия двух столбцов
                if (bottomFlipAxis == 'Y' || bottomFlipAxis == 'y')
                {
                    // R = R * Ry(pi): инвертируем столбцы X и Z
                    r00 = -r00; r02 = -r02;
                    r10 = -r10; r12 = -r12;
                    r20 = -r20; r22 = -r22;
                }
                else
                {
                    // R = R * Rx(pi): инвертируем столбцы Y и Z
                    r01 = -r01; r02 = -r02;
                    r11 = -r11; r12 = -r12;
                    r21 = -r21; r22 = -r22;
                }
            }
        }

        /// <summary>
        /// Первый экземпляр: вставка из файла + сразу задать placement (InitByMatrix3D + fallback).
        /// </summary>
        private static ksPart InsertOnceFromFile_WithPlacement_BottomAware(
            ksDocument3D doc3D,
            string filePath,
            double x, double y, double z,
            double rotCWdeg, bool isBottom, char bottomFlipAxis)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                throw new FileNotFoundException("Model not found", filePath);

            var part = (ksPart)doc3D.GetPart((short)Part_Type.pNew_Part);
            bool ok = false;
            try { ok = doc3D.SetPartFromFileEx(filePath, part, true, false); } catch { }
            if (!ok) { try { ok = doc3D.SetPartFromFile(filePath, part); } catch { } }
            if (!ok) throw new InvalidOperationException("SetPartFromFile* failed: " + filePath);

            // Поворот
            double r00, r01, r02, r10, r11, r12, r20, r21, r22;
            BuildRotationBottomAware(rotCWdeg, isBottom, bottomFlipAxis,
                                     out r00, out r01, out r02,
                                     out r10, out r11, out r12,
                                     out r20, out r21, out r22);

            var pl = (ksPlacement)part.GetPlacement();

            // row-major 4x4
            double[] mRow = {
                r00, r01, r02, x,
                r10, r11, r12, y,
                r20, r21, r22, z,
                0,   0,   0,   1
            };
            // col-major (на всякий случай)
            double[] mCol = {
                r00, r10, r20, 0,
                r01, r11, r21, 0,
                r02, r12, r22, 0,
                x,   y,   z,   1
            };

            if (!TryInitPlacementByMatrix(pl, mRow))
                TryInitPlacementByMatrix(pl, mCol);
            TryPlacementSetOrigin(pl, x, y, z);
            TryPlacementSetAxes(pl, r00, r10, r20, r01, r11, r21, r02, r12, r22);

            part.SetPlacement(pl);
            TryUpdatePlacement(part);   // применяем сразу
            return part;
        }

        /// <summary>
        /// Создать ksPlacement с полной матрицей (с учётом нижнего слоя).
        /// </summary>
        private static ksPlacement CreatePlacement_BottomAware(
            ksDocument3D doc3D,
            double x, double y, double z,
            double rotCWdeg, bool isBottom, char bottomFlipAxis)
        {
            ksPlacement pl = TryCreateDefaultPlacement(doc3D);
            if (pl == null)
            {
                // Фолбэк: создаём временный пустой компонент и берём его placement
                var tmp = (ksPart)doc3D.GetPart((short)Part_Type.pNew_Part);
                pl = (ksPlacement)tmp.GetPlacement();
            }

            double r00, r01, r02, r10, r11, r12, r20, r21, r22;
            BuildRotationBottomAware(rotCWdeg, isBottom, bottomFlipAxis,
                                     out r00, out r01, out r02,
                                     out r10, out r11, out r12,
                                     out r20, out r21, out r22);

            double[] mRow = {
                r00, r01, r02, x,
                r10, r11, r12, y,
                r20, r21, r22, z,
                0,   0,   0,   1
            };
            double[] mCol = {
                r00, r10, r20, 0,
                r01, r11, r21, 0,
                r02, r12, r22, 0,
                x,   y,   z,   1
            };

            if (!TryInitPlacementByMatrix(pl, mRow))
                TryInitPlacementByMatrix(pl, mCol);
            TryPlacementSetOrigin(pl, x, y, z);
            TryPlacementSetAxes(pl, r00, r10, r20, r01, r11, r21, r02, r12, r22);

            return pl;
        }

        /// <summary>
        /// Быстрый дубликат: CopyPart(source, placement). Если метода нет — null.
        /// </summary>
        private static ksPart TryCopyPart(ksDocument3D doc3D, ksPart source, ksPlacement placement)
        {
            try
            {
                var t = doc3D.GetType();
                var res = t.InvokeMember("CopyPart",
                    System.Reflection.BindingFlags.InvokeMethod | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                    null, doc3D, new object[] { source, placement }) as ksPart;
                if (res != null)
                {
                    TryUpdatePlacement(res);
                    return res;
                }
            }
            catch { }
            return null;
        }

        private static ksPlacement TryCreateDefaultPlacement(ksDocument3D doc3D)
        {
            try
            {
                var t = doc3D.GetType();
                var pl = t.InvokeMember("DefaultPlacement",
                    System.Reflection.BindingFlags.InvokeMethod | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                    null, doc3D, new object[] { }) as ksPlacement;
                if (pl != null) return pl;
            }
            catch { }
            try
            {
                var t = doc3D.GetType();
                var pl = t.InvokeMember("CreatePlacement",
                    System.Reflection.BindingFlags.InvokeMethod | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                    null, doc3D, new object[] { }) as ksPlacement;
                if (pl != null) return pl;
            }
            catch { }
            return null;
        }

        // ======================= Общие helpers для placement =======================

        private static bool TryInitPlacementByMatrix(ksPlacement pl, double[] m)
        {
            try
            {
                var t = pl.GetType();
                try
                {
                    t.InvokeMember("InitByMatrix3D",
                        System.Reflection.BindingFlags.InvokeMethod | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                        null, pl, new object[] { (object)m });
                    return true;
                }
                catch { }
                try
                {
                    t.InvokeMember("SetMatrix3D",
                        System.Reflection.BindingFlags.InvokeMethod | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                        null, pl, new object[] { (object)m });
                    return true;
                }
                catch { }
                try
                {
                    t.InvokeMember("SetTransform",
                        System.Reflection.BindingFlags.InvokeMethod | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                        null, pl, new object[] { (object)m });
                    return true;
                }
                catch { }
                try
                {
                    t.InvokeMember("PutMatrix3D",
                        System.Reflection.BindingFlags.InvokeMethod | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                        null, pl, new object[] { (object)m });
                    return true;
                }
                catch { }
                try
                {
                    t.InvokeMember("PutMatrix",
                        System.Reflection.BindingFlags.InvokeMethod | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                        null, pl, new object[] { (object)m });
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
                    catch { }
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
    }
}
