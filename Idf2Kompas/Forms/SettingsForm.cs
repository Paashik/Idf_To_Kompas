using System;
using System.Windows.Forms;
using Idf2Kompas.Models;
using Idf2Kompas.Services;

namespace Idf2Kompas.Forms
{
    public partial class SettingsForm : Form
    {
        private AppSettings _settings;

        public SettingsForm(AppSettings s)
        {
            InitializeComponent();
            _settings = s ?? new AppSettings();

            // Инициализация полей из настроек
            txtLibDir.Text = _settings.LibDir ?? "";
            txtSaveBoardDir.Text = _settings.SaveBoardDir ?? "";
            txtSaveAsmDir.Text = _settings.SaveAsmDir ?? "";
            txtHoleMin.Text = (_settings.SignalHoleMinDiaMm).ToString(System.Globalization.CultureInfo.InvariantCulture);
            txtModelSource.Text = _settings.ModelNameSource ?? "Body";

            txtRefDes.Text = _settings.BomRefDesName ?? "Designator";
            txtPN.Text = _settings.BomPNName ?? "Stock Code";
            txtComment.Text = _settings.BomCommentName ?? "Comment";
            txtBody.Text = _settings.BomBodyName ?? "Body";
            txtFootprint.Text = _settings.BomFootprintName ?? "Footprint";
            txtDesc.Text = _settings.BomDescriptionName ?? "Description";

            // Пикеры папок
            btnPickLib.Click += (s1, e1) => PickFolder(txtLibDir);
            btnPickBoardDir.Click += (s1, e1) => PickFolder(txtSaveBoardDir);
            btnPickAsmDir.Click += (s1, e1) => PickFolder(txtSaveAsmDir);

            // Сохранение по OK
            this.FormClosing += SettingsForm_FormClosing;
        }

        private void PickFolder(TextBox tb)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    tb.Text = dlg.SelectedPath;
            }
        }

        private void SettingsForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (this.DialogResult != DialogResult.OK)
                return;

            // Валидация и запись
            double minDia = 0;
            double.TryParse(txtHoleMin.Text.Replace(',', '.'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out minDia);

            _settings.LibDir = txtLibDir.Text?.Trim();
            _settings.SaveBoardDir = txtSaveBoardDir.Text?.Trim();
            _settings.SaveAsmDir = txtSaveAsmDir.Text?.Trim();
            _settings.SignalHoleMinDiaMm = Math.Max(0, minDia);
            _settings.ModelNameSource = string.IsNullOrWhiteSpace(txtModelSource.Text) ? "Body" : txtModelSource.Text.Trim();

            _settings.BomRefDesName = string.IsNullOrWhiteSpace(txtRefDes.Text) ? "Designator" : txtRefDes.Text.Trim();
            _settings.BomPNName = string.IsNullOrWhiteSpace(txtPN.Text) ? "Stock Code" : txtPN.Text.Trim();
            _settings.BomCommentName = string.IsNullOrWhiteSpace(txtComment.Text) ? "Comment" : txtComment.Text.Trim();
            _settings.BomBodyName = string.IsNullOrWhiteSpace(txtBody.Text) ? "Body" : txtBody.Text.Trim();
            _settings.BomFootprintName = string.IsNullOrWhiteSpace(txtFootprint.Text) ? "Footprint" : txtFootprint.Text.Trim();
            _settings.BomDescriptionName = string.IsNullOrWhiteSpace(txtDesc.Text) ? "Description" : txtDesc.Text.Trim();

            SettingsService.Save(_settings);
        }
    }
}
