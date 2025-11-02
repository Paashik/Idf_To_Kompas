namespace Idf2Kompas
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.Panel topPanel;
        private System.Windows.Forms.Button btnBrowseBrd;
        private System.Windows.Forms.Button btnBrowsePro;
        private System.Windows.Forms.Button btnBrowseCsv;
        private System.Windows.Forms.Button btnParse;
        private System.Windows.Forms.Button btnWriteIdf;
        private System.Windows.Forms.Button btnSettings;
        private System.Windows.Forms.Button btnBuildKompas;
        private System.Windows.Forms.Label lblAsm;
        private System.Windows.Forms.Label lblBoard;
        private System.Windows.Forms.DataGridView gridPreview;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.topPanel = new System.Windows.Forms.Panel();
            this.btnBrowseBrd = new System.Windows.Forms.Button();
            this.btnBrowsePro = new System.Windows.Forms.Button();
            this.btnBrowseCsv = new System.Windows.Forms.Button();
            this.btnParse = new System.Windows.Forms.Button();
            this.btnWriteIdf = new System.Windows.Forms.Button();
            this.btnSettings = new System.Windows.Forms.Button();
            this.btnBuildKompas = new System.Windows.Forms.Button();
            this.lblAsm = new System.Windows.Forms.Label();
            this.lblBoard = new System.Windows.Forms.Label();
            this.gridPreview = new System.Windows.Forms.DataGridView();
            this.topPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.gridPreview)).BeginInit();
            this.SuspendLayout();
            // 
            // topPanel
            // 
            this.topPanel.Controls.Add(this.btnBrowseBrd);
            this.topPanel.Controls.Add(this.btnBrowsePro);
            this.topPanel.Controls.Add(this.btnBrowseCsv);
            this.topPanel.Controls.Add(this.btnParse);
            this.topPanel.Controls.Add(this.btnWriteIdf);
            this.topPanel.Controls.Add(this.btnSettings);
            this.topPanel.Controls.Add(this.btnBuildKompas);
            this.topPanel.Controls.Add(this.lblAsm);
            this.topPanel.Controls.Add(this.lblBoard);
            this.topPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.topPanel.Height = 96;
            this.topPanel.TabIndex = 0;
            // 
            // Buttons layout
            // 
            this.btnBrowseBrd.Text = "BRD";
            this.btnBrowseBrd.Left = 8; this.btnBrowseBrd.Top = 8; this.btnBrowseBrd.Width = 60;
            this.btnBrowsePro.Text = "PRO";
            this.btnBrowsePro.Left = 72; this.btnBrowsePro.Top = 8; this.btnBrowsePro.Width = 60;
            this.btnBrowseCsv.Text = "CSV";
            this.btnBrowseCsv.Left = 136; this.btnBrowseCsv.Top = 8; this.btnBrowseCsv.Width = 60;
            this.btnParse.Text = "Parse";
            this.btnParse.Left = 200; this.btnParse.Top = 8; this.btnParse.Width = 60;
            this.btnWriteIdf.Text = "Write IDF";
            this.btnWriteIdf.Left = 264; this.btnWriteIdf.Top = 8; this.btnWriteIdf.Width = 80;
            this.btnSettings.Text = "Settings";
            this.btnSettings.Left = 348; this.btnSettings.Top = 8; this.btnSettings.Width = 80;
            this.btnBuildKompas.Text = "Build → KOMPAS";
            this.btnBuildKompas.Left = 432; this.btnBuildKompas.Top = 8; this.btnBuildKompas.Width = 140;

            // Labels
            this.lblAsm.Left = 8; this.lblAsm.Top = 48; this.lblAsm.Width = 1000; this.lblAsm.Text = "Assembly: -";
            this.lblBoard.Left = 8; this.lblBoard.Top = 68; this.lblBoard.Width = 1000; this.lblBoard.Text = "Board: -";

            // 
            // gridPreview
            // 
            this.gridPreview.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gridPreview.ReadOnly = true;
            this.gridPreview.AllowUserToAddRows = false;
            this.gridPreview.AllowUserToDeleteRows = false;
            this.gridPreview.RowHeadersVisible = false;
            this.gridPreview.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;

            // 
            // MainForm
            // 
            this.Text = "IDF → KOMPAS (WinForms)";
            this.Width = 1100;
            this.Height = 700;
            this.Controls.Add(this.gridPreview);
            this.Controls.Add(this.topPanel);
            this.topPanel.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.gridPreview)).EndInit();
            this.ResumeLayout(false);
        }
    }
}
