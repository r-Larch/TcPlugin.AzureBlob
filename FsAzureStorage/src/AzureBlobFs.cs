using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using FsAzureStorage.Resources;
using TcPluginBase;
using TcPluginBase.Content;
using TcPluginBase.FileSystem;


namespace LarchSys.FsAzureStorage {
    public class AzureBlobFs : FsPlugin {
        private readonly Settings _pluginSettings;
        private readonly BlobFileSystem _fs;

        public AzureBlobFs(Settings pluginSettings) : base(pluginSettings)
        {
            _pluginSettings = pluginSettings;
            BackgroundFlags = FsBackgroundFlags.Download | FsBackgroundFlags.Upload /*| FsBackgroundFlags.AskUser*/;
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
        }

        ~AzureBlobFs()
        {
            Log.Warning("~AzureBlobFs() is called.");
        }


        #region IFsPlugin Members

        public override object FindFirst(string path, out FindData findData)
        {
            var enumerator = _fs.ListDirectory(path).GetEnumerator();

            if (enumerator.MoveNext()) {
                findData = enumerator.Current;
                return enumerator;
            }

            // empty list
            findData = null;
            return null;
        }

        public override bool FindNext(ref object o, out FindData findData)
        {
            if (o is IEnumerator<FindData> fsEnum) {
                if (fsEnum.MoveNext()) {
                    var current = fsEnum.Current;
                    if (current != null) {
                        findData = current;
                        return true;
                    }
                }
            }

            // end of sequence
            findData = null;
            return false;
        }


        public override bool MkDir(string dir)
        {
            return _fs.CacheDirectory(dir);
        }


        public override bool RemoveDir(string dirName)
        {
            _fs.RemoveVirtualDir(dirName);

            // TC should delete files one by one!
            // this reduces chances of deleting whole containers within a second.
            return false;
        }

        public override bool DeleteFile(string fileName)
        {
            return _fs.DeleteFile(fileName).Result;
        }


        public override FileSystemExitCode RenMovFile(string oldName, string newName, bool move, bool overwrite, RemoteInfo remoteInfo)
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


        public override FileSystemExitCode GetFile(string remoteName, ref string localName, CopyFlags copyFlags, RemoteInfo remoteInfo)
        {
            var loclName = localName;
            Log.Warning($"GetFile({remoteName}, {loclName}, {copyFlags})");

            var overWrite = (CopyFlags.Overwrite & copyFlags) != 0;
            var performMove = (CopyFlags.Move & copyFlags) != 0;
            var resume = (CopyFlags.Resume & copyFlags) != 0;

            if (resume) {
                return FileSystemExitCode.NotSupported;
            }

            if (File.Exists(localName) && !overWrite) {
                return FileSystemExitCode.FileExists;
            }

            // My ThreadKeeper class is needed hire because calls to ProgressProc must be made from this thread and not from some random async one.
            try {
                using (var exec = new ThreadKeeper()) {
                    var prevPercent = -1;

                    var ret = exec.ExecAsync(
                        asyncFunc: (token) => _fs.DownloadFile(
                            srcFileName: remoteName,
                            dstFileName: new FileInfo(loclName),
                            overwrite: overWrite,
                            fileProgress: (source, destination, percent) => {
                                if (percent != prevPercent) {
                                    prevPercent = percent;

                                    exec.RunInMainThread(() => {
                                        if (ProgressProc(source, destination, percent) == 1) {
                                            exec.Cancel();
                                        }
                                    });
                                }
                            },
                            deleteAfter: performMove,
                            token
                        )
                    );

                    return ret;
                }
            }
            catch (OperationCanceledException) {
                return FileSystemExitCode.UserAbort;
            }
        }


        public override FileSystemExitCode PutFile(string localName, ref string remoteName, CopyFlags copyFlags)
        {
            var rmtName = remoteName;

            var overWrite = (CopyFlags.Overwrite & copyFlags) != 0;
            var performMove = (CopyFlags.Move & copyFlags) != 0;
            var resume = (CopyFlags.Resume & copyFlags) != 0;

            if (resume) {
                return FileSystemExitCode.NotSupported;
            }

            if (!File.Exists(localName)) {
                return FileSystemExitCode.FileNotFound;
            }

            // My ThreadKeeper class is needed here because calls to ProgressProc must be made from this thread and not from some random async one.
            try {
                using (var exec = new ThreadKeeper()) {
                    var prevPercent = -1;

                    var ret = exec.ExecAsync(
                        asyncFunc: (token) => _fs.UploadFile(new FileInfo(localName), rmtName, overwrite: overWrite,
                            fileProgress: (source, destination, percent) => {
                                if (percent != prevPercent) {
                                    prevPercent = percent;

                                    exec.RunInMainThread(() => {
                                        if (ProgressProc(source, destination, percent) == 1) {
                                            exec.Cancel();
                                        }
                                    });
                                }
                            },
                            token: token
                        )
                    );

                    if (performMove && ret == FileSystemExitCode.OK) {
                        File.Delete(localName);
                    }

                    return ret;
                }
            }
            catch (OperationCanceledException) {
                return FileSystemExitCode.UserAbort;
            }
        }


        public override ExtractIconResult ExtractCustomIcon(ref string remoteName, ExtractIconFlags extractFlags, out Icon icon)
        {
            var path = new CloudPath(remoteName);

            if (path.Path.EndsWith("..")) {
                icon = null;
                return ExtractIconResult.UseDefault;
            }

            if (path.Level == 1) {
                switch (path) {
                    case "/settings":
                        icon = Icons.settings_icon;
                        return ExtractIconResult.Extracted;

                    default:
                        // accounts
                        icon = Icons.storage_account;
                        return ExtractIconResult.Extracted;
                }
            }

            if (path.Level == 2) {
                icon = Icons.container_icon;
                return ExtractIconResult.Extracted;
            }

            icon = null;
            return ExtractIconResult.UseDefault;
        }


        public override void StatusInfo(string remoteDir, InfoStartEnd startEnd, InfoOperation infoOperation)
        {
            base.StatusInfo(remoteDir, startEnd, infoOperation);
        }


        public override bool Disconnect(string disconnectRoot)
        {
            bool canDisconnect = false;
            // private bool canDisconnect;
            // private string currentConnection;
            // private List<string> ftpConnections = new List<string>();
            //
            // TODO what to do??
            if (canDisconnect) {
                var msg = $"Do you really want to disconnect from \"{disconnectRoot}\"";
                var s = "Yes";
                if (RequestProc(RequestType.MsgYesNo, "Disconnect?", msg, ref s, 45)) {
                    canDisconnect = false;
                    LogProc(LogMsgType.Details, $"Trying to disconnect {disconnectRoot} ...");
                    //LogProc(LogMsgType.Disconnect, null);
                    // TODO disconnect
                    return true;
                }

                MessageBox.Show("Sorry, we are not able to disconnect " + disconnectRoot, "Can not disconnect", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            return false;
        }

        public override ExecResult ExecuteCommand(TcWindow mainWin, ref string remoteName, string command)
        {
            switch (command) {
                case "refresh":
                    mainWin.Refresh();
                    return ExecResult.OK;

                case "show cache": {
                    var sb = new StringBuilder();
                    sb.AppendLine("Cache:");
                    _fs._pathCache.Paths.Aggregate(sb, (s, path) => s.AppendLine($"  {path}"));
                    MessageBox.Show(sb.ToString(), "All cached paths");
                    return ExecResult.OK;
                }

                case "clear cache":
                    _fs._pathCache.Paths.Clear();
                    MessageBox.Show("Cache cleared", "Cache", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return ExecResult.OK;

                case "show settings": {
                    var sb = new StringBuilder();
                    sb.AppendLine("Settings:");
                    _pluginSettings.Aggregate(sb, (s, pair) => s.AppendLine($"  {pair.Key}: \t {pair.Value}"));
                    MessageBox.Show(sb.ToString(), "Plugin Settings");
                    return ExecResult.OK;
                }

                case "cd ..":
                    return ExecResult.Yourself;
                default:
                    Log.Info($"{nameof(ExecuteCommand)}(\"{mainWin.Handle}\", \"{remoteName}\", \"{command}\")");
                    throw new NotImplementedException($"{nameof(ExecuteCommand)}(\"{mainWin.Handle}\", \"{remoteName}\", \"{command}\")");
            }

            //return ExecResult.Yourself;

            //ExecResult result = ExecResult.Yourself;
            //if (String.IsNullOrEmpty(command))
            //    return result;
            //string[] cmdPars = command.Split(new[] {' ', '\\'});
            //if (cmdPars[0].Equals("log", StringComparison.InvariantCultureIgnoreCase)) {
            //    string logString = ((cmdPars.Length < 3 || String.IsNullOrEmpty(cmdPars[2])) ? null : cmdPars[2]);
            //    if (cmdPars.Length > 1) {
            //        canDisconnect = true;
            //        if (cmdPars[1].Equals("connect", StringComparison.InvariantCultureIgnoreCase)) {
            //            //msgType = LogMsgType.Connect;
            //            currentConnection = logString == null ? Title : logString;
            //            LogProc(LogMsgType.Connect, "CONNECT \\" + currentConnection);
            //            ftpConnections.Add(currentConnection);
            //            LogProc(LogMsgType.Details, String.Format("Connection to {0} established.", currentConnection));
            //        }
            //        else if (cmdPars[1].Equals("disconnect", StringComparison.InvariantCultureIgnoreCase)) {
            //            LogProc(LogMsgType.Disconnect, "Disconnect from: " + logString);
            //        }
            //        else if (cmdPars[1].Equals("details", StringComparison.InvariantCultureIgnoreCase)) {
            //            LogProc(LogMsgType.Details, logString);
            //        }
            //        else if (cmdPars[1].Equals("trComplete", StringComparison.InvariantCultureIgnoreCase)) {
            //            LogProc(LogMsgType.TransferComplete, "Transfer complete: \\" + remoteName + " -> " + logString);
            //        }
            //        else if (cmdPars[1].Equals("error", StringComparison.InvariantCultureIgnoreCase)) {
            //            LogProc(LogMsgType.ImportantError, logString);
            //        }
            //        else if (cmdPars[1].Equals("opComplete", StringComparison.InvariantCultureIgnoreCase)) {
            //            LogProc(LogMsgType.OperationComplete, logString);
            //        }

            //        result = ExecResult.OK;
            //    }

            //    //currentConnection = "\\" + ((cmdPars.Length < 2 || String.IsNullOrEmpty(cmdPars[1])) ? Title : cmdPars[1]);
            //    //LogProc(LogMsgType.Connect, "CONNECT " + currentConnection);
            //    //if (cmdPars.Length > 2)
            //    //    LogProc(LogMsgType.Details,
            //    //        String.Format("Connection to {0} established with {1}.", currentConnection, cmdPars[2]));
            //    //result = ExecResult.OK;
            //}
            //else if (cmdPars[0].Equals("req", StringComparison.InvariantCultureIgnoreCase)) {
            //    RequestType requestType = RequestType.Other;
            //    if (cmdPars.Length > 1) {
            //        if (cmdPars[1].Equals("UserName", StringComparison.InvariantCultureIgnoreCase))
            //            requestType = RequestType.UserName;
            //        else if (cmdPars[1].Equals("Password", StringComparison.InvariantCultureIgnoreCase))
            //            requestType = RequestType.Password;
            //        else if (cmdPars[1].Equals("Account", StringComparison.InvariantCultureIgnoreCase))
            //            requestType = RequestType.Account;
            //        else if (cmdPars[1].Equals("TargetDir", StringComparison.InvariantCultureIgnoreCase))
            //            requestType = RequestType.TargetDir;
            //        else if (cmdPars[1].Equals("url", StringComparison.InvariantCultureIgnoreCase))
            //            requestType = RequestType.Url;
            //        else if (cmdPars[1].Equals("DomainInfo", StringComparison.InvariantCultureIgnoreCase))
            //            requestType = RequestType.DomainInfo;
            //    }

            //    string customText = (requestType == RequestType.Other) ? "Input value:" : null;
            //    string testValue = (cmdPars.Length > 2) ? cmdPars[2] : null;
            //    if (RequestProc(requestType, "Request Callback Test", customText, ref testValue, 2048)) {
            //        MessageBox.Show(testValue, String.Format("Request for '{0}' returned:", requestType.ToString()),
            //            MessageBoxButtons.OK, MessageBoxIcon.Information);
            //        result = ExecResult.OK;
            //    }
            //}
            //else if (cmdPars[0].Equals("crypt", StringComparison.InvariantCultureIgnoreCase)) {
            //    string connectionName = "LFS Test Connection";
            //    if (cmdPars.Length > 2)
            //        connectionName = cmdPars[2];
            //    string password = "qwerty";
            //    if (cmdPars.Length > 3)
            //        password = cmdPars[3];
            //    CryptResult cryptRes = CryptResult.PasswordNotFound;
            //    if (cmdPars.Length > 1) {
            //        if (cmdPars[1].Equals("Save", StringComparison.InvariantCultureIgnoreCase))
            //            cryptRes = Password.Save(connectionName, password);
            //        else if (cmdPars[1].Equals("Load", StringComparison.InvariantCultureIgnoreCase))
            //            cryptRes = Password.Load(connectionName, ref password);
            //        else if (cmdPars[1].Equals("LoadNoUi", StringComparison.InvariantCultureIgnoreCase))
            //            cryptRes = Password.LoadNoUI(connectionName, ref password);
            //        else if (cmdPars[1].Equals("Copy", StringComparison.InvariantCultureIgnoreCase))
            //            cryptRes = Password.Copy(connectionName, password);
            //        else if (cmdPars[1].Equals("Move", StringComparison.InvariantCultureIgnoreCase))
            //            cryptRes = Password.Move(connectionName, password);
            //        else if (cmdPars[1].Equals("Delete", StringComparison.InvariantCultureIgnoreCase))
            //            cryptRes = Password.Delete(connectionName);
            //    }

            //    string s = String.Format("Crypt for '{0}' returned '{1}'", cmdPars[1], cryptRes.ToString());
            //    if (cryptRes == CryptResult.OK)
            //        s += " (" + password + ")";
            //    string testValue = null;
            //    RequestProc(RequestType.MsgYesNo, null, s, ref testValue, 0);
            //    result = ExecResult.OK;
            //}

            //return result;
        }

        public override ExecResult ExecuteProperties(TcWindow mainWin, string remoteName)
        {
            return ExecResult.Yourself;
        }

        public override ExecResult ExecuteOpen(TcWindow mainWin, ref string remoteName)
        {
            CloudPath path = remoteName;

            switch (path.Level) {
                case 2 when path.AccountName == "settings":
                    _fs.ProcessSettings(path);
                    MessageBox.Show("Ok Accounts loaded!", "Success!"); // TODO improve
                    return ExecResult.OK;
            }


            //if (Password.Save("store", "passw0rd") == CryptResult.OK) {
            //}

            return ExecResult.Yourself;
        }


        //public override PreviewBitmapResult GetPreviewBitmap(ref string remoteName, int width, int height, out Bitmap returnedBitmap)
        //{
        //    returnedBitmap = null;
        //    return PreviewBitmapResult.None;
        //}

        //public override bool SetAttr(string remoteName, FileAttributes attr)
        //{
        //    throw new PluginNotImplementedException();
        //}

        //public override bool SetTime(string remoteName, DateTime? creationTime, DateTime? lastAccessTime, DateTime? lastWriteTime)
        //{
        //    throw new PluginNotImplementedException();
        //}

        #endregion IFsPlugin Members
    }
}
