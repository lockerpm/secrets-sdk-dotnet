namespace Locker
{
    using System.Collections.Generic;
    using Locker.Infrastructure;
    using Newtonsoft.Json;
    using DotNetEnv;

    /// <summary>
    /// Global configuration class for Locker.net settings.
    /// </summary>
    public class LockerConfiguration
    {
        private string _apiBase;
        private string _accessKeyId;
        private string _accessKeySecret;
        private string _apiVersion;
        private string _sdkVersion;
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

        public void Init(string apiBase = null, string accessKeyId = null, string accessKeySecret = null,
            string apiVersion = null,
            Dictionary<string, string> headers = null, string envPath = null)
        {
            _apiBase = apiBase;
            _accessKeyId = accessKeyId;
            _accessKeySecret = accessKeySecret;
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
                    _apiBase = Env.GetString("API_BASE");
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
                    _accessKeyId = Env.GetString("ACCESS_KEY_ID");
                }

                return _accessKeyId;
            }
            set => _accessKeyId = value;
        }

        /// <summary>Gets or sets the Access key secret.</summary>
        /// <remarks>
        /// You can also set the Access key secret using the <c>ACCESS_KEY_SECRET</c> key in .env file
        /// </remarks>
        public string AccessKeySecret
        {
            get
            {
                if (string.IsNullOrEmpty(_accessKeySecret))
                {
                    _accessKeySecret = Env.GetString("ACCESS_KEY_SECRET");
                }

                return _accessKeySecret;
            }
            set => _accessKeySecret = value;
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
                    _apiVersion = Env.GetString("API_VERSION");
                }

                return _apiVersion;
            }
            set => _apiVersion = value;
        }

        public Dictionary<string, string> Headers
        {
            get
            {
                var cfAccessClientId = Env.GetString("CF_ACCESS_CLIENT_ID");
                var cfAccessClientSecret = Env.GetString("CF_ACCESS_CLIENT_SECRET");
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