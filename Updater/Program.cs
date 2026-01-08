using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PictureDayUpdater
{
	class Program
	{
		private const string PipeName = "PictureDayUpdater";
		private const string GitHubReleasesUrl = "https://api.github.com/repos/thebigyap/PictureDay/releases/latest";
		private static string? _currentVersion;
		private static string? _appDirectory;
		private static string? _tempDownloadPath;
		private static NamedPipeServerStream? _pipeServer;
		private static bool _isUpdating = false;

		static async Task Main(string[] args)
		{
			if (args.Length < 2)
			{
				Console.WriteLine("Usage: PictureDayUpdater.exe <currentVersion> <appDirectory>");
				Environment.Exit(1);
				return;
			}

			_currentVersion = args[0];
			_appDirectory = args[1];

			if (IsUpdaterAlreadyRunning())
			{
				Console.WriteLine("Updater is already running. Exiting.");
				Environment.Exit(0);
				return;
			}

			try
			{
				await StartPipeServer();
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error in updater: {ex.Message}");
				Environment.Exit(1);
			}
		}

		private static bool IsUpdaterAlreadyRunning()
		{
			Process currentProcess = Process.GetCurrentProcess();
			Process[] processes = Process.GetProcessesByName("PictureDayUpdater");

			foreach (Process process in processes)
			{
				if (process.Id != currentProcess.Id)
				{
					return true;
				}
			}
			return false;
		}

		private static async Task StartPipeServer()
		{
			while (true)
			{
				try
				{
					_pipeServer = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1,
						PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

					await _pipeServer.WaitForConnectionAsync();
					await HandleClient();
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Pipe error: {ex.Message}");
					if (_pipeServer != null)
					{
						try
						{
							_pipeServer.Disconnect();
							_pipeServer.Close();
						}
						catch { }
					}
					await Task.Delay(1000);
				}
			}
		}

		private static async Task HandleClient()
		{
			while (_pipeServer != null && _pipeServer.IsConnected)
			{
				try
				{
					byte[] buffer = new byte[4096];
					int bytesRead = await _pipeServer.ReadAsync(buffer, 0, buffer.Length);

					if (bytesRead == 0)
					{
						break;
					}

					if (bytesRead > 0)
					{
						string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
						JObject? request = JsonConvert.DeserializeObject<JObject>(message);

						if (request != null)
						{
							string? command = request["command"]?.ToString();

							switch (command)
							{
								case "check":
									await HandleCheckUpdate();
									break;
								case "download":
									await HandleDownload();
									break;
								case "apply":
									await HandleApplyUpdate();
									break;
							}
						}
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Error handling client request: {ex.Message}");
					try
					{
						await SendResponse(new { error = ex.Message });
					}
					catch
					{
						break;
					}
				}
			}

			if (_pipeServer != null)
			{
				try
				{
					_pipeServer.Disconnect();
					_pipeServer.Close();
				}
				catch { }
			}
		}

		private static async Task HandleCheckUpdate()
		{
			try
			{
				using HttpClient client = new HttpClient();
				client.DefaultRequestHeaders.Add("User-Agent", "PictureDayUpdater");
				client.Timeout = TimeSpan.FromSeconds(30);

				string url = GitHubReleasesUrl;

				string response = await client.GetStringAsync(url);
				JObject? release = JsonConvert.DeserializeObject<JObject>(response);

				if (release == null)
				{
					await SendResponse(new { command = "check", error = "Failed to parse GitHub API response" });
					return;
				}

				string? latestVersion = release["tag_name"]?.ToString()?.TrimStart('v');
				string? downloadUrl = null;

				JArray? assets = release["assets"] as JArray;
				if (assets != null)
				{
					foreach (JObject asset in assets)
					{
						string? name = asset["name"]?.ToString();
						if (name != null && name.EndsWith(".zip"))
						{
							downloadUrl = asset["browser_download_url"]?.ToString();
							break;
						}
					}
				}

				bool hasUpdate = !string.IsNullOrEmpty(latestVersion) &&
								!string.IsNullOrEmpty(downloadUrl) &&
								CompareVersions(latestVersion, _currentVersion) > 0;

				await SendResponse(new
				{
					command = "check",
					hasUpdate = hasUpdate,
					latestVersion = latestVersion ?? "",
					currentVersion = _currentVersion,
					downloadUrl = downloadUrl ?? ""
				});
			}
			catch (Exception ex)
			{
				await SendResponse(new { command = "check", error = ex.Message });
			}
		}

		private static async Task HandleDownload()
		{
			if (_isUpdating)
			{
				await SendResponse(new { command = "download", error = "Update already in progress" });
				return;
			}

			try
			{
				_isUpdating = true;
				string? downloadUrl = null;

				using HttpClient client = new HttpClient();
				client.DefaultRequestHeaders.Add("User-Agent", "PictureDayUpdater");

				string url = GitHubReleasesUrl;

				string response = await client.GetStringAsync(url);
				JObject? release = JsonConvert.DeserializeObject<JObject>(response);

				if (release != null)
				{
					JArray? assets = release["assets"] as JArray;
					if (assets != null)
					{
						foreach (JObject asset in assets)
						{
							string? name = asset["name"]?.ToString();
							if (name != null && name.EndsWith(".zip"))
							{
								downloadUrl = asset["browser_download_url"]?.ToString();
								break;
							}
						}
					}
				}

				if (string.IsNullOrEmpty(downloadUrl))
				{
					await SendResponse(new { command = "download", error = "No download URL found" });
					return;
				}

				_tempDownloadPath = Path.Combine(Path.GetTempPath(), $"PictureDayUpdate_{Guid.NewGuid()}.zip");

				using HttpClient downloadClient = new HttpClient();
				downloadClient.DefaultRequestHeaders.Add("User-Agent", "PictureDayUpdater");

				using HttpResponseMessage httpResponse = await downloadClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
				httpResponse.EnsureSuccessStatusCode();

				long? totalBytes = httpResponse.Content.Headers.ContentLength;
				long downloadedBytes = 0;

				using Stream contentStream = await httpResponse.Content.ReadAsStreamAsync();
				using FileStream fileStream = new FileStream(_tempDownloadPath, FileMode.Create, FileAccess.Write, FileShare.None);

				byte[] buffer = new byte[8192];
				int bytesRead;

				while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
				{
					await fileStream.WriteAsync(buffer, 0, bytesRead);
					downloadedBytes += bytesRead;

					if (totalBytes.HasValue)
					{
						int progress = (int)((downloadedBytes * 100) / totalBytes.Value);
						await SendResponse(new
						{
							command = "download",
							progress = progress,
							downloaded = downloadedBytes,
							total = totalBytes.Value
						});
					}
				}

				await SendResponse(new { command = "download", completed = true, filePath = _tempDownloadPath });
			}
			catch (Exception ex)
			{
				_isUpdating = false;
				await SendResponse(new { command = "download", error = ex.Message });
			}
		}

		private static async Task HandleApplyUpdate()
		{
			if (string.IsNullOrEmpty(_tempDownloadPath) || !File.Exists(_tempDownloadPath))
			{
				await SendResponse(new { command = "apply", error = "Download file not found" });
				return;
			}

			if (string.IsNullOrEmpty(_appDirectory))
			{
				await SendResponse(new { command = "apply", error = "App directory not set" });
				return;
			}

			try
			{
				await SendResponse(new { command = "apply", status = "waiting" });

				string extractPath = Path.Combine(Path.GetTempPath(), $"PictureDayExtract_{Guid.NewGuid()}");
				Directory.CreateDirectory(extractPath);

				System.IO.Compression.ZipFile.ExtractToDirectory(_tempDownloadPath, extractPath);

				await SendResponse(new { command = "apply", status = "ready" });

				string scriptPath = Path.Combine(Path.GetTempPath(), "PictureDayUpdate.bat");
				string scriptContent = $@"@echo off
timeout /t 2 /nobreak >nul
taskkill /F /IM PictureDay.exe 2>nul
timeout /t 1 /nobreak >nul

xcopy /Y /E /I ""{extractPath}\*"" ""{_appDirectory}"" >nul 2>&1
if errorlevel 1 (
    echo Update failed: xcopy error
    pause
    exit /b 1
)

start """" ""{_appDirectory}\PictureDay.exe""

timeout /t 2 /nobreak >nul
del /F /Q ""{_tempDownloadPath}"" >nul 2>&1
rmdir /S /Q ""{extractPath}"" >nul 2>&1
del /F /Q ""{scriptPath}"" >nul 2>&1
";

				await File.WriteAllTextAsync(scriptPath, scriptContent);

				ProcessStartInfo startInfo = new ProcessStartInfo
				{
					FileName = scriptPath,
					CreateNoWindow = true,
					UseShellExecute = false,
					WindowStyle = ProcessWindowStyle.Hidden
				};

				Process? scriptProcess = Process.Start(startInfo);
				if (scriptProcess == null)
				{
					await SendResponse(new { command = "apply", error = "Failed to start update script" });
					return;
				}

				try
				{
					await SendResponse(new { command = "apply", status = "applied" });
				}
				catch
				{
				}

				await Task.Delay(500);
				Environment.Exit(0);
			}
			catch (Exception ex)
			{
				await SendResponse(new { command = "apply", error = ex.Message });
			}
		}

		private static async Task SendResponse(object response)
		{
			if (_pipeServer != null && _pipeServer.IsConnected)
			{
				string json = JsonConvert.SerializeObject(response);
				byte[] data = Encoding.UTF8.GetBytes(json);
				await _pipeServer.WriteAsync(data, 0, data.Length);
				await _pipeServer.FlushAsync();
			}
		}

		private static int CompareVersions(string version1, string? version2)
		{
			if (string.IsNullOrEmpty(version2)) return 1;

			Version? v1 = ParseVersion(version1);
			Version? v2 = ParseVersion(version2);

			if (v1 == null || v2 == null) return 0;

			return v1.CompareTo(v2);
		}

		private static Version? ParseVersion(string version)
		{
			version = version.TrimStart('v', 'V');
			if (Version.TryParse(version, out Version? result))
			{
				return result;
			}
			return null;
		}
	}
}
