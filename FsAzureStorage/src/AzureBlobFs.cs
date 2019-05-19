using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using FsAzureStorage.Forms;
using FsAzureStorage.Resources;
using TcPluginBase;
using TcPluginBase.Content;
using TcPluginBase.FileSystem;


namespace FsAzureStorage {
    public class AzureBlobFs : FsPlugin {
        private readonly Settings _pluginSettings;
        private readonly BlobFileSystem _fs;

        public AzureBlobFs(Settings pluginSettings) : base(pluginSettings)
        {
            _pluginSettings = pluginSettings;
            Title = "Azure Blob Plugin";

            _fs = new BlobFileSystem();

            TcPluginEventHandler += (sender, args) => {
                switch (args) {
                    case RequestEventArgs x:
                        Log.Info($"Event: {args.GetType().Name}: CustomTitle: {x.CustomTitle}");
                        break;
                    case ProgressEventArgs x:
                        Log.Info($"Event: {args.GetType().Name}: PercentDone: {x.PercentDone}");
                        break;
                    case ContentProgressEventArgs x:
                        Log.Info($"Event: {args.GetType().Name}: NextBlockData: {x.NextBlockData}");
                        break;
                    default:
                        Log.Info($"Event: {args.GetType().FullName}");
                        break;
                }
            };

            // to debug!
            //AppDomain.CurrentDomain.FirstChanceException += (sender, eventArgs) => {
            //    Log.Error($"FirstChanceException: " + eventArgs.Exception.ToString());
            //};
        }


        #region IFsPlugin Members

        public override IEnumerable<FindData> GetFiles(RemotePath path)
        {
            return _fs.ListDirectory(path);
        }


        public override bool MkDir(RemotePath dir)
        {
            return _fs.CacheDirectory(dir);
        }


        public override bool RemoveDir(RemotePath dirName)
        {
            _fs.RemoveVirtualDir(dirName);

            // TC should delete files one by one!
            // this reduces chances of deleting whole containers within a second.
            return false;
        }

        public override bool DeleteFile(RemotePath fileName)
        {
            return _fs.DeleteFile(fileName).Result;
        }


        public override FileSystemExitCode RenMovFile(RemotePath oldName, RemotePath newName, bool move, bool overwrite, RemoteInfo remoteInfo)
        {
            ProgressProc(oldName, newName, 0);
            try {
                if (move) {
                    return _fs.Move(oldName, newName, overwrite: overwrite, default).Result;
                }
                else {
                    return _fs.Copy(oldName, newName, overwrite: overwrite, default).Result;
                }
            }
            finally {
                ProgressProc(oldName, newName, 100);
            }
        }


        public override async Task<FileSystemExitCode> PutFileAsync(string localName, RemotePath remoteName, CopyFlags copyFlags, Action<int> setProgress, CancellationToken token)
        {
            var overWrite = (CopyFlags.Overwrite & copyFlags) != 0;
            var performMove = (CopyFlags.Move & copyFlags) != 0;
            var resume = (CopyFlags.Resume & copyFlags) != 0;

            if (resume) {
                return FileSystemExitCode.NotSupported;
            }

            if (!File.Exists(localName)) {
                return FileSystemExitCode.FileNotFound;
            }

            var prevPercent = -1;
            var ret = await _fs.UploadFile(new FileInfo(localName), remoteName, overwrite: overWrite,
                fileProgress: (source, destination, percent) => {
                    if (percent != prevPercent) {
                        prevPercent = percent;

                        setProgress(percent);
                    }
                },
                token: token
            );

            if (performMove && ret == FileSystemExitCode.OK) {
                File.Delete(localName);
            }

            return ret;
        }


        public override async Task<FileSystemExitCode> GetFileAsync(RemotePath remoteName, string localName, CopyFlags copyFlags, RemoteInfo remoteInfo, Action<int> setProgress, CancellationToken token)
        {
            Log.Warning($"GetFile({remoteName}, {localName}, {copyFlags})");

            var overWrite = (CopyFlags.Overwrite & copyFlags) != 0;
            var performMove = (CopyFlags.Move & copyFlags) != 0;
            var resume = (CopyFlags.Resume & copyFlags) != 0;

            if (resume) {
                return FileSystemExitCode.NotSupported;
            }

            if (File.Exists(localName) && !overWrite) {
                return FileSystemExitCode.FileExists;
            }

            var prevPercent = -1;
            return await _fs.DownloadFile(
                srcFileName: remoteName,
                dstFileName: new FileInfo(localName),
                overwrite: overWrite,
                fileProgress: (source, destination, percent) => {
                    if (percent != prevPercent) {
                        prevPercent = percent;

                        setProgress(percent);
                    }
                },
                deleteAfter: performMove,
                token
            );
        }


        public override ExtractIconResult ExtractCustomIcon(RemotePath remoteName, ExtractIconFlags extractFlags)
        {
            var path = new CloudPath(remoteName);

            if (path.Path.EndsWith("..")) {
                return ExtractIconResult.UseDefault;
            }

            if (path.Level == 1) {
                switch (path) {
                    case "/settings":
                        return ExtractIconResult.Extracted(Icons.settings_icon);

                    default:
                        // accounts
                        return ExtractIconResult.Extracted(Icons.storage_account);
                }
            }

            if (path.Level == 2) {
                return ExtractIconResult.Extracted(Icons.container_icon);
            }

            return ExtractIconResult.UseDefault;
        }


        public override void StatusInfo(string remoteDir, InfoStartEnd startEnd, InfoOperation infoOperation)
        {
            base.StatusInfo(remoteDir, startEnd, infoOperation);
        }


        public override ExecResult ExecuteCommand(TcWindow mainWin, RemotePath remoteName, string command)
        {
            switch (command) {
                case "refresh":
                    mainWin.Refresh();
                    return ExecResult.Ok;

                case "show cache": {
                    var sb = new StringBuilder();
                    sb.AppendLine("Cache:");
                    _fs._pathCache.Paths.Aggregate(sb, (s, path) => s.AppendLine($"  {path}"));
                    MessageBox.Show(sb.ToString(), "All cached paths");
                    return ExecResult.Ok;
                }

                case "clear cache":
                    _fs._pathCache.Paths.Clear();
                    MessageBox.Show("Cache cleared", "Cache", MessageBoxButton.OK, MessageBoxImage.Information);
                    return ExecResult.Ok;

                case "show settings": {
                    var sb = new StringBuilder();
                    sb.AppendLine("Settings:");
                    _pluginSettings.Aggregate(sb, (s, pair) => s.AppendLine($"  {pair.Key}: \t {pair.Value}"));
                    MessageBox.Show(sb.ToString(), "Plugin Settings");
                    return ExecResult.Ok;
                }

                case "cd ..":
                    return ExecResult.Yourself;
                default:
                    Log.Info($"{nameof(ExecuteCommand)}(\"{mainWin.Handle}\", \"{remoteName}\", \"{command}\")");
                    //throw new NotImplementedException($"{nameof(ExecuteCommand)}(\"{mainWin.Handle}\", \"{remoteName}\", \"{command}\")");
                    break;
            }


            if (string.IsNullOrEmpty(command)) {
                return ExecResult.Yourself;
            }

            var cmdPars = command.Split(' ', '\\');

            if (cmdPars[0] == "req") {
                /*
                req UserName testValue
                req Password testValue
                req Account testValue
                req TargetDir testValue
                req url testValue
                req Other testValue
                 */

                var customTitle = "Request Callback Test";
                var preValue = cmdPars.Length > 2 ? cmdPars[2] : null;

                var res = string.Empty;

                if (cmdPars.Length > 1) {
                    switch (cmdPars[1]) {
                        case "UserName":
                            res = Prompt.AskUserName(customTitle, preValue);
                            break;

                        case "Password":
                            res = Prompt.AskPassword(customTitle, preValue);
                            break;

                        case "Account":
                            res = Prompt.AskAccount(customTitle, preValue);
                            break;

                        case "TargetDir":
                            res = Prompt.AskTargetDir(customTitle, preValue);
                            break;

                        case "url":
                            res = Prompt.AskUrl(customTitle, preValue);
                            break;

                        case "Other":
                        default:
                            var customText = "Input value:";
                            res = Prompt.AskOther(customTitle, customText, preValue);
                            break;
                    }
                }

                if (res != null) {
                    Prompt.MsgOk($"Request for '{cmdPars[1]}' returned:", res);
                }

                return ExecResult.Ok;
            }
            else if (cmdPars[0] == "crypt") {
                /*
                crypt Save MyConn passw0rd
                crypt Load MyConn
                crypt LoadNoUI MyConn
                crypt Copy MyConn dstConn
                crypt Move MyConn dstConn
                crypt Delete MyConn
                 */

                var func = cmdPars.Length > 1 ? cmdPars[1] : null;
                var connectionName = cmdPars.Length > 2 ? cmdPars[2] : "Test Connection";
                var password = cmdPars.Length > 3 ? cmdPars[3] : null;

                var cryptRes = CryptResult.PasswordNotFound;

                switch (func) {
                    case "Save":
                        cryptRes = Password.Save(connectionName, password);
                        break;
                    case "Load":
                        cryptRes = Password.Load(connectionName, ref password);
                        break;
                    case "LoadNoUI":
                        cryptRes = Password.LoadNoUI(connectionName, ref password);
                        break;
                    case "Copy":
                        cryptRes = Password.Copy(connectionName, password);
                        break;
                    case "Move":
                        cryptRes = Password.Move(connectionName, password);
                        break;
                    case "Delete":
                        cryptRes = Password.Delete(connectionName);
                        break;
                }

                Prompt.MsgOk(null, $"Crypt for '{func}' returned '{cryptRes}'\r({password})");
                return ExecResult.Ok;
            }

            return ExecResult.Yourself;
        }


        public override ExecResult ExecuteProperties(TcWindow mainWin, RemotePath remoteName)
        {
            return ExecResult.Yourself;
        }

        public override ExecResult ExecuteOpen(TcWindow mainWin, RemotePath remoteName)
        {
            CloudPath path = remoteName;

            switch (path.Level) {
                case 2 when path.AccountName == "settings":
                    var window = new SettingsWindow("Azure Storage Account");
                    window.ShowDialog();
                    //ProcessSettings(path);
                    return ExecResult.Ok;
            }

            return ExecResult.Yourself;
        }


        private void ProcessSettings(CloudPath path)
        {
            if (path.ContainerName == "Connect to Azure") {
                var connection = GetConnection("\\" + path.AccountName);
                connection.WriteStatus("Connecting to Azure");
                connection.WriteStatus("Opening sign on Screen");
                connection.WriteStatus("Please Wait..");
                var accounts = new AzureApiClient().GetStorageAccounts(connection.WriteStatus).Result;
                _fs.AddAccounts(accounts);
                connection.WriteStatus("Successful");
            }
        }


        //public override PreviewBitmapResult GetPreviewBitmap(RemotePath remoteName, int width, int height)
        //{
        //    return PreviewBitmapResult.None;
        //}

        //public override bool SetAttr(RemotePath remoteName, FileAttributes attr)
        //{
        //    return false;
        //}

        //public override bool SetTime(RemotePath remoteName, DateTime? creationTime, DateTime? lastAccessTime, DateTime? lastWriteTime)
        //{
        //    return false;
        //}

        #endregion IFsPlugin Members
    }
}
