namespace Idf2Kompas.Forms
{
    partial class SettingsForm
    {
        private System.ComponentModel.IContainer components = null;

        // Корневой лэйаут: 2 строки — настройки (100%) и панель кнопок (48 px)
        private System.Windows.Forms.TableLayoutPanel root;

        // Таблица с настройками
        private System.Windows.Forms.TableLayoutPanel tbl;

        // Поля
        private System.Windows.Forms.TextBox txtLibDir, txtSaveBoardDir, txtSaveAsmDir;
        private System.Windows.Forms.TextBox txtHoleMin, txtModelSource;
        private System.Windows.Forms.TextBox txtRefDes, txtPN, txtComment, txtBody, txtFootprint, txtDesc;

        // Кнопки и панель кнопок
        private System.Windows.Forms.FlowLayoutPanel pnlButtons;
        private System.Windows.Forms.Button btnOK, btnCancel, btnPickLib, btnPickBoardDir, btnPickAsmDir;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();

            // === ROOT ===
            this.root = new System.Windows.Forms.TableLayoutPanel();
            this.root.ColumnCount = 1;
            this.root.RowCount = 2;
            this.root.Dock = System.Windows.Forms.DockStyle.Fill;
            this.root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F)); // настройки
            this.root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 48F)); // кнопки

            // === TABLE (настройки) ===
            this.tbl = new System.Windows.Forms.TableLayoutPanel();
            this.tbl.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tbl.ColumnCount = 3;
            this.tbl.RowCount = 11;
            this.tbl.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 240F)); // метки
            this.tbl.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F)); // поля
            this.tbl.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 80F));  // "..."
            for (int i = 0; i < this.tbl.RowCount; i++)
                this.tbl.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));

            // Поля
            var lblLib = new System.Windows.Forms.Label() { Text = "Библиотека корпусов:", AutoSize = true, Dock = System.Windows.Forms.DockStyle.Fill };
            txtLibDir = new System.Windows.Forms.TextBox() { Dock = System.Windows.Forms.DockStyle.Fill };
            btnPickLib = new System.Windows.Forms.Button() { Text = "...", Dock = System.Windows.Forms.DockStyle.Fill };

            var lblBoard = new System.Windows.Forms.Label() { Text = "Каталог сохранения платы (.m3d):", AutoSize = true, Dock = System.Windows.Forms.DockStyle.Fill };
            txtSaveBoardDir = new System.Windows.Forms.TextBox() { Dock = System.Windows.Forms.DockStyle.Fill };
            btnPickBoardDir = new System.Windows.Forms.Button() { Text = "...", Dock = System.Windows.Forms.DockStyle.Fill };

            var lblAsm = new System.Windows.Forms.Label() { Text = "Каталог сохранения сборки (.a3d):", AutoSize = true, Dock = System.Windows.Forms.DockStyle.Fill };
            txtSaveAsmDir = new System.Windows.Forms.TextBox() { Dock = System.Windows.Forms.DockStyle.Fill };
            btnPickAsmDir = new System.Windows.Forms.Button() { Text = "...", Dock = System.Windows.Forms.DockStyle.Fill };

            var lblHole = new System.Windows.Forms.Label() { Text = "Мин. диаметр сигн. отверстий (мм):", AutoSize = true, Dock = System.Windows.Forms.DockStyle.Fill };
            txtHoleMin = new System.Windows.Forms.TextBox() { Dock = System.Windows.Forms.DockStyle.Left, Width = 120 };

            var lblSrc = new System.Windows.Forms.Label() { Text = "Источник имени корпуса (Body/Footprint/Comment/PN):", AutoSize = true, Dock = System.Windows.Forms.DockStyle.Fill };
            txtModelSource = new System.Windows.Forms.TextBox() { Dock = System.Windows.Forms.DockStyle.Left, Width = 160 };

            var l1 = new System.Windows.Forms.Label() { Text = "BOM: Designator:", AutoSize = true, Dock = System.Windows.Forms.DockStyle.Fill };
            txtRefDes = new System.Windows.Forms.TextBox() { Dock = System.Windows.Forms.DockStyle.Left, Width = 200 };
            var l2 = new System.Windows.Forms.Label() { Text = "BOM: Stock Code:", AutoSize = true, Dock = System.Windows.Forms.DockStyle.Fill };
            txtPN = new System.Windows.Forms.TextBox() { Dock = System.Windows.Forms.DockStyle.Left, Width = 200 };
            var l3 = new System.Windows.Forms.Label() { Text = "BOM: Comment:", AutoSize = true, Dock = System.Windows.Forms.DockStyle.Fill };
            txtComment = new System.Windows.Forms.TextBox() { Dock = System.Windows.Forms.DockStyle.Left, Width = 200 };
            var l4 = new System.Windows.Forms.Label() { Text = "BOM: Body:", AutoSize = true, Dock = System.Windows.Forms.DockStyle.Fill };
            txtBody = new System.Windows.Forms.TextBox() { Dock = System.Windows.Forms.DockStyle.Left, Width = 200 };
            var l5 = new System.Windows.Forms.Label() { Text = "BOM: Footprint:", AutoSize = true, Dock = System.Windows.Forms.DockStyle.Fill };
            txtFootprint = new System.Windows.Forms.TextBox() { Dock = System.Windows.Forms.DockStyle.Left, Width = 200 };
            var l6 = new System.Windows.Forms.Label() { Text = "BOM: Description:", AutoSize = true, Dock = System.Windows.Forms.DockStyle.Fill };
            txtDesc = new System.Windows.Forms.TextBox() { Dock = System.Windows.Forms.DockStyle.Left, Width = 200 };

            int r = 0;
            this.tbl.Controls.Add(lblLib, 0, r); this.tbl.Controls.Add(txtLibDir, 1, r); this.tbl.Controls.Add(btnPickLib, 2, r); r++;
            this.tbl.Controls.Add(lblBoard, 0, r); this.tbl.Controls.Add(txtSaveBoardDir, 1, r); this.tbl.Controls.Add(btnPickBoardDir, 2, r); r++;
            this.tbl.Controls.Add(lblAsm, 0, r); this.tbl.Controls.Add(txtSaveAsmDir, 1, r); this.tbl.Controls.Add(btnPickAsmDir, 2, r); r++;
            this.tbl.Controls.Add(lblHole, 0, r); this.tbl.Controls.Add(txtHoleMin, 1, r); r++;
            this.tbl.Controls.Add(lblSrc, 0, r); this.tbl.Controls.Add(txtModelSource, 1, r); r++;
            this.tbl.Controls.Add(l1, 0, r); this.tbl.Controls.Add(txtRefDes, 1, r); r++;
            this.tbl.Controls.Add(l2, 0, r); this.tbl.Controls.Add(txtPN, 1, r); r++;
            this.tbl.Controls.Add(l3, 0, r); this.tbl.Controls.Add(txtComment, 1, r); r++;
            this.tbl.Controls.Add(l4, 0, r); this.tbl.Controls.Add(txtBody, 1, r); r++;
            this.tbl.Controls.Add(l5, 0, r); this.tbl.Controls.Add(txtFootprint, 1, r); r++;
            this.tbl.Controls.Add(l6, 0, r); this.tbl.Controls.Add(txtDesc, 1, r); r++;

            // === PANEL BUTTONS ===
            this.pnlButtons = new System.Windows.Forms.FlowLayoutPanel();
            this.pnlButtons.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlButtons.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            this.pnlButtons.Padding = new System.Windows.Forms.Padding(8, 8, 8, 8);

            this.btnOK = new System.Windows.Forms.Button() { Text = "OK", Width = 100, DialogResult = System.Windows.Forms.DialogResult.OK };
            this.btnCancel = new System.Windows.Forms.Button() { Text = "Отмена", Width = 100, DialogResult = System.Windows.Forms.DialogResult.Cancel };

            this.pnlButtons.Controls.Add(this.btnOK);
            this.pnlButtons.Controls.Add(this.btnCancel);

            // === ROOT composition ===
            this.root.Controls.Add(this.tbl, 0, 0);
            this.root.Controls.Add(this.pnlButtons, 0, 1);

            // === FORM ===
            this.Text = "Настройки";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MinimizeBox = false; this.MaximizeBox = false;
            this.ClientSize = new System.Drawing.Size(900, 520);
            this.Controls.Add(this.root);

            // Enter/Esc работают
            this.AcceptButton = this.btnOK;
            this.CancelButton = this.btnCancel;
        }
    }
}
