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

namespace PackageSubmoduleDownloader
{
    public class PackageSubmoduleDownloader
    {
        
        private static readonly List<string> submoduleDirectories = new List<string>();

        private static string UnityDirectoryPath = $"{Application.dataPath}/..";
        private static string PackageCachePath => $"{UnityDirectoryPath}/Library/PackageCache";
        private static string TempDirectoryPath = $"{UnityDirectoryPath}/Temp";
        
        // uiとエラーハンドリング
        // openupm登録したい

        [MenuItem("Assets/Package Submodule Downloader")]
        public static async Task DownloadSubmodule()
        {
            var directories = Directory.GetDirectories(PackageCachePath);
            foreach (var directory in directories)
            {
                submoduleDirectories.Clear();
                SearchEmptyDirectory(directory);
                var isExitsSubModule = submoduleDirectories.Count != 0;
                if (!isExitsSubModule) continue;
                
                var githubUrl = GetGitHubUrl(directory);
                var repositoryName = githubUrl.Split('/').Last();
                var downloadPath = $"{TempDirectoryPath}/{repositoryName}";
                var gitmoduleString = await GitSubmodulesAsync(githubUrl, downloadPath);
                if (gitmoduleString == "") continue;
                
                var submoduleUrls = ParseUrls(gitmoduleString);
                foreach (var url in submoduleUrls)
                {
                    var directoryName = url.Split('/').Last();
                    var tempDirectory = $"{TempDirectoryPath}/{directoryName}";
                    DownloadRepositoryZip(url, $"{TempDirectoryPath}/{directoryName}");
                    UnZip($"{tempDirectory}.zip");
                    foreach (var subModuleDirectory in submoduleDirectories)
                    {
                        var isTargetDirecotry = subModuleDirectory.Contains(directoryName);
                        if (isTargetDirecotry)
                        {
                            var sourceDirectory = Directory.GetDirectories(tempDirectory).First();
                            DirectoryCopy(sourceDirectory, subModuleDirectory);
                        }
                    }
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
                // e.g.) `Window` -> `Package Manager` -> `Add package from git URL` and paste `github url`.
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

        private static async void DownloadRepositoryZip(string url, string downloadPath)
        {
            using (var client = new WebClient())
            {
                var lastVersion = await GetLastTag($"{url}/releases/latest/download");
                client.DownloadFile($"{url}/archive/{lastVersion}.zip",  $"{downloadPath}.zip");
            }
        }
        
        private static async Task<string> GetLastTag(string url)
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
        
        private static void UnZip(string zipPath)
        {
            var unityEditorDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var sevenZipPath = $"{unityEditorDirectory}/Data/Tools/7z.exe";
            var outputPath = zipPath.Replace(".zip", "");
            new Process
            {
                StartInfo =
                {
                    FileName = $"\"{sevenZipPath}\"",
                    Arguments = $"x \"{zipPath}\" -o\"{outputPath}\" -r",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            }.Start();
        }
        
        private static void DirectoryCopy(string sourceDirName, string destDirName)
        {
            var directoryInfo = new DirectoryInfo(sourceDirName);
            var directoryInfos = directoryInfo.GetDirectories();
            Directory.CreateDirectory(destDirName);

            var files = directoryInfo.GetFiles();
            foreach (var file in files)
            {
                var tempPath = Path.Combine(destDirName, file.Name);
                file.CopyTo(tempPath, false);
            }

            foreach (var directory in directoryInfos)
            {
                var tempPath = Path.Combine(destDirName, directory.Name);
                DirectoryCopy(directory.FullName, tempPath);
            }
        }
    }
}
