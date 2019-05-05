using System;
using System.Linq;
using System.Text;


namespace LarchSys.FsAzureStorage {
    public struct CloudPath {
        // Path = '/some/segments/without/trailing/slash'
        public string Path {
            get {
                var sb = new StringBuilder();
                sb.Append('/');

                if (!string.IsNullOrEmpty(AccountName)) {
                    sb.Append(AccountName);

                    if (!string.IsNullOrEmpty(ContainerName)) {
                        sb.Append($"/{ContainerName}");
                        if (!string.IsNullOrEmpty(Prefix)) {
                            sb.Append($"/{Prefix}");
                        }
                    }
                }

                return sb.ToString();
            }
        }

        public string AccountName { get; }
        public string ContainerName { get; }
        public string Prefix { get; }
        public string BlobName => Prefix;

        /// <summary>
        /// '\'                 level 0
        /// '\segment'          level 1
        /// '\segment\segment'  level 2
        /// </summary>
        public int Level => Path.Length == 1 ? 0 : Path.Split('/').Skip(1).Count();

        //public bool IsAccountPath => !string.IsNullOrEmpty(AccountName);
        //public bool IsContainerPath => !string.IsNullOrEmpty(ContainerName);
        public bool IsBlobPath => Level > 2;
        public CloudPath Directory => System.IO.Path.GetDirectoryName(Path);

        /// <param name="path"> can contain backslashes and forward slashes </param>
        public CloudPath(string path)
        {
            if (string.IsNullOrEmpty(path)) {
                AccountName = string.Empty;
                ContainerName = string.Empty;
                Prefix = string.Empty;
                return;
            }

            if (path[0] != '\\' && path[0] != '/') {
                throw new NotSupportedException($"relative paths are not supported! path: '{path}'");
            }

            var parts = path.Substring(1).Split('\\', '/');
            AccountName = parts.First();
            ContainerName = parts.Skip(1).FirstOrDefault() ?? string.Empty;
            var prefix = string.Join("/", parts.Skip(2));
            if (prefix.EndsWith("/")) {
                prefix = prefix.Substring(0, prefix.Length - 1);
            }

            Prefix = prefix;
        }

        public string GetSegment(int level)
        {
            if (level == 0) {
                return null;
            }

            var segments = Path.Split('/');
            if (segments.Length > level) {
                return segments[level];
            }

            return null;
        }

        public static implicit operator string(CloudPath path)
        {
            return path.ToString();
        }

        public static implicit operator CloudPath(string path)
        {
            return new CloudPath(path);
        }

        public override string ToString()
        {
            return Path;
        }
    }
}
