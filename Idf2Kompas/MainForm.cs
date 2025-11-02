using System;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Collections.Generic;
using Idf2Kompas.Models;
using Idf2Kompas.Parsers;
using Idf2Kompas.Services;
using static Idf2Kompas.Parsers.IdfGeometry;

namespace Idf2Kompas
{
    public partial class MainForm : Form
    {
        private IdfBoard _board;
        private DataTable _preview;
        private AppSettings _settings = SettingsService.Load();

        private string _brdPath, _proPath, _csvPath;

        public MainForm()
        {
            InitializeComponent();

            btnBrowseBrd.Click += (s, e) => BrowseFile("IDF Board (*.brd)|*.brd|All files|*.*", v => _brdPath = v);
            btnBrowsePro.Click += (s, e) => BrowseFile("IDF Library (*.pro)|*.pro|All files|*.*", v => _proPath = v);
            btnBrowseCsv.Click += (s, e) => BrowseFile("CSV (*.csv)|*.csv|All files|*.*", v => _csvPath = v);
            btnParse.Click += (s, e) => ParseAndPreview();
            btnWriteIdf.Click += (s, e) => WriteIdf();
            btnSettings.Click += (s, e) => OpenSettings();
            btnBuildKompas.Click += (s, e) => BuildKompas();

            gridPreview.DataBindingComplete += GridPreview_DataBindingComplete;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            try
            {
                if (!string.IsNullOrWhiteSpace(_settings.BrdPath)) _brdPath = _settings.BrdPath;
                if (!string.IsNullOrWhiteSpace(_settings.ProPath)) _proPath = _settings.ProPath;
                if (!string.IsNullOrWhiteSpace(_settings.CsvPath)) _csvPath = _settings.CsvPath;
                RefreshLabels();
            }
            catch { }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            try
            {
                _settings.BrdPath = _brdPath;
                _settings.ProPath = _proPath;
                _settings.CsvPath = _csvPath;
                SettingsService.Save(_settings);
            }
            catch { }
        }

        private void BrowseFile(string filter, Action<string> setPath)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = filter;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    setPath(dlg.FileName);
                    RefreshLabels();
                }
            }
        }

        private void RefreshLabels()
        {
            var asmInfo = FileNameParser.Parse(_csvPath);
            var brdInfo = FileNameParser.Parse(_brdPath);
            lblAsm.Text = $@"Assembly: Обозначение = ""{asmInfo.DesignationItem ?? asmInfo.Name}"", Наименование = ""{asmInfo.Name ?? asmInfo.DesignationItem}""";
            lblBoard.Text = $@"Board:    Обозначение = ""{brdInfo.DesignationItem ?? brdInfo.Name}"", Наименование = ""{brdInfo.Name ?? brdInfo.DesignationItem}""";
        }

        private BomColumnMap MapFromSettings() => new BomColumnMap
        {
            RefDes = _settings.BomRefDesName,
            PN = _settings.BomPNName,
            Comment = _settings.BomCommentName,
            Body = _settings.BomBodyName,
            Footprint = _settings.BomFootprintName,
            Description = _settings.BomDescriptionName
        };

        private string ResolveModelName(IdfPlacement p)
        {
            var src = (_settings.ModelNameSource ?? "Body").ToLowerInvariant();
            switch (src)
            {
                case "footprint": return p.FootprintFromBom ?? p.FootprintFromIdf;
                case "comment": return p.Comment;
                case "pn": return p.PN;
                default: return p.Body;
            }
        }

        private void OpenSettings()
        {
            using (var dlg = new Idf2Kompas.Forms.SettingsForm(_settings))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    // перечитываем сохранённое
                    _settings = Idf2Kompas.Services.SettingsService.Load();
                    // обновим подсветку/подписи
                    RefreshLabels();
                    if (gridPreview.DataSource != null)
                        GridPreview_DataBindingComplete(gridPreview, null);
                }
            }
        }

        private void ParseAndPreview()
        {
            try
            {
                if (!File.Exists(_brdPath) || !File.Exists(_proPath))
                {
                    MessageBox.Show(this, "Укажите файлы BRD и PRO (IDF v3).", "Нет входных файлов", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var idf = new IdfParser();
                _board = idf.Parse(_brdPath, _proPath, new IdfParseOptions());

                if (File.Exists(_csvPath))
                {
                    var bom = new Idf2Kompas.Parsers.BomReader().LoadCsv(_csvPath, MapFromSettings(), Encoding.GetEncoding(1251));
                    Idf2Kompas.Parsers.BomReader.EnrichWithBom(_board, bom);
                }

                BuildPreview();
                RefreshLabels();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "Ошибка разбора", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BuildPreview()
        {
            _preview = new DataTable();
            foreach (var c in new[] { "RefDes", "Comment", "Body", "Model", "Footprint(BOM)", "Footprint(IDF)", "StockCode", "Side", "X", "Y", "Rot" })
                _preview.Columns.Add(c);

            if (_board?.Placements != null)
            {
                foreach (var p in _board.Placements)
                {
                    _preview.Rows.Add(
                        p.RefDes,
                        p.Comment,
                        p.Body,
                        ResolveModelName(p),
                        p.FootprintFromBom,
                        p.FootprintFromIdf,
                        p.PN,
                        p.Side,
                        p.X.ToString("0.###"),
                        p.Y.ToString("0.###"),
                        p.RotDeg.ToString("0.###")
                    );
                }
            }

            gridPreview.DataSource = _preview;
        }

        private void GridPreview_DataBindingComplete(object sender, DataGridViewBindingCompleteEventArgs e)
        {
            if (_board?.Placements == null || _board.Placements.Count == 0) return;
            if (!gridPreview.Columns.Contains("Model")) return;

            foreach (DataGridViewRow row in gridPreview.Rows)
            {
                if (row.Index < 0 || row.Index >= _board.Placements.Count) continue;
                var modelName = ResolveModelName(_board.Placements[row.Index]);
                bool exists = Idf2Kompas.Services.LibraryHelper.BodyExists(_settings.LibDir, modelName);

                var cell = row.Cells["Model"];
                cell.Style.BackColor = exists
                    ? System.Drawing.Color.FromArgb(0xE6, 0xFF, 0xE6)
                    : System.Drawing.Color.FromArgb(0xFF, 0xE6, 0xE6);
            }
        }

        private void BuildKompas()
        {
            try
            {
                if (!File.Exists(_brdPath))
                {
                    MessageBox.Show(this, "Укажите .BRD", "Нет файла", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // 1) читаем BRD, определяем масштаб
                var brdText = File.ReadAllText(_brdPath, Encoding.GetEncoding(1251));
                double scale = Idf2Kompas.Parsers.IdfGeometry.DetectUnitsScale(brdText);

                // 2) контур + толщина
                string outlineBlock = Idf2Kompas.Parsers.IdfGeometry.ExtractBlock(brdText, ".BOARD_OUTLINE", ".END_BOARD_OUTLINE");
                var outline = Idf2Kompas.Parsers.IdfGeometry.ParseBoardOutline(outlineBlock, scale);

                // если толщина не задана — спросим у пользователя
                if (outline.ThicknessMm <= 0 || double.IsNaN(outline.ThicknessMm) || double.IsInfinity(outline.ThicknessMm))
                {
                    double t = PromptThicknessMm(1.6);
                    if (t <= 0)
                    {
                        MessageBox.Show(this, "Толщина платы не задана. Построение отменено.",
                            "Толщина платы", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    outline.ThicknessMm = t;
                }

                // 3) отверстия (DRILLED_HOLES | HOLES)
                string holesBlock = Idf2Kompas.Parsers.IdfGeometry.ExtractBlock(brdText, ".DRILLED_HOLES", ".END_DRILLED_HOLES");
                if (string.IsNullOrWhiteSpace(holesBlock))
                    holesBlock = Idf2Kompas.Parsers.IdfGeometry.ExtractBlock(brdText, ".HOLES", ".END_HOLES");
                var holes = Idf2Kompas.Parsers.IdfGeometry.ParseHoles(holesBlock, scale);

                // 4) имя/обозначение
                // парсим имена для платы и сборки
                var brdInfo = FileNameParser.Parse(_brdPath);
                var asmInfo = FileNameParser.Parse(_csvPath);

                var model = new ProjectModel
                {
                    BoardDesignation = brdInfo.DesignationItem ?? brdInfo.Name ?? "BOARD",
                    BoardName = brdInfo.Name ?? brdInfo.DesignationItem ?? "Плата",

                    AssemblyDesignation = asmInfo.DesignationItem ?? asmInfo.Name ?? "ASM",
                    AssemblyName = asmInfo.Name ?? asmInfo.DesignationItem ?? "Сборка",

                    BoardThickness = outline.ThicknessMm
                };

                // 5) перенос размещений (если парсер IDF заполнит _board)
                if (_board?.Placements != null)
                {
                    foreach (var p in _board.Placements)
                    {
                        model.Placements.Add(new Idf2Kompas.Models.ProjectPlacement
                        {
                            Designator = p.RefDes,
                            Body = ResolveModelName(p),
                            Xmm = p.X * scale,
                            Ymm = p.Y * scale,
                            RotationDeg = p.RotDeg,
                            IsBottomSide = string.Equals(p.Side, "BOTTOM", StringComparison.OrdinalIgnoreCase)
                        });
                        model.BomRows.Add(new Idf2Kompas.Models.ProjectBomRow
                        {
                            Designator = p.RefDes,
                            Body = ResolveModelName(p),
                            Comment = p.Comment,
                            StockCode = p.PN,
                            Description = p.PartNameFromIdf
                        });
                    }
                }

                // 6) Пути сохранения с правильными именами
                string brdDir = Path.GetDirectoryName(_brdPath);
                string csvDir = Path.GetDirectoryName(_csvPath);

                string saveBoardDir = !string.IsNullOrWhiteSpace(_settings.SaveBoardDir) ? _settings.SaveBoardDir : brdDir;
                string saveAsmDir = !string.IsNullOrWhiteSpace(_settings.SaveAsmDir) ? _settings.SaveAsmDir : csvDir;

                string boardBase = Path.GetFileNameWithoutExtension(_brdPath);
                string asmBase = Path.GetFileNameWithoutExtension(_csvPath);

                string boardOutPath = System.IO.Path.Combine(saveBoardDir ?? brdDir, boardBase + ".m3d");
                string asmOutPath = System.IO.Path.Combine(saveAsmDir ?? csvDir, asmBase + ".a3d");

                // 7) Построить в КОМПАС
                Idf2Kompas.Services.KompasService.BuildAssemblyInKompas(model, _settings, outline, holes, boardOutPath, asmOutPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "Ошибка КОМПАС", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

       

        private void MainForm_Load(object sender, EventArgs e)
        {

        }

       
        private void MainForm_Load_1(object sender, EventArgs e)
        {

        }

        private double PromptThicknessMm(double @default)
        {
            using (var f = new Form())
            using (var lbl = new Label())
            using (var tb = new TextBox())
            using (var ok = new Button())
            using (var cancel = new Button())
            {
                f.Text = "Толщина платы";
                f.StartPosition = FormStartPosition.CenterParent;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MinimizeBox = false; f.MaximizeBox = false;
                f.Width = 320; f.Height = 150;

                lbl.Text = "Введите толщину платы, мм:";
                lbl.AutoSize = true; lbl.Left = 12; lbl.Top = 12;

                tb.Left = 16; tb.Top = 40; tb.Width = 120;
                tb.Text = @default.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

                ok.Text = "OK"; ok.Left = 160; ok.Top = 38; ok.DialogResult = DialogResult.OK;
                cancel.Text = "Отмена"; cancel.Left = 220; cancel.Top = 38; cancel.DialogResult = DialogResult.Cancel;

                f.Controls.AddRange(new Control[] { lbl, tb, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;

                if (f.ShowDialog(this) == DialogResult.OK)
                {
                    double t;
                    if (double.TryParse(tb.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out t))
                        return t;
                }
                return -1;
            }
        }

        private void WriteIdf()
        {
            MessageBox.Show(this, "Экспорт IDF пока отключён.", "Инфо", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
