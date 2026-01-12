using System;
using System.Data;
using System.Drawing;
using System.Threading.Tasks;
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
        private Button _btnRebuildIndex;

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

            var panelButtons = new FlowLayoutPanel();
            panelButtons.Dock = DockStyle.Bottom;
            panelButtons.Height = 50;
            panelButtons.Padding = new Padding(10);
            panelButtons.FlowDirection = FlowDirection.RightToLeft;

            _btnSave = new Button();
            _btnSave.Text = "Save && Restart";
            _btnSave.Size = new System.Drawing.Size(150, 30);
            _btnSave.Click += (s, e) => SaveSettings();

            _btnCancel = new Button();
            _btnCancel.Text = "Cancel";
            _btnCancel.DialogResult = DialogResult.Cancel;
            _btnCancel.Size = new System.Drawing.Size(100, 30);

            _btnRebuildIndex = new Button();
            _btnRebuildIndex.Text = "Rebuild Index";
            _btnRebuildIndex.Size = new System.Drawing.Size(120, 30);
            _btnRebuildIndex.Click += OnRebuildIndexClick;
            
            var btnRepairTimeline = new Button();
            btnRepairTimeline.Text = "Repair Timeline";
            btnRepairTimeline.Size = new System.Drawing.Size(120, 30);
            btnRepairTimeline.Click += OnRepairTimelineClick;

            panelButtons.Controls.Add(_btnCancel);
            panelButtons.Controls.Add(_btnSave);
            panelButtons.Controls.Add(_btnRebuildIndex);
            panelButtons.Controls.Add(btnRepairTimeline);

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

        private void OnRebuildIndexClick(object sender, EventArgs e)
        {
            if (MessageBox.Show("This will rebuild the search index. It may take a while. Continue?", "Rebuild Index", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                _btnRebuildIndex.Enabled = false;
                var progress = new Progress<int>(count => 
                {
                    if (!_btnRebuildIndex.IsDisposed)
                        _btnRebuildIndex.Text = $"Processed {count}...";
                });
                
                Task.Run(() => 
                {
                    try 
                    {
                        if (_ocrDb == null) 
                        {
                            var settings = new SettingsManager().Load();
                            _ocrDb = new OcrDatabase(settings.StoragePath);
                        }
                        
                        _ocrDb.RebuildSearchIndex(progress);
                        
                        this.Invoke((Action)(() => 
                        {
                            MessageBox.Show("Index rebuild complete!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }));
                    } 
                    catch (Exception ex) 
                    {
                        this.Invoke((Action)(() => 
                        {
                            MessageBox.Show("Error: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }));
                    } 
                    finally 
                    {
                        if (!this.IsDisposed && !this.Disposing)
                        {
                            this.Invoke((Action)(() => 
                            {
                                _btnRebuildIndex.Text = "Rebuild Index";
                                _btnRebuildIndex.Enabled = true;
                            }));
                        }
                    }
                });
            }
        }

        private void OnRepairTimelineClick(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "Recap Timeline Files (*.sch)|*.sch";
                ofd.Title = "Select Timeline File to Repair";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    var btn = (Button)sender;
                    btn.Enabled = false;
                    string oldText = btn.Text;
                    
                    var progress = new Progress<string>(status => 
                    {
                        if (!btn.IsDisposed) btn.Text = status;
                    });

                    Task.Run(() => 
                    {
                        try 
                        {
                            var settings = new SettingsManager().Load();
                            if (_ocrDb == null) _ocrDb = new OcrDatabase(settings.StoragePath);
                            
                            var repo = new FrameRepository(settings.StoragePath, _ocrDb);
                            int count = repo.RepairDayFromBinaryFile(ofd.FileName, progress);
                            
                            this.Invoke((Action)(() => 
                            { 
                                MessageBox.Show(
                                    $"Recovery complete.\nNew frames available: {count}.\nStats reset.", 
                                    "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }));
                        } 
                        catch (Exception ex)
                        {
                            this.Invoke((Action)(() => MessageBox.Show("Error: " + ex.Message)));
                        }
                        finally
                        {
                            this.Invoke((Action)(() => 
                            { 
                                btn.Text = oldText;
                                btn.Enabled = true;
                            }));
                        }
                    });
                }
            }
        }

        private void LoadSettings()
        {
            _propertyGrid.SelectedObject = AdvancedSettings.Instance;
        }

        private void SaveSettings()
        {
            try
            {
                _propertyGrid.Focus();
                this.ActiveControl = _btnSave;
                
                AdvancedSettings.Instance.Save();
                MessageBox.Show("Settings saved. Some changes may require a restart.", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save settings: {ex.Message}", "Save Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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
