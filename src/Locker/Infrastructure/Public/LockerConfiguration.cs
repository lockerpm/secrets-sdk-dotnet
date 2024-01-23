namespace Locker
{
    using System.Collections.Generic;
    using Infrastructure;
    using Newtonsoft.Json;
    using DotNetEnv;
    using System.IO;
    using System.Security.AccessControl;
    using System.Diagnostics;


    /// <summary>
    /// Global configuration class for Locker.net settings.
    /// </summary>
    public class LockerConfiguration
    {
        private string _apiBase;
        private string _accessKeyId;
        private string _secretAccessKey;
        private string _apiVersion;
        private string _sdkVersion;
        private string _lockerDir;
        private string _binaryFilePath;
        private string _binaryVersion;
        private Dictionary<string, string> _headers = new Dictionary<string, string>();

        private static LockerConfiguration instance;
        private static readonly object lockObject = new object();

        private LockerConfiguration()
        {
        }

        public static LockerConfiguration Instance
        {
            get
            {
                if (instance == null)
                {
                    lock (lockObject)
                    {
                        if (instance == null)
                        {
                            instance = new LockerConfiguration();
                        }
                    }
                }

                return instance;
            }
        }

        private void InitBinaryPath()
        {
            string homeDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
            _lockerDir = Path.Combine(homeDir, ".locker");
            _binaryVersion = "1.0.69";
            _binaryFilePath = Path.Combine(_lockerDir, $"locker_binary-{this._binaryVersion}");
        }

        private void DownloadBinaryFile()
        {
            string binaryUrl;
            var currentPlatform = System.Environment.OSVersion.Platform;
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform
                    .OSX))
            {
                if (System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture ==
                    System.Runtime.InteropServices.Architecture.Arm)
                {
                    binaryUrl = $"https://s.locker.io/download/locker-cli-mac-arm64-{_binaryVersion}";
                }
                else
                {
                    binaryUrl = $"https://s.locker.io/download/locker-cli-mac-x64-{_binaryVersion}";
                }
            }
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices
                         .OSPlatform.Windows))
            {
                binaryUrl = $"https://s.locker.io/download/locker-cli-win-x64-{_binaryVersion}.exe";

                _binaryFilePath = Path.Combine(_lockerDir, $"locker_binary-{_binaryVersion}.exe");
            }
            else
            {
                binaryUrl = $"https://s.locker.io/download/locker-cli-linux-x64-{_binaryVersion}";
            }

            // Check if the .locker directory exists, and create it if not
            if (!Directory.Exists(_lockerDir))
            {
                Directory.CreateDirectory(_lockerDir);
            }

            // Download binary file
            if (!File.Exists(_binaryFilePath))
            {
                using (var client = new System.Net.WebClient())
                {
                    Console.WriteLine($"saving to {Path.GetFullPath(_binaryFilePath)}");
                    client.DownloadFile(binaryUrl, _binaryFilePath);
                }

                try
                {
                    switch (currentPlatform)
                    {
                        case PlatformID.Win32S:
                        case PlatformID.Win32Windows:
                        case PlatformID.Win32NT:
                        case PlatformID.WinCE:
                        {
                            var fileInfo = new FileInfo(_binaryFilePath);
                            var security = new FileSecurity(
                                fileInfo.FullName,
                                AccessControlSections.Owner |
                                AccessControlSections.Group |
                                AccessControlSections.Access);
                            security.SetAccessRule(
                                new FileSystemAccessRule(
                                    "Everyone", FileSystemRights.ExecuteFile,
                                    AccessControlType.Allow
                                )
                            );
                            break;
                        }

                        case PlatformID.MacOSX:
                        case PlatformID.Unix:
                        {
                            using Process process = new Process();
                            process.StartInfo.FileName = "chmod";
                            process.StartInfo.Arguments =
                                $"755 {_binaryFilePath}";
                            process.Start();
                            process.WaitForExit();
                            break;
                        }
                    }
                }
                catch (UnauthorizedAccessException e)
                {
                    Console.WriteLine(e.Message);
                }
            }
        }


        public void Init(string apiBase = null, string accessKeyId = null, string secretAccessKey = null,
            string apiVersion = null,
            Dictionary<string, string> headers = null, string envPath = null)
        {
            _apiBase = apiBase;
            _accessKeyId = accessKeyId;
            _secretAccessKey = secretAccessKey;
            _apiVersion = apiVersion;
            _headers = headers;

            var assembly = typeof(LockerConfiguration).Assembly;
            _sdkVersion = assembly.GetName().Version.ToString();

            if (envPath != null)
            {
                DotNetEnv.Env.Load(envPath);
            }
            else
            {
                DotNetEnv.Env.Load(".env");
            }

            InitBinaryPath();
            DownloadBinaryFile();
        }


        /// <summary>Gets or sets the API base.</summary>
        /// <remarks>
        /// You can also set the API base using the <c>API_BASE</c> key in .env
        /// </remarks>
        public string ApiBase
        {
            get
            {
                if (string.IsNullOrEmpty(_apiBase))
                {
                    _apiBase = DotNetEnv.Env.GetString("API_BASE");
                }

                return _apiBase;
            }
            set => _apiBase = value;
        }

        /// <summary>Gets or sets the Access key id.</summary>
        /// <remarks>
        /// You can also set the Access key id using the <c>ACCESS_KEY_ID</c> key in .env file
        /// </remarks>
        public string AccessKeyId
        {
            get
            {
                if (string.IsNullOrEmpty(_accessKeyId))
                {
                    _accessKeyId = DotNetEnv.Env.GetString("ACCESS_KEY_ID");
                }

                return _accessKeyId;
            }
            set => _accessKeyId = value;
        }

        /// <summary>Gets or sets the Access key secret.</summary>
        /// <remarks>
        /// You can also set the Access key secret using the <c>ACCESS_KEY_SECRET</c> key in .env file
        /// </remarks>
        public string SecretAccessKey
        {
            get
            {
                if (string.IsNullOrEmpty(_secretAccessKey))
                {
                    _secretAccessKey = DotNetEnv.Env.GetString("SECRET_ACCESS_KEY");
                }

                return _secretAccessKey;
            }
            set => _secretAccessKey = value;
        }

        /// <summary>Gets or sets the API version.</summary>
        /// <remarks>
        /// You can also set the Api version using the <c>API_VERSION</c> key in .env file
        /// </remarks>
        public string ApiVersion
        {
            get
            {
                if (string.IsNullOrEmpty(_apiVersion))
                {
                    _apiVersion = DotNetEnv.Env.GetString("API_VERSION");
                }

                return _apiVersion;
            }
            set => _apiVersion = value;
        }

        public Dictionary<string, string> Headers
        {
            get
            {
                var cfAccessClientId = DotNetEnv.Env.GetString("CF_ACCESS_CLIENT_ID");
                var cfAccessClientSecret = DotNetEnv.Env.GetString("CF_ACCESS_CLIENT_SECRET");
                _headers = _headers == null || _headers.Count == 0
                    ? new Dictionary<string, string>()
                    {
                        { "CF-Access-Client-Id", cfAccessClientId },
                        { "CF-Access-Client-Secret", cfAccessClientSecret },
                    }
                    : _headers;
                return _headers;
            }

            set => _headers = value;
        }

        public string SdkVersion
        {
            get => this._sdkVersion;
            set => _sdkVersion = value;
        }

        public string LockerDir
        {
            get => _lockerDir;
        }

        public string BinaryFilePath
        {
            get => _binaryFilePath;
        }


        public static JsonSerializerSettings SerializerSettings { get; set; } = DefaultSerializerSettings();

        private static JsonSerializerSettings DefaultSerializerSettings()
        {
            return new JsonSerializerSettings
            {
                Converters = new List<JsonConverter>
                {
                    new LockerObjectConverter(),
                },
                DateParseHandling = DateParseHandling.None,
                MaxDepth = 128,
            };
        }
    }
}