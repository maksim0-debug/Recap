using System;
using System.Data;
using System.Drawing;
using System.Windows.Forms;

namespace Recap
{
    public class AdvancedSettingsForm : Form
    {
        private TabControl _tabControl;
        private TabPage _tabSettings;
        private TabPage _tabSql;
        
        private PropertyGrid _propertyGrid;
        private Button _btnSave;
        private Button _btnCancel;

        private TextBox _txtSql;
        private Button _btnExecuteSql;
        private DataGridView _gridSqlResults;
        private Label _lblSqlStatus;
        private OcrDatabase _ocrDb;

        public AdvancedSettingsForm()
        {
            InitializeComponent();
            LoadSettings();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            try
            {
                var settings = new SettingsManager().Load();
                if (!string.IsNullOrEmpty(settings.StoragePath))
                {
                    _ocrDb = new OcrDatabase(settings.StoragePath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Failed to init DB in AdvancedSettings: " + ex.Message);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            _ocrDb?.Dispose();
        }

        private void InitializeComponent()
        {
            this.Text = "Advanced Settings";
            this.Size = new System.Drawing.Size(800, 600);      
            this.StartPosition = FormStartPosition.CenterParent;

            _tabControl = new TabControl();
            _tabControl.Dock = DockStyle.Fill;

            _tabSettings = new TabPage("Settings");
            
            _propertyGrid = new PropertyGrid();
            _propertyGrid.Dock = DockStyle.Fill;     
            _propertyGrid.ToolbarVisible = false;
            _propertyGrid.PropertySort = PropertySort.Categorized;

            var panelButtons = new Panel();
            panelButtons.Dock = DockStyle.Bottom;
            panelButtons.Height = 50;

            _btnSave = new Button();
            _btnSave.Text = "Save && Restart";
            _btnSave.DialogResult = DialogResult.OK;
            _btnSave.Location = new System.Drawing.Point(620, 10);
            _btnSave.Size = new System.Drawing.Size(150, 30);
            _btnSave.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _btnSave.Click += (s, e) => SaveSettings();

            _btnCancel = new Button();
            _btnCancel.Text = "Cancel";
            _btnCancel.DialogResult = DialogResult.Cancel;
            _btnCancel.Location = new System.Drawing.Point(510, 10);
            _btnCancel.Size = new System.Drawing.Size(100, 30);
            _btnCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

            panelButtons.Controls.Add(_btnSave);
            panelButtons.Controls.Add(_btnCancel);

            _tabSettings.Controls.Add(_propertyGrid);
            _tabSettings.Controls.Add(panelButtons);

            _tabSql = new TabPage("SQL Console");
            
            var panelSqlTop = new Panel();
            panelSqlTop.Dock = DockStyle.Top;
            panelSqlTop.Height = 100;

            _txtSql = new TextBox();
            _txtSql.Multiline = true;
            _txtSql.ScrollBars = ScrollBars.Vertical;
            _txtSql.Dock = DockStyle.Fill;
            _txtSql.Font = new Font("Consolas", 10);
            _txtSql.Text = "SELECT * FROM FrameIndex LIMIT 10;";   

            var panelSqlButtons = new Panel();
            panelSqlButtons.Dock = DockStyle.Right;
            panelSqlButtons.Width = 100;

            _btnExecuteSql = new Button();
            _btnExecuteSql.Text = "Execute";
            _btnExecuteSql.Location = new Point(10, 10);
            _btnExecuteSql.Size = new Size(80, 30);
            _btnExecuteSql.Click += (s, e) => ExecuteSql();

            panelSqlButtons.Controls.Add(_btnExecuteSql);
            
            panelSqlTop.Controls.Add(_txtSql);
            panelSqlTop.Controls.Add(panelSqlButtons);

            _gridSqlResults = new DataGridView();
            _gridSqlResults.Dock = DockStyle.Fill;
            _gridSqlResults.ReadOnly = true;
            _gridSqlResults.AllowUserToAddRows = false;
            _gridSqlResults.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
            _gridSqlResults.DataError += (s, e) => { e.ThrowException = false; };     
            _gridSqlResults.CellFormatting += (s, e) => 
            {
                if (e.Value is byte[])
                {
                    e.Value = "[Binary Data]";
                    e.FormattingApplied = true;
                }
            };

            _lblSqlStatus = new Label();
            _lblSqlStatus.Dock = DockStyle.Bottom;
            _lblSqlStatus.Height = 30;
            _lblSqlStatus.TextAlign = ContentAlignment.MiddleLeft;
            _lblSqlStatus.Padding = new Padding(5, 0, 0, 0);

            _tabSql.Controls.Add(_gridSqlResults);
            _tabSql.Controls.Add(panelSqlTop);
            _tabSql.Controls.Add(_lblSqlStatus);

            _tabControl.Controls.Add(_tabSettings);
            _tabControl.Controls.Add(_tabSql);

            this.Controls.Add(_tabControl);
        }

        private void LoadSettings()
        {
            _propertyGrid.SelectedObject = AdvancedSettings.Instance;
        }

        private void SaveSettings()
        {
            AdvancedSettings.Instance.Save();
            MessageBox.Show("Settings saved. Some changes may require a restart.", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            this.Close();
        }

        private void ExecuteSql()
        {
            string sql = _txtSql.Text.Trim();
            if (string.IsNullOrWhiteSpace(sql)) return;

            _lblSqlStatus.Text = "Executing...";
            _lblSqlStatus.ForeColor = Color.Black;
            Application.DoEvents();    
            
            try
            {
                if (_ocrDb == null)
                {
                     _lblSqlStatus.Text = "Error: Database not connected (check StoragePath).";
                     _lblSqlStatus.ForeColor = Color.Red;
                     return;
                }

                var dt = _ocrDb.ExecuteRawQuery(sql);
                _gridSqlResults.DataSource = dt;
                _lblSqlStatus.Text = $"Success. Rows: {dt.Rows.Count}";
                _lblSqlStatus.ForeColor = Color.Green;
            }
            catch (Exception ex)
            {
                _lblSqlStatus.Text = $"Error: {ex.Message}";
                _lblSqlStatus.ForeColor = Color.Red;
                MessageBox.Show(ex.Message, "SQL Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
