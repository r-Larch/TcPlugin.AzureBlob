using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Core.Util;
using OY.TotalCommander.TcPluginBase.FileSystem;


// ReSharper disable LocalizableElement

namespace LarchSys.FsAzureStorage {
    internal delegate void FileProgress(string source, string destination, int percentDone);

    internal class BlobFileSystem {
        private List<StorageConnectionString> _accounts;
        internal Dictionary<string, CloudBlobClient> _clients;
        internal readonly PathCache _pathCache;


        public BlobFileSystem()
        {
            _accounts = new List<StorageConnectionString>();
            _clients = new Dictionary<string, CloudBlobClient>();
            _pathCache = new PathCache();
        }


        public IEnumerable<FindData> ListDirectory(CloudPath path)
        {
            switch (path.Level) {
                case 0:
                    return GetAccounts();
                case 1 when path.AccountName == "settings":
                    return GetSettings();
                case 1:
                    return GetContainers(path.AccountName);
                default:
                    return GetBlobSegments(path);
            }
        }

        private IEnumerable<FindData> GetSettings()
        {
            yield return new FindData("Connect to Azure");
            //yield return new FindData("Add Manually");
        }

        public void ProcessSettings(CloudPath path)
        {
            try {
                if (path.ContainerName == "Connect to Azure") {
                    var accounts = new AzureApiClient().GetStorageAccounts().Result;

                    _accounts.AddRange(accounts);
                    _accounts = _accounts.GroupBy(x => x.ToString()).Select(_ => _.First()).ToList();
                }
            }
            catch (Exception e) {
                MessageBox.Show(e.ToString(), "Error"); // TODO remove
            }
        }


        private IEnumerable<FindData> GetAccounts()
        {
            yield return new FindData("settings", FileAttributes.Directory);

            foreach (var account in _accounts) {
                yield return new FindData(account.AccountName, FileAttributes.Directory);
            }
        }


        private IEnumerable<FindData> GetContainers(string accountName)
        {
            var client = GetAccountClient(accountName);
            if (client == null) {
                return Enumerable.Empty<FindData>();
            }

            var list = client.ListContainers(prefix: null, ContainerListingDetails.Metadata);
            return list.Select(_ => new FindData(
                fileName: _.Name,
                fileSize: 0,
                attributes: FileAttributes.Directory,
                lastWriteTime: _.Properties.LastModified?.LocalDateTime
            ));
        }


        private IEnumerable<FindData> GetBlobSegments(CloudPath path)
        {
            var prefix = path.Prefix;

            if (prefix.Length > 0) {
                prefix += "/";
            }

            var container = GetAccountClient(path.AccountName)?.GetContainerReference(path.ContainerName);
            if (container == null) {
                return new FindData[0];
            }

            var list = container.ListBlobs(prefix, useFlatBlobListing: false, BlobListingDetails.Metadata)
                .Select(_ => {
                    switch (_) {
                        case CloudBlobDirectory dir: {
                            return new FindData(
                                fileName: dir.Prefix.Substring(prefix.Length).TrimEnd('/'),
                                fileSize: 0,
                                attributes: FileAttributes.Directory
                            );
                        }

                        case CloudBlockBlob block:
                            return new FindData(
                                fileName: block.Name.Substring(prefix.Length),
                                fileSize: (ulong) block.Properties.Length,
                                attributes: FileAttributes.Normal,
                                lastWriteTime: block.Properties.LastModified?.LocalDateTime,
                                creationTime: block.Properties.Created?.LocalDateTime,
                                lastAccessTime: block.Properties.BlobTierLastModifiedTime?.LocalDateTime
                            );
                        default:
                            throw new NotSupportedException($"Type: {_.GetType().FullName} not supported!");
                    }
                });

            return _pathCache
                .WithCached(path, list)
                .DefaultIfEmpty(new FindData("..", FileAttributes.Directory));
        }


        private CloudBlobClient GetAccountClient(string accountName)
        {
            if (_clients.TryGetValue(accountName, out var client)) {
                return client;
            }

            var conn = _accounts.FirstOrDefault(_ => _.AccountName == accountName);
            if (conn == null) {
                return null;
            }

            var account = CloudStorageAccount.Parse(conn.ConnectionString);
            return account.CreateCloudBlobClient();
        }

        private CloudBlockBlob GetBlockBlobReference(CloudPath blobFileName)
        {
            try {
                return GetAccountClient(blobFileName.AccountName)
                    ?.GetContainerReference(blobFileName.ContainerName)
                    ?.GetBlockBlobReference(blobFileName.BlobName);
            }
            catch {
                return null;
            }
        }


        public async Task<FileSystemExitCode> DownloadFile(CloudPath srcFileName, FileInfo dstFileName, bool overwrite, FileProgress fileProgress, bool deleteAfter = false, CancellationToken token = default)
        {
            var blob = GetBlockBlobReference(srcFileName);
            if (blob is null || !await blob.ExistsAsync(token)) {
                return FileSystemExitCode.FileNotFound;
            }

            var fileSize = blob.Properties.Length;

            Progress(new StorageProgress(0));

            void Progress(StorageProgress p)
            {
                var percent = fileSize == 0
                    ? 0
                    : decimal.ToInt32((p.BytesTransferred * 100) / (decimal) fileSize);

                fileProgress(srcFileName, dstFileName.FullName, percent);
            }

            var mode = overwrite ? FileMode.Create : FileMode.CreateNew;

            try {
                await blob.DownloadToFileAsync(dstFileName.FullName, mode, AccessCondition.GenerateIfExistsCondition(), null, null, new Progress<StorageProgress>(Progress), token);
                if (deleteAfter) {
                    await blob.DeleteAsync(token);
                }

                Progress(new StorageProgress(fileSize));

                return FileSystemExitCode.OK;
            }
            catch (TaskCanceledException) {
                return FileSystemExitCode.UserAbort;
            }
        }


        public async Task<FileSystemExitCode> UploadFile(FileInfo srcFileName, CloudPath dstFileName, bool overwrite, FileProgress fileProgress, CancellationToken token = default)
        {
            var blob = GetBlockBlobReference(dstFileName);
            if (blob == null) {
                return FileSystemExitCode.NotSupported;
            }

            if (!overwrite && await blob.ExistsAsync(token)) {
                return FileSystemExitCode.FileExists;
            }

            var fileSize = srcFileName.Length;

            Progress(new StorageProgress(0));

            void Progress(StorageProgress p)
            {
                var percent = fileSize == 0
                    ? 0
                    : decimal.ToInt32((p.BytesTransferred * 100) / (decimal) fileSize);

                fileProgress(dstFileName, srcFileName.FullName, percent);
            }

            var mode = overwrite ? AccessCondition.GenerateEmptyCondition() : AccessCondition.GenerateIfNotExistsCondition();

            try {
                await blob.UploadFromFileAsync(srcFileName.FullName, mode, null, null, new Progress<StorageProgress>(Progress), token);

                Progress(new StorageProgress(fileSize));

                return FileSystemExitCode.OK;
            }
            catch (TaskCanceledException) {
                return FileSystemExitCode.UserAbort;
            }
        }


        public async Task<FileSystemExitCode> Move(CloudPath sourceFileName, CloudPath destFileName, bool overwrite, CancellationToken token)
        {
            var source = GetBlockBlobReference(sourceFileName);
            var target = GetBlockBlobReference(destFileName);

            if (source is null || target is null) {
                return FileSystemExitCode.NotSupported;
            }

            if (!overwrite && await target.ExistsAsync(token)) {
                return FileSystemExitCode.FileExists;
            }

            var res = await CopyAndOverwrite(source, target, token);
            if (res != FileSystemExitCode.OK) {
                return res;
            }

            if (!await target.ExistsAsync(token)) {
                throw new Exception("Move failed because the target file wasn't created.");
            }

            await source.DeleteIfExistsAsync(token);
            return FileSystemExitCode.OK;
        }


        public async Task<FileSystemExitCode> Copy(CloudPath sourceFileName, CloudPath destFileName, bool overwrite, CancellationToken token)
        {
            var source = GetBlockBlobReference(sourceFileName);
            var target = GetBlockBlobReference(destFileName);

            if (source is null || target is null) {
                return FileSystemExitCode.NotSupported;
            }

            if (!overwrite && await target.ExistsAsync(token)) {
                return FileSystemExitCode.FileExists;
            }

            var res = await CopyAndOverwrite(source, target, token);
            if (res != FileSystemExitCode.OK) {
                return res;
            }

            if (!await target.ExistsAsync(token)) {
                throw new Exception("Move failed because the target file wasn't created.");
            }

            return FileSystemExitCode.OK;
        }


        public bool RemoveVirtualDir(CloudPath directory)
        {
            return _pathCache.Remove(directory); // allow removing virtual dirs
        }

        public async Task<bool> DeleteFile(CloudPath fileName)
        {
            var blob = GetBlockBlobReference(fileName);
            if (blob is null) {
                return false;
            }

            var success = RemoveVirtualDir(fileName);

            if (await blob.DeleteIfExistsAsync()) {
                // cache the directory to allow adding some files
                _pathCache.Add(fileName.Directory);
                return true;
            }

            return success;
        }


        //public bool RemoveDir(CloudPath dirName)
        //{
        //    // TODO implement (maybe where level > 2)

        //    //MessageBox.Show($"Delete {dirName} ?", "Delete directory?");

        //    //if (!dirName.IsBlobPath) {
        //    //    // don't let TC delete hole accounts and container
        //    //    return true; // return "yes it is deleted" so TC doesn't try to delete all one by one!!
        //    //}

        //    return false;
        //}


        private static async Task<FileSystemExitCode> CopyAndOverwrite(CloudBlockBlob src, CloudBlockBlob dst, CancellationToken token)
        {
            if (!await src.ExistsAsync(token)) {
                return FileSystemExitCode.FileNotFound;
            }

            var copyId = await dst.StartCopyAsync(source: src, token);
            while (dst.CopyState.Status == CopyStatus.Pending) {
                System.Threading.Thread.Sleep(100); // prevent endless loops: https://stackoverflow.com/questions/14152087/copying-one-azure-blob-to-another-blob-in-azure-storage-client-2-0#42255582
                await dst.FetchAttributesAsync(token);
            }

            if (dst.CopyState.Status != CopyStatus.Success) {
                throw new Exception("Copy Blob failed: " + dst.CopyState.Status);
            }

            return FileSystemExitCode.OK;
        }


        public bool CacheDirectory(CloudPath dir)
        {
            if (dir.IsBlobPath) {
                _pathCache.Add(dir);
                return true;
            }

            // can not create accounts and container
            return false;
        }
    }
}
