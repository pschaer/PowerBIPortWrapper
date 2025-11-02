using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.AnalysisServices.AdomdClient;
using PowerBIPortWrapper.Models;

namespace PowerBIPortWrapper.Services
{
    public class PowerBIDetector
    {
        private readonly string _workspacesPath;

        public PowerBIDetector()
        {
            _workspacesPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\Power BI Desktop\AnalysisServicesWorkspaces"
            );
        }

        public List<PowerBIInstance> DetectRunningInstances()
        {
            var instances = new List<PowerBIInstance>();

            if (!Directory.Exists(_workspacesPath))
            {
                System.Diagnostics.Debug.WriteLine("Workspaces path does not exist");
                return instances;
            }

            try
            {
                var workspaceDirs = Directory.GetDirectories(_workspacesPath);
                System.Diagnostics.Debug.WriteLine($"Found {workspaceDirs.Length} workspace directories");

                foreach (var workspaceDir in workspaceDirs)
                {
                    try
                    {
                        var portFile = Path.Combine(workspaceDir, @"Data\msmdsrv.port.txt");
                        System.Diagnostics.Debug.WriteLine($"Checking: {portFile}");

                        if (File.Exists(portFile))
                        {
                            // Read with proper encoding detection
                            string portText = ReadPortFile(portFile);
                            System.Diagnostics.Debug.WriteLine($"Port file content: '{portText}'");

                            if (int.TryParse(portText, out int port))
                            {
                                System.Diagnostics.Debug.WriteLine($"Parsed port: {port}");

                                // Get database name by connecting to the instance
                                string databaseName = GetDatabaseName(port);
                                System.Diagnostics.Debug.WriteLine($"Database name: {databaseName ?? "(null)"}");

                                var instance = new PowerBIInstance
                                {
                                    WorkspaceId = Path.GetFileName(workspaceDir),
                                    Port = port,
                                    DatabaseName = databaseName,
                                    LastModified = Directory.GetLastWriteTime(workspaceDir),
                                    FilePath = workspaceDir,
                                    FileName = $"Workspace-{Path.GetFileName(workspaceDir).Substring(0, Math.Min(8, Path.GetFileName(workspaceDir).Length))}"
                                };

                                instances.Add(instance);
                                System.Diagnostics.Debug.WriteLine($"Added instance: {instance.FileName}");
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"Failed to parse port from: '{portText}'");
                            }
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"Port file does not exist: {portFile}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error processing workspace {workspaceDir}: {ex.Message}");
                    }
                }

                instances = instances.OrderByDescending(i => i.LastModified).ToList();
                System.Diagnostics.Debug.WriteLine($"Returning {instances.Count} instances");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error detecting instances: {ex.Message}");
            }

            return instances;
        }

        private string ReadPortFile(string portFile)
        {
            try
            {
                // Try UTF-16 LE (Little Endian) first - this is what Power BI uses
                var content = File.ReadAllText(portFile, Encoding.Unicode);
                var trimmed = content.Trim('\0', ' ', '\r', '\n', '\t');

                if (!string.IsNullOrEmpty(trimmed) && trimmed.All(c => char.IsDigit(c)))
                {
                    return trimmed;
                }
            }
            catch { }

            try
            {
                // Fallback to UTF-8
                var content = File.ReadAllText(portFile, Encoding.UTF8);
                return content.Trim('\0', ' ', '\r', '\n', '\t');
            }
            catch { }

            try
            {
                // Last resort - default encoding
                var content = File.ReadAllText(portFile);
                return content.Trim('\0', ' ', '\r', '\n', '\t');
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading port file: {ex.Message}");
                return null;
            }
        }

        private string GetDatabaseName(int port)
        {
            try
            {
                string connectionString = $"Data Source=localhost:{port};";

                using (var connection = new AdomdConnection(connectionString))
                {
                    connection.Open();

                    // Query the catalog to get database name
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT [CATALOG_NAME] FROM $SYSTEM.DBSCHEMA_CATALOGS";

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return reader.GetString(0);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting database name for port {port}: {ex.Message}");
            }

            return null;
        }

        public bool IsWorkspacePathValid()
        {
            return Directory.Exists(_workspacesPath);
        }
    }
}