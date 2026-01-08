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

		public bool IsConnected()
		{
			return _isConnected && _pipeClient != null && _pipeClient.IsConnected;
		}

		public async Task<bool> StartUpdaterAsync(string appDirectory)
		{
			if (IsUpdaterRunning())
			{
				return await ConnectToUpdaterAsync();
			}

			try
			{
				string updaterPath = Path.Combine(appDirectory, UpdaterExeName);
				if (!File.Exists(updaterPath))
				{
					UpdateError?.Invoke(this, $"Updater executable not found at: {updaterPath}");
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
					UpdateError?.Invoke(this, "Failed to start updater process.");
					return false;
				}

				for (int i = 0; i < 10; i++)
				{
					await Task.Delay(500);
					if (await ConnectToUpdaterAsync())
					{
						return true;
					}
				}

				UpdateError?.Invoke(this, "Failed to connect to updater after starting.");
				return false;
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Error starting updater: {ex.Message}");
				UpdateError?.Invoke(this, $"Error starting updater: {ex.Message}");
				return false;
			}
		}

		public async Task<bool> ConnectToUpdaterAsync()
		{
			try
			{
				if (_pipeClient != null)
				{
					try
					{
						_pipeClient.Close();
						_pipeClient.Dispose();
					}
					catch { }
				}

				_pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
				await _pipeClient.ConnectAsync(5000);
				_isConnected = true;
				_ = Task.Run(ListenForMessages);
				return true;
			}
			catch (Exception ex)
			{
				_isConnected = false;
				System.Diagnostics.Debug.WriteLine($"Failed to connect to updater: {ex.Message}");
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

					if (bytesRead == 0)
					{
						_isConnected = false;
						System.Diagnostics.Debug.WriteLine("Pipe read returned 0 bytes - connection closed by server");
						UpdateError?.Invoke(this, "Connection to updater lost.");
						break;
					}

					if (bytesRead > 0)
					{
						string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
						System.Diagnostics.Debug.WriteLine($"Received message from updater: {message}");
						JObject? response = JsonConvert.DeserializeObject<JObject>(message);

						if (response != null)
						{
							HandleResponse(response);
						}
						else
						{
							System.Diagnostics.Debug.WriteLine("Failed to parse response as JSON");
						}
					}
				}
				catch (System.IO.IOException ioEx)
				{
					System.Diagnostics.Debug.WriteLine($"IO error reading from pipe: {ioEx.Message}");
					_isConnected = false;
					UpdateError?.Invoke(this, $"Connection error: {ioEx.Message}");
					break;
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"Error reading from pipe: {ex.Message}");
					_isConnected = false;
					UpdateError?.Invoke(this, $"Connection error: {ex.Message}");
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
						App.SetShuttingDownForUpdate(true);
						System.Windows.Application.Current.Dispatcher.Invoke(() =>
						{
							System.Windows.Application.Current.Shutdown();
						});
					}
					else if (status == "waiting")
					{
						System.Diagnostics.Debug.WriteLine("Updater is preparing the update...");
					}
					break;
			}
		}

		public async Task CheckForUpdatesAsync()
		{
			if (!_isConnected || _pipeClient == null || !_pipeClient.IsConnected)
			{
				UpdateError?.Invoke(this, "Not connected to updater. Please try again.");
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
				_isConnected = false;
				UpdateError?.Invoke(this, $"Failed to check for updates: {ex.Message}");
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

		public void ApplyUpdate()
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

				App.SetShuttingDownForUpdate(true);
			}
			catch (Exception ex)
			{
				UpdateError?.Invoke(this, ex.Message);
			}
		}

		public void KillUpdater()
		{
			try
			{
				Process[] processes = Process.GetProcessesByName("PictureDayUpdater");
				foreach (Process process in processes)
				{
					try
					{
						process.Kill();
						process.WaitForExit(1000);
					}
					catch (Exception ex)
					{
						System.Diagnostics.Debug.WriteLine($"Error killing updater process: {ex.Message}");
					}
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Error finding updater processes: {ex.Message}");
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
