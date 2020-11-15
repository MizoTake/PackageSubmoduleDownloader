using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace PackageSubmoduleDownloader
{
    public class PackageSubmoduleDownloader
    {

        private static float progress;
        private static float per = 1;
        private static readonly List<string> submoduleDirectories = new List<string>();

        private static string UnityDirectoryPath = $"{Application.dataPath}/..";
        private static string PackageCachePath => $"{UnityDirectoryPath}/Library/PackageCache";
        private static string TempDirectoryPath = $"{UnityDirectoryPath}/Temp";

        [MenuItem("Assets/Package Submodule Downloader")]
        public static async Task Execute()
        {
            AssetDatabase.DisallowAutoRefresh();
            progress = 0;
            UpdateProgressBar(0.1f, nameof(Execute));
            await DownloadSubmoduleAsync();
            AssetDatabase.Refresh();
            UpdateProgressBar(1 - progress, "Finish");
            AssetDatabase.AllowAutoRefresh();
            EditorUtility.ClearProgressBar();
        }
        
        private static void UpdateProgressBar(float addProgress, string info)
        {
            if (progress + addProgress > 0.9) addProgress = 0;
            progress += addProgress;
            Debug.Log($"[{nameof(PackageSubmoduleDownloader)}] {info} : {progress}");
            EditorUtility.DisplayProgressBar(nameof(PackageSubmoduleDownloader), info, progress / per);
        }
        
        public static async Task DownloadSubmoduleAsync()
        {
            var directories = Directory.GetDirectories(PackageCachePath);
            per = directories.Length;
            foreach (var directory in directories)
            {
                submoduleDirectories.Clear();
                SearchEmptyDirectory(directory);
                var isExitsSubModule = submoduleDirectories.Count != 0;
                if (!isExitsSubModule) continue;
                UpdateProgressBar(0.3f, nameof(GetGitHubUrl));

                var githubUrl = GetGitHubUrl(directory);
                var repositoryName = githubUrl.Split('/').Last();
                var downloadPath = $"{TempDirectoryPath}/{repositoryName}";
                var gitmoduleString = await GitSubmodulesAsync(githubUrl, downloadPath);
                if (gitmoduleString == "") continue;
                UpdateProgressBar(0.3f, nameof(ParseUrls));

                var submoduleUrls = ParseUrls(gitmoduleString);
                var per = submoduleUrls.Count();
                foreach (var url in submoduleUrls)
                {
                    var directoryName = url.Split('/').Last();
                    var tempDirectory = $"{TempDirectoryPath}/{directoryName}";
                    await DownloadRepositoryZipAsync(url, $"{TempDirectoryPath}/{directoryName}");
                    UpdateProgressBar(0.1f / per, nameof(DownloadRepositoryZipAsync));
                    foreach (var subModuleDirectory in from subModuleDirectory in submoduleDirectories let isTargetDirecotry = subModuleDirectory.Contains(directoryName) where isTargetDirecotry select subModuleDirectory)
                    {
                        await UnZipAsync($"{tempDirectory}.zip");
                        UpdateProgressBar(0.1f / per, nameof(UnZipAsync));
                        var source = Directory.GetDirectories(tempDirectory).First();
                        await DirectoryMoveAsync(source, subModuleDirectory);
                    }
                    UpdateProgressBar(0.1f / per, nameof(DirectoryMoveAsync));
                }
            }
        }
        
        private static void SearchEmptyDirectory(string path)
        {
            foreach (var directory in Directory.GetDirectories(path))
            {
                SearchEmptyDirectory(directory);
                var isEmptyFiles = Directory.GetFiles(directory).Length == 0;
                var isEmptyDirectories = Directory.GetDirectories(directory).Length == 0;
                if (isEmptyFiles && isEmptyDirectories)
                {
                    submoduleDirectories.Add(directory);
                }
            }
        }
        
        private static string GetGitHubUrl(string path)
        {
            var readmeFileName = $"{path}/README.md";
            if (!File.Exists(readmeFileName)) return "";
            using (var raedmeFile = new StreamReader(readmeFileName))
            {
                var readme = raedmeFile.ReadToEnd().Split('\n');
                var urlLine = readme.FirstOrDefault(x => x.StartsWith("    \"com."));
                if (urlLine == null) return "";
                // e.g.)    "com.example.package": "https://github.com/example/package",
                return urlLine.Split('"')[3].Split('?').First().Replace(".git", "");
            }
        }

        private static async Task<string> GitSubmodulesAsync(string url, string downloadPath)
        {
            var gitmoduleUrl = $"{url}/master/.gitmodules".Replace("github.com", "raw.githubusercontent.com");
            var isExistsGitModules = await IsExistsGitSubmodulesAsync(gitmoduleUrl);
            if (!isExistsGitModules) return "";
            Directory.CreateDirectory(downloadPath);
            var client = new HttpClient();
            var response = await client.GetAsync(gitmoduleUrl);
            return await response.Content.ReadAsStringAsync();
        }

        private static async Task<bool> IsExistsGitSubmodulesAsync(string url)
        {
            var client = new HttpClient();
            var response = await client.GetAsync(url);
            try
            {
                response.EnsureSuccessStatusCode();
            }
            catch
            {
                // ignored
            }
            return response.IsSuccessStatusCode;
        }

        private static IEnumerable<string> ParseUrls(string gitmoduleString)
        {
            var fileValue = gitmoduleString.Split('\n');
            return fileValue.Where(x => x.StartsWith("	url =")).Select(x => x.Replace("	url = ", "").Replace(".git", ""));
        }

        private static async Task DownloadRepositoryZipAsync(string url, string downloadPath)
        {
            var completionSource = new TaskCompletionSource<TaskStatus>();
            using (var client = new WebClient())
            {
                var lastVersion = await GetLastTagAsync($"{url}/releases/latest/download");
                client.DownloadFileCompleted += (sender, args) =>
                {
                    completionSource.SetResult(TaskStatus.RanToCompletion);
                };
                client.DownloadFileAsync(new Uri($"{url}/archive/{lastVersion}.zip"),  $"{downloadPath}.zip");
            }
            await completionSource.Task;
        }

        private static async Task<string> GetLastTagAsync(string url)
        {
            var client = new HttpClient();
            var response = await client.GetAsync(url);
            try
            {
                response.EnsureSuccessStatusCode();
            }
            catch
            {
                // ignored
            }
            return response.RequestMessage.RequestUri.ToString().Split('/').Last();
        }
        
        private static Task<int> UnZipAsync(string zipPath)
        {
            var completionSource = new TaskCompletionSource<int>();
            var contentName = zipPath.Replace(".zip", "").Split('/').Last();
            var outputPath = $"{TempDirectoryPath}/{contentName}";
#if UNITY_EDITOR_WIN
            var unityEditorDirectory = System.AppDomain.CurrentDomain.BaseDirectory;
            var sevenZipPath = $"{unityEditorDirectory}/Data/Tools/7z.exe";
            var fileName = $"\"{sevenZipPath}\"";
            var arguments = $"x \"{zipPath}\" -o\"{outputPath}\" -r";
#else
            var fileName = "unzip";
            var arguments = $"\"{zipPath}\" -d \"{outputPath}\"";
#endif
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
            var process = new Process
            {
                StartInfo =
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                },
                EnableRaisingEvents = true
            };
            
            process.Exited += (sender, args) =>
            {
                completionSource.SetResult(process.ExitCode);
                process.Dispose();
            };
            process.OutputDataReceived += (sender, args) => {
                if (!string.IsNullOrEmpty(args.Data)) {
                    Debug.Log(args.Data);
                }
            };
            process.ErrorDataReceived += (sender, args) => {
                if (!string.IsNullOrEmpty(args.Data)) {
                    Debug.LogError(args.Data);
                }
            };
            process.Start();
            return completionSource.Task;
        }
        
        private static async Task DirectoryMoveAsync(string sourceDirectory, string destDirectory)
        {
            var sourceInfo = new DirectoryInfo(sourceDirectory);

            var directories = sourceInfo.GetDirectories();
            Directory.CreateDirectory(destDirectory);
            
            var files = sourceInfo.GetFiles();
            foreach (var file in files)
            {
                var destFileName = Path.Combine(destDirectory, file.Name);

                File.Move(file.FullName, destFileName);
            }

            foreach (var directory in directories)
            {
                var dest = Path.Combine(destDirectory, directory.Name);
                await DirectoryMoveAsync(directory.FullName, dest);
            }
        }
    }
}
