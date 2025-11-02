using System;
using System.Linq;
using System.Windows.Forms;
using PowerBIPortWrapper.Models;
using PowerBIPortWrapper.Services;

namespace PowerBIPortWrapper
{
    public partial class MainForm : Form
    {
        private PowerBIDetector _detector;
        private XmlaProxyService _proxyService;
        private ConfigurationManager _configManager;
        private ProxyConfiguration _config;
        private PowerBIInstance _selectedInstance;

        public MainForm()
        {
            InitializeComponent();
            InitializeServices();
            InitializeEventHandlers();
            LoadConfiguration();
            RefreshInstances();
        }

        private void InitializeServices()
        {
            _detector = new PowerBIDetector();
            _proxyService = new XmlaProxyService();  // Changed from TcpProxyService
            _configManager = new ConfigurationManager();

            _proxyService.OnLog += (sender, message) =>
            {
                LogMessage(message);
            };

            _proxyService.OnError += (sender, message) =>
            {
                LogMessage($"ERROR: {message}");
                MessageBox.Show(message, "Proxy Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
        }

        private void InitializeEventHandlers()
        {
            // Wire up event handlers
            buttonStart.Click += ButtonStart_Click;
            buttonStop.Click += ButtonStop_Click;
            buttonRefresh.Click += ButtonRefresh_Click;
            buttonCopy.Click += ButtonCopy_Click;
            buttonDebug.Click += buttonDebug_Click;
            checkBoxNetworkAccess.CheckedChanged += CheckBoxNetworkAccess_CheckedChanged;
            listBoxInstances.SelectedIndexChanged += ListBoxInstances_SelectedIndexChanged;

            // Handle form closing
            this.FormClosing += MainForm_FormClosing;

            // Initialize UI state
            UpdateStatus("Not Running", false);
        }

        private void LoadConfiguration()
        {
            _config = _configManager.LoadConfiguration();

            // Apply configuration to UI
            textBoxFixedPort.Text = _config.FixedPort.ToString();
            checkBoxNetworkAccess.Checked = _config.AllowNetworkAccess;
            textBoxNetworkPort.Text = _config.NetworkPort.ToString();

            UpdateConnectionString();
        }

        private void SaveConfiguration()
        {
            try
            {
                // Update config from UI
                if (int.TryParse(textBoxFixedPort.Text, out int fixedPort))
                {
                    _config.FixedPort = fixedPort;
                }

                _config.AllowNetworkAccess = checkBoxNetworkAccess.Checked;

                if (int.TryParse(textBoxNetworkPort.Text, out int networkPort))
                {
                    _config.NetworkPort = networkPort;
                }

                if (_selectedInstance != null)
                {
                    _config.LastSelectedInstance = _selectedInstance.WorkspaceId;
                }

                _configManager.SaveConfiguration(_config);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving configuration: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RefreshInstances()
        {
            listBoxInstances.Items.Clear();

            if (!_detector.IsWorkspacePathValid())
            {
                listBoxInstances.Items.Add("Power BI Desktop workspace folder not found");
                return;
            }

            var instances = _detector.DetectRunningInstances();

            if (instances.Count == 0)
            {
                listBoxInstances.Items.Add("No running Power BI Desktop instances found");
                return;
            }

            foreach (var instance in instances)
            {
                listBoxInstances.Items.Add(instance);
            }

            // Select the first instance by default, or the last selected one
            if (instances.Count > 0)
            {
                if (!string.IsNullOrEmpty(_config.LastSelectedInstance))
                {
                    var lastInstance = instances.FirstOrDefault(i => i.WorkspaceId == _config.LastSelectedInstance);
                    if (lastInstance != null)
                    {
                        listBoxInstances.SelectedItem = lastInstance;
                        return;
                    }
                }

                listBoxInstances.SelectedIndex = 0;
            }
        }

        private async void ButtonStart_Click(object sender, EventArgs e)
        {
            if (_selectedInstance == null)
            {
                MessageBox.Show("Please select a Power BI Desktop instance first.", "No Instance Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrEmpty(_selectedInstance.DatabaseName))
            {
                MessageBox.Show("Could not detect database name. Please refresh instances and try again.", "Database Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!int.TryParse(textBoxFixedPort.Text, out int fixedPort) || fixedPort < 1024 || fixedPort > 65535)
            {
                MessageBox.Show("Please enter a valid port number (1024-65535).", "Invalid Port", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                ClearLog();
                LogMessage($"Starting proxy for {_selectedInstance.FileName}...");
                LogMessage($"Target database: {_selectedInstance.DatabaseName}");

                bool allowRemote = checkBoxNetworkAccess.Checked;
                await _proxyService.StartAsync(fixedPort, _selectedInstance.Port, _selectedInstance.DatabaseName, allowRemote);

                UpdateStatus("Running", true);
                SaveConfiguration();

                MessageBox.Show(
                    $"Proxy started successfully!\n\n" +
                    $"Server: localhost:{fixedPort}\n" +
                    $"Target Database: {_selectedInstance.DatabaseName}\n\n" +
                    $"You can connect with ANY database name - it will be automatically\n" +
                    $"rewritten to the correct Power BI database.\n\n" +
                    $"Suggested connection strings:\n" +
                    $"• Data Source=localhost:{fixedPort};Initial Catalog=PowerBI\n" +
                    $"• Data Source=localhost:{fixedPort};Initial Catalog=Model\n" +
                    $"• Data Source=localhost:{fixedPort} (no catalog specified)\n\n" +
                    $"Network Access: {(allowRemote ? "Enabled" : "Disabled")}",
                    "Proxy Started",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
            catch (Exception ex)
            {
                LogMessage($"Failed to start: {ex.Message}");
                MessageBox.Show($"Failed to start proxy:\n\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                UpdateStatus("Error", false);
            }
        }

        private void ButtonStop_Click(object sender, EventArgs e)
        {
            try
            {
                _proxyService.Stop();
                UpdateStatus("Stopped", false);
                MessageBox.Show("Proxy stopped successfully.", "Proxy Stopped", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error stopping proxy:\n\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ButtonRefresh_Click(object sender, EventArgs e)
        {
            RefreshInstances();
            MessageBox.Show("Instance list refreshed.", "Refresh Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ButtonCopy_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(textBoxConnectionString.Text))
            {
                Clipboard.SetText(textBoxConnectionString.Text);
                MessageBox.Show("Connection string copied to clipboard!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void CheckBoxNetworkAccess_CheckedChanged(object sender, EventArgs e)
        {
            bool enabled = checkBoxNetworkAccess.Checked;
            labelNetworkPort.Enabled = enabled;
            textBoxNetworkPort.Enabled = enabled;
            UpdateConnectionString();
        }

        private void ListBoxInstances_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Prevent changing selection while proxy is running
            if (_proxyService != null && _proxyService.IsRunning)
            {
                // Revert to the selected instance
                if (_selectedInstance != null)
                {
                    listBoxInstances.SelectedItem = _selectedInstance;
                }
                return;
            }

            if (listBoxInstances.SelectedItem is PowerBIInstance instance)
            {
                _selectedInstance = instance;
                UpdateConnectionString();
            }
            else
            {
                _selectedInstance = null;
            }
        }

        private void UpdateConnectionString()
        {
            if (int.TryParse(textBoxFixedPort.Text, out int port))
            {
                if (_selectedInstance != null && !string.IsNullOrEmpty(_selectedInstance.DatabaseName))
                {
                    // Show the ACTUAL database name for testing
                    textBoxConnectionString.Text = $"Data Source=localhost:{port};Initial Catalog={_selectedInstance.DatabaseName}";
                }
                else
                {
                    textBoxConnectionString.Text = $"Data Source=localhost:{port}";
                }
            }
        }

        private void UpdateStatus(string status, bool isRunning)
        {
            labelStatus.Text = $"Status: {status}";
            labelStatus.ForeColor = isRunning ? System.Drawing.Color.Green : System.Drawing.Color.Red;

            buttonStart.Enabled = !isRunning;
            buttonStop.Enabled = isRunning;
            buttonRefresh.Enabled = !isRunning;

            // Disable configuration controls while running
            textBoxFixedPort.Enabled = !isRunning;
            checkBoxNetworkAccess.Enabled = !isRunning;
            textBoxNetworkPort.Enabled = !isRunning && checkBoxNetworkAccess.Checked;

            // Simply disable the listbox - accept default Windows appearance
            listBoxInstances.Enabled = !isRunning;
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Stop proxy if running
            if (_proxyService.IsRunning)
            {
                var result = MessageBox.Show(
                    "The proxy is still running. Stop it and exit?",
                    "Proxy Running",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (result == DialogResult.Yes)
                {
                    _proxyService.Stop();
                }
                else
                {
                    e.Cancel = true;
                    return;
                }
            }

            // Save configuration
            SaveConfiguration();
        }

        private void LogMessage(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        }

        private void ClearLog()
        {
            // Nothing to clear if we're only using Debug output
        }

        private void buttonDebug_Click(object sender, EventArgs e)
        {
            var workspacesPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\Power BI Desktop\AnalysisServicesWorkspaces"
            );

            var debugInfo = new System.Text.StringBuilder();
            debugInfo.AppendLine($"Workspaces Path: {workspacesPath}");
            debugInfo.AppendLine($"Path Exists: {Directory.Exists(workspacesPath)}");
            debugInfo.AppendLine();

            if (Directory.Exists(workspacesPath))
            {
                var dirs = Directory.GetDirectories(workspacesPath);
                debugInfo.AppendLine($"Found {dirs.Length} workspace folder(s):");
                debugInfo.AppendLine();

                foreach (var dir in dirs)
                {
                    debugInfo.AppendLine($"Workspace: {Path.GetFileName(dir)}");
                    debugInfo.AppendLine($"  Last Modified: {Directory.GetLastWriteTime(dir)}");

                    var portFile = Path.Combine(dir, @"Data\msmdsrv.port.txt");
                    debugInfo.AppendLine($"  Port File Path: {portFile}");
                    debugInfo.AppendLine($"  Port File Exists: {File.Exists(portFile)}");

                    if (File.Exists(portFile))
                    {
                        try
                        {
                            var portBytes = File.ReadAllBytes(portFile);
                            debugInfo.AppendLine($"  Port File Size: {portBytes.Length} bytes");
                            debugInfo.AppendLine($"  Port Content (hex): {BitConverter.ToString(portBytes)}");

                            // Only show UTF-16 (the actual encoding used)
                            var utf16Content = System.Text.Encoding.Unicode.GetString(portBytes).Trim('\0', ' ', '\r', '\n', '\t');
                            debugInfo.AppendLine($"  Port Content (UTF-16): '{utf16Content}'");

                            string portContent = utf16Content;

                            if (int.TryParse(portContent, out int port))
                            {
                                debugInfo.AppendLine($"  ✓ Port parsed successfully: {port}");

                                // Try to get database name
                                try
                                {
                                    string connStr = $"Data Source=localhost:{port};";
                                    debugInfo.AppendLine($"  Attempting connection: {connStr}");

                                    using (var conn = new Microsoft.AnalysisServices.AdomdClient.AdomdConnection(connStr))
                                    {
                                        conn.Open();
                                        debugInfo.AppendLine($"  ✓ Connection opened successfully!");

                                        var cmd = conn.CreateCommand();
                                        cmd.CommandText = "SELECT [CATALOG_NAME] FROM $SYSTEM.DBSCHEMA_CATALOGS";

                                        using (var reader = cmd.ExecuteReader())
                                        {
                                            int dbCount = 0;
                                            while (reader.Read())
                                            {
                                                string dbName = reader.GetString(0);
                                                debugInfo.AppendLine($"  Database {dbCount + 1}: {dbName}");
                                                dbCount++;
                                            }

                                            if (dbCount == 0)
                                            {
                                                debugInfo.AppendLine($"  ⚠ No databases found");
                                            }
                                            else
                                            {
                                                debugInfo.AppendLine($"  ✓ Total databases: {dbCount}");
                                            }
                                        }
                                    }
                                }
                                catch (Exception dbEx)
                                {
                                    debugInfo.AppendLine($"  ✗ Error connecting/querying database:");
                                    debugInfo.AppendLine($"    {dbEx.GetType().Name}: {dbEx.Message}");
                                    if (dbEx.InnerException != null)
                                    {
                                        debugInfo.AppendLine($"    Inner: {dbEx.InnerException.Message}");
                                    }
                                }
                            }
                            else
                            {
                                debugInfo.AppendLine($"  ✗ ERROR: Could not parse port number from: '{portContent}'");
                            }
                        }
                        catch (Exception ex)
                        {
                            debugInfo.AppendLine($"  ✗ Error reading port file:");
                            debugInfo.AppendLine($"    {ex.GetType().Name}: {ex.Message}");
                        }
                    }
                    else
                    {
                        var dataFolder = Path.Combine(dir, "Data");
                        if (Directory.Exists(dataFolder))
                        {
                            var files = Directory.GetFiles(dataFolder);
                            debugInfo.AppendLine($"  Data folder exists with {files.Length} files:");
                            foreach (var file in files)
                            {
                                debugInfo.AppendLine($"    - {Path.GetFileName(file)}");
                            }
                        }
                        else
                        {
                            debugInfo.AppendLine($"  ✗ Data folder doesn't exist!");
                        }
                    }
                    debugInfo.AppendLine();
                    debugInfo.AppendLine("=".PadRight(70, '='));
                    debugInfo.AppendLine();
                }
            }

            debugInfo.AppendLine();
            debugInfo.AppendLine("DETECTOR TEST:");
            debugInfo.AppendLine("=".PadRight(70, '='));

            var instances = _detector.DetectRunningInstances();
            debugInfo.AppendLine($"Detector found {instances.Count} instance(s):");

            if (instances.Count == 0)
            {
                debugInfo.AppendLine("  ✗ No instances detected!");
            }
            else
            {
                foreach (var instance in instances)
                {
                    debugInfo.AppendLine($"  ✓ {instance.FileName}");
                    debugInfo.AppendLine($"    Port: {instance.Port}");
                    debugInfo.AppendLine($"    Database: {instance.DatabaseName ?? "(null)"}");
                    debugInfo.AppendLine($"    WorkspaceId: {instance.WorkspaceId}");
                    debugInfo.AppendLine();
                }
            }

            // Create and show the debug form
            var debugForm = new Form
            {
                Text = "Debug Information",
                Width = 750,
                Height = 550,
                StartPosition = FormStartPosition.CenterParent
            };

            var textBox = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                Dock = DockStyle.Fill,
                Text = debugInfo.ToString(),
                Font = new System.Drawing.Font("Consolas", 8.5f),
                WordWrap = false
            };

            var copyButton = new Button
            {
                Text = "Copy to Clipboard",
                Dock = DockStyle.Bottom,
                Height = 30
            };

            copyButton.Click += (s, ev) =>
            {
                Clipboard.SetText(debugInfo.ToString());
                MessageBox.Show("Debug info copied to clipboard!", "Copied", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            debugForm.Controls.Add(textBox);
            debugForm.Controls.Add(copyButton);

            // Show as modal dialog - should only show once
            debugForm.ShowDialog(this);
        }
    }
}
