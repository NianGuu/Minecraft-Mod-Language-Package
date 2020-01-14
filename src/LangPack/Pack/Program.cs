﻿using Octokit;
using Qiniu.CDN;
using Qiniu.Storage;
using Qiniu.Util;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

namespace Pack
{
    class Program
    {
        public static async Task Main(string[] args)
        {
            Stopwatch sw = Stopwatch.StartNew();
            var access_key = Environment.GetEnvironmentVariable("ak");
            var secret_key = Environment.GetEnvironmentVariable("sk");
            var reference = Environment.GetEnvironmentVariable("ref");
            var sha = Environment.GetEnvironmentVariable("sha");
            var github_actor = Environment.GetEnvironmentVariable("actor");
            var github_token = Environment.GetEnvironmentVariable("repo_token");


            var repoPath = GetTargetParentDirectory(Environment.CurrentDirectory, ".git");
            Directory.SetCurrentDirectory(repoPath);
            if (File.Exists(@"./Minecraft-Mod-Language-Modpack.zip"))
            {
                File.Delete(@"./Minecraft-Mod-Language-Modpack.zip");
            }
            Console.WriteLine("Start packing!");

            var paths = Directory.EnumerateFiles(@"./project", "*", SearchOption.AllDirectories).ToAsyncEnumerable()
                .Where(_ => _.EndsWith("zh_cn.lang"))
                .Append(@"./project/pack.png")
                .Append(@"./project/pack.mcmeta")
                .Select(_ => new { src = _, dest = Path.GetRelativePath(@"./project", _) })
                .Append(new {src= @"./README.md",dest= @"README.md" })
                .Append(new {src= @"./LICENSE", dest= @"LICENSE" })
                .Append(new {src= @"./database/asset_map.json", dest= @"assets/i18nmod/asset_map/asset_map.json" });

            Directory.CreateDirectory(@"./out");
            Console.WriteLine($"Totall found {paths.CountAsync()} files ");
            await using var zipFile = File.OpenWrite(@"./Minecraft-Mod-Language-Modpack.zip"); 
            using var zipArchive = new ZipArchive(zipFile,ZipArchiveMode.Create);
            await foreach (var path in paths)
            {
                await using var fs = File.OpenRead(path.src);
                var entry = zipArchive.CreateEntry(path.dest, CompressionLevel.Optimal);
                await using var zipStream = entry.Open();
                await fs.CopyToAsync(zipStream);
                Console.WriteLine($"Added {path.dest}!");
            }
            Console.WriteLine("Completed!");
            if (!string.IsNullOrEmpty(github_token))
            {
                var client = new GitHubClient(new ProductHeaderValue("CFPA"));

                client.Credentials = new Credentials(github_token);
                var user = await client.User.Current();
                var actor = await client.User.Get(github_actor);
                var repo = await client.Repository.Get(user.Name, "Minecraft-Mod-Language-Package");
                var commitMessage = (await client.Repository.Commit.Get(repo.Id, reference)).Commit.Message;
                var comment = string.Join("\n",
                    (await client.Repository.Comment.GetAllForCommit(repo.Id, sha)).Select(c => c.Body));
                var tagName = $"汉化资源包-Snapshot-{DateTime.UtcNow.ToString("yyyyMMddhhmmss")}";
                var tag = new NewTag
                {
                    Object = sha,
                    Message = comment,
                    Tag = tagName,
                    Type = TaggedType.Commit,
                    Tagger = new Committer(name: actor.Name, email: actor.Email, date: DateTimeOffset.UtcNow)
                };
                var tagResult = await client.Git.Tag.Create(repo.Id, tag);
                Console.WriteLine("Created a tag for {0} at {1}", tagResult.Tag, tagResult.Sha);
                var newRelease = new NewRelease(tagName)
                {
                    Name = tagName + $":{commitMessage}",
                    Body = tag.Message,
                    Draft = false,
                    Prerelease = false
                };
                var releaseResult = await client.Repository.Release.Create(repo.Id, newRelease);
                Console.WriteLine("Created release id {0}", releaseResult.Id);

                var assetUpload = new ReleaseAssetUpload()
                {
                    FileName = "Minecraft-Mod-Language-Modpack.zip",
                    ContentType = "application/zip",
                    RawData = zipFile
                };
                var release = await client.Repository.Release.Get(repo.Id, releaseResult.Id);
                var asset = await client.Repository.Release.UploadAsset(release, assetUpload);
            }

            await zipFile.DisposeAsync();

            if ((!string.IsNullOrEmpty(access_key)) && (!string.IsNullOrEmpty(secret_key)))
            {
                Mac mac = new Mac(access_key, secret_key);
                PutPolicy putPolicy = new PutPolicy
                {
                    Scope = "langpack"
                };
                putPolicy.SetExpires(120);
                string token = Auth.CreateUploadToken(mac, putPolicy.ToJsonString());
                UploadManager um = new UploadManager(new Config());
                var result = um.UploadFile(@"./Minecraft-Mod-Language-Modpack.zip",
                    "Minecraft-Mod-Language-Modpack.zip", token, new PutExtra());
                Console.WriteLine(result.Text);
                var cdnm = new CdnManager(mac);
                var refreshResult = cdnm.RefreshUrls(new[] { "http://downloader.meitangdehulu.com/Minecraft-Mod-Language-Modpack.zip" });
                Console.WriteLine(refreshResult.Text);

                sw.Stop();
                Console.WriteLine($"All works finished in {sw.Elapsed.Milliseconds}ms");
            }
        }
            
            

            
        private static string GetTargetParentDirectory(string path, string containDir)
        {
            if (Directory.Exists(Path.Combine(path, containDir)))
            {
                return path;
            }else
            {
                if (Path.GetPathRoot(path) == path)
                {
                    throw new DirectoryNotFoundException($"The {nameof(containDir)} doesn't contain in any parent of {nameof(path)}");
                }
                return GetTargetParentDirectory(Directory.GetParent(path).FullName, containDir);
            }
        }
    }
}