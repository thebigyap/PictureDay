using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PictureDay.Services
{
    public class UpdateService
    {
        private const string PipeName = "PictureDayUpdater";
        private const string UpdaterExeName = "PictureDayUpdater.exe";
        private NamedPipeClientStream? _pipeClient;
        private Process? _updaterProcess;
        private bool _isConnected = false;

        public event EventHandler<UpdateCheckResult>? UpdateCheckCompleted;
        public event EventHandler<DownloadProgressEventArgs>? DownloadProgress;
        public event EventHandler<string>? UpdateError;

        public bool IsUpdaterRunning()
        {
            Process[] processes = Process.GetProcessesByName("PictureDayUpdater");
            return processes.Length > 0;
        }

        public async Task<bool> StartUpdaterAsync(string appDirectory)
        {
            if (IsUpdaterRunning())
            {
                return false;
            }

            try
            {
                string updaterPath = Path.Combine(appDirectory, UpdaterExeName);
                if (!File.Exists(updaterPath))
                {
                    return false;
                }

                string currentVersion = App.Version;
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = updaterPath,
                    Arguments = $"\"{currentVersion}\" \"{appDirectory}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                _updaterProcess = Process.Start(startInfo);
                if (_updaterProcess == null)
                {
                    return false;
                }

                await Task.Delay(1000);
                return await ConnectToUpdaterAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error starting updater: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> ConnectToUpdaterAsync()
        {
            try
            {
                _pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await _pipeClient.ConnectAsync(5000);
                _isConnected = true;
                _ = Task.Run(ListenForMessages);
                return true;
            }
            catch
            {
                _isConnected = false;
                return false;
            }
        }

        private async Task ListenForMessages()
        {
            while (_isConnected && _pipeClient != null && _pipeClient.IsConnected)
            {
                try
                {
                    byte[] buffer = new byte[4096];
                    int bytesRead = await _pipeClient.ReadAsync(buffer, 0, buffer.Length);

                    if (bytesRead > 0)
                    {
                        string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        JObject? response = JsonConvert.DeserializeObject<JObject>(message);

                        if (response != null)
                        {
                            HandleResponse(response);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error reading from pipe: {ex.Message}");
                    _isConnected = false;
                    break;
                }
            }
        }

        private void HandleResponse(JObject response)
        {
            string? command = response["command"]?.ToString();

            if (response["error"] != null)
            {
                string? error = response["error"]?.ToString();
                UpdateError?.Invoke(this, error ?? "Unknown error");
                return;
            }

            switch (command)
            {
                case "check":
                    bool hasUpdate = response["hasUpdate"]?.ToObject<bool>() ?? false;
                    string? latestVersion = response["latestVersion"]?.ToString();
                    string? downloadUrl = response["downloadUrl"]?.ToString();

                    UpdateCheckCompleted?.Invoke(this, new UpdateCheckResult
                    {
                        HasUpdate = hasUpdate,
                        LatestVersion = latestVersion ?? "",
                        DownloadUrl = downloadUrl ?? ""
                    });
                    break;

                case "download":
                    if (response["completed"]?.ToObject<bool>() == true)
                    {
                        DownloadProgress?.Invoke(this, new DownloadProgressEventArgs { Progress = 100, Completed = true });
                    }
                    else if (response["progress"] != null)
                    {
                        int progress = response["progress"]?.ToObject<int>() ?? 0;
                        long downloaded = response["downloaded"]?.ToObject<long>() ?? 0;
                        long total = response["total"]?.ToObject<long>() ?? 0;

                        DownloadProgress?.Invoke(this, new DownloadProgressEventArgs
                        {
                            Progress = progress,
                            Downloaded = downloaded,
                            Total = total,
                            Completed = false
                        });
                    }
                    break;

                case "apply":
                    string? status = response["status"]?.ToString();
                    if (status == "ready")
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            var result = System.Windows.MessageBox.Show(
                                "Update downloaded. Click OK to apply the update and restart PictureDay.",
                                "Update Ready",
                                System.Windows.MessageBoxButton.OKCancel,
                                System.Windows.MessageBoxImage.Information);

                            if (result == System.Windows.MessageBoxResult.OK)
                            {
                                ApplyUpdate();
                            }
                        });
                    }
                    break;
            }
        }

        public async Task CheckForUpdatesAsync()
        {
            if (!_isConnected || _pipeClient == null || !_pipeClient.IsConnected)
            {
                UpdateError?.Invoke(this, "Not connected to updater");
                return;
            }

            try
            {
                JObject request = new JObject
                {
                    ["command"] = "check"
                };

                string json = JsonConvert.SerializeObject(request);
                byte[] data = Encoding.UTF8.GetBytes(json);
                await _pipeClient.WriteAsync(data, 0, data.Length);
                await _pipeClient.FlushAsync();
            }
            catch (Exception ex)
            {
                UpdateError?.Invoke(this, ex.Message);
            }
        }

        public async Task StartDownloadAsync()
        {
            if (!_isConnected || _pipeClient == null || !_pipeClient.IsConnected)
            {
                UpdateError?.Invoke(this, "Not connected to updater");
                return;
            }

            try
            {
                JObject request = new JObject
                {
                    ["command"] = "download"
                };

                string json = JsonConvert.SerializeObject(request);
                byte[] data = Encoding.UTF8.GetBytes(json);
                await _pipeClient.WriteAsync(data, 0, data.Length);
                await _pipeClient.FlushAsync();
            }
            catch (Exception ex)
            {
                UpdateError?.Invoke(this, ex.Message);
            }
        }

        private void ApplyUpdate()
        {
            if (!_isConnected || _pipeClient == null || !_pipeClient.IsConnected)
            {
                UpdateError?.Invoke(this, "Not connected to updater");
                return;
            }

            try
            {
                JObject request = new JObject
                {
                    ["command"] = "apply"
                };

                string json = JsonConvert.SerializeObject(request);
                byte[] data = Encoding.UTF8.GetBytes(json);
                _pipeClient.Write(data, 0, data.Length);
                _pipeClient.Flush();

                System.Windows.Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                UpdateError?.Invoke(this, ex.Message);
            }
        }

        public void Dispose()
        {
            _isConnected = false;
            _pipeClient?.Close();
            _pipeClient?.Dispose();
        }
    }

    public class UpdateCheckResult
    {
        public bool HasUpdate { get; set; }
        public string LatestVersion { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
    }

    public class DownloadProgressEventArgs : EventArgs
    {
        public int Progress { get; set; }
        public long Downloaded { get; set; }
        public long Total { get; set; }
        public bool Completed { get; set; }
    }
}
