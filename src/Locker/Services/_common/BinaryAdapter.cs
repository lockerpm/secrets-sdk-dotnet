using System.Text;

namespace Locker
{
    using System.Diagnostics;
    using System.Security.AccessControl;
    using System.Security.Principal;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;


    public class BinaryAdapter
    {
        private string _accessKeyId;
        private string _secretAccessKey;
        private string _apiBase;
        private bool _isJson;
        private string _apiVersion;
        private Dictionary<string, string> _headers;
        private PlatformID _systemPlatform;
        static string EscapeArgument(string argument)
        {
            if (string.IsNullOrEmpty(argument))
                return string.Empty;

            return argument.Replace("\"", "\\\"").Replace(";", "").Replace("&", "").Replace("|", "");
        }
        public BinaryAdapter(string accessKeyId = null, string secretAccessKey = null, string apiBase = null,
            string apiVersion = null, bool isJson = false,
            Dictionary<string, string> headers = null)
        {
            LockerConfiguration config = LockerConfiguration.Instance;
            this._accessKeyId = accessKeyId;
            this._secretAccessKey = secretAccessKey;
            this._isJson = isJson;
            this._apiBase = apiBase ?? config.ApiBase;
            this._apiVersion = apiVersion ?? config.ApiVersion;
            this._headers = headers ?? config.Headers;
            this._systemPlatform = this.GetPlatform();
        }

        private void MakeExecutable(string path)
        {
            FileInfo fileInfo = new FileInfo(path);
            if (this._systemPlatform == PlatformID.Win32NT)
            {
                var security = new FileSecurity(fileInfo.FullName,
                    AccessControlSections.Owner |
                    AccessControlSections.Group |
                    AccessControlSections.Access);
                security.SetAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                    FileSystemRights.ExecuteFile, AccessControlType.Allow));
            }
            else if (this._systemPlatform == PlatformID.MacOSX || this._systemPlatform == PlatformID.Unix)
            {
                using Process process = new Process();
                process.StartInfo.FileName = "chmod";
                process.StartInfo.Arguments = $"755 {path}"; // Replace with the desired permissions (e.g. 755)
                process.Start();
                process.WaitForExit();
            }
        }

        private PlatformID GetPlatform()
        {
            return System.Environment.OSVersion.Platform;
        }

        private string? GetBinaryFile()
        {
            return LockerConfiguration.Instance.BinaryFilePath;
        }

        public string Call(string cli, int timeout = 30,
            BaseOptions options = null)
        {
            string raw = "";
            string binaryFile = this.GetBinaryFile();
            if (binaryFile != null)
            {
                this.MakeExecutable(binaryFile);
            }

            string myAccessKeyId = this._accessKeyId ?? LockerConfiguration.Instance.AccessKeyId;
            string mySecretAccessKey = this._secretAccessKey ?? LockerConfiguration.Instance.SecretAccessKey;

            string defaultUserAgent = $"CSharp - {LockerConfiguration.Instance.SdkVersion}";
            var argumentBuilder = new StringBuilder();

// Safe argument concatenation with escaping
            argumentBuilder.AppendFormat("{0} ", cli);
            argumentBuilder.AppendFormat("--access-key-id \"{0}\" ", EscapeArgument(myAccessKeyId));
            argumentBuilder.AppendFormat("--secret-access-key \"{0}\" ", EscapeArgument(mySecretAccessKey));
            argumentBuilder.AppendFormat("--agent \"{0}\" ", EscapeArgument(defaultUserAgent));

            if (!String.IsNullOrEmpty(_apiBase))
            {
                argumentBuilder.AppendFormat("--api-base \"{0}\" ", EscapeArgument(_apiBase));
            }

            if (this._isJson)
            {
                argumentBuilder.Append("--output-format json ");
            }


            var headers = this._headers ?? LockerConfiguration.Instance.Headers;
            
            List<string> headerList = new List<string>();
            foreach (var pair in headers)
            {
                headerList.Add($"{pair.Key}:{pair.Value}");
            }

            string headerStr = String.Join(",", headerList);
            argumentBuilder.AppendFormat("--headers \"{0}\" ", EscapeArgument(headerStr));

            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = binaryFile;
            startInfo.Arguments = argumentBuilder.ToString().TrimEnd();

            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.UseShellExecute = false;

            Process process = new Process();
            process.StartInfo = startInfo;

            // Start the process
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                raw = output;
            }
            else
            {
                List<string> signs = new List<string>()
                    { "\"success\": false", "\"success\": true", "\"object\": \"error\"" };
                bool isContainSign = false;
                foreach (var sign in signs)
                {
                    if (output.Contains(sign))
                    {
                        raw = output;
                        isContainSign = true;
                        break;
                    }
                }

                if (!isContainSign)
                {
                    var exc = new CliRunError(output, process);
                    throw exc;
                }
            }


            return this.InterpretResponse(raw);
        }

        private string InterpretResponse(string responseBody)
        {
            try
            {
                object responseObj = null;
                responseObj = JsonConvert.DeserializeObject(responseBody);
                JContainer? jsonObject = (JContainer)responseObj;


                if (ShouldHandleAsError(jsonObject))
                {
                    jsonObject["object"] = "error";

                    HandleErrorResponse((JObject)jsonObject);
                }
            }
            catch (JsonReaderException ex)
            {
                if (this._isJson)
                {
                    var exc = new CliRunError($"CLI JSONDecodeError:::{responseBody}");
                    throw exc;
                }

                string objectValue = ExtractValue(responseBody, "object");

                if (objectValue != null)
                {
                    string errorValue = ExtractValue(responseBody, "error");
                    string messageValue = ExtractValue(responseBody, "message");
                    var jObject = new JObject();
                    jObject["object"] = objectValue;
                    jObject["error"] = errorValue;
                    jObject["message"] = messageValue;
                    HandleErrorResponse(jObject);
                }
            }

            return responseBody;
        }

        static string? ExtractValue(string json, string key)
        {
            int startIndex = json.IndexOf($"\"{key}\"");
            if (startIndex >= 0)
            {
                startIndex = json.IndexOf(':', startIndex);
                if (startIndex >= 0)
                {
                    int endIndex = json.IndexOf("\n", startIndex);
                    if (endIndex >= 0)
                    {
                        string value = json.Substring(startIndex + 3, endIndex - (startIndex + 4)).Trim();
                        value = value.Replace("\"", "");
                        return value;
                    }
                }
            }

            return null;
        }

        private bool ShouldHandleAsError(JContainer? responseObj)
        {
            JObject checkObj = null;
            try
            {
                checkObj = (JObject)responseObj;
            }
            catch (Exception e)
            {
                return false;
            }

            if (checkObj == null)
            {
                return false;
            }

            try
            {
                string objectStr = checkObj.TryGetValue("object", out JToken? objToken)
                    ? checkObj["object"].ToObject<string>()
                    : "";
                string successStr = checkObj.TryGetValue("success", out JToken? successToken)
                    ? checkObj["success"].ToObject<string>()
                    : "";
                bool successBool = checkObj.TryGetValue("success", out JToken? token)
                    ? checkObj["success"].ToObject<bool>()
                    : true;
                return objectStr == "error" || successBool == false ||
                       successStr == "false";
            }
            catch (ArgumentNullException ex)
            {
                return false;
            }
        }

        private void HandleErrorResponse(JObject responseBody)
        {
            var exc = SpecificCliError(responseBody);
            throw exc;
        }

        private Exception SpecificCliError(JObject errorData)
        {
            errorData.TryGetValue("status_code", out var statusCode);
            errorData.TryGetValue("error", out var errorCode);
            errorData.TryGetValue("message", out var message);
            int statusCodeInt = (int)(statusCode ?? -1);
            string errorCodeStr = (string)(errorCode ?? "_");
            string messageStr = (string)(message ?? "");
            if (statusCodeInt == 429 || errorCodeStr == "rate_limit")
            {
                return new RateLimitError(message: messageStr, httpBody: JsonConvert.SerializeObject(errorData),
                    httpStatus: 429, errorCode: "rate_limit");
            }

            if (statusCodeInt == 403 || errorCodeStr == "permission_denied")
            {
                return new PermissionDeniedError(message: messageStr,
                    httpBody: JsonConvert.SerializeObject(errorData),
                    httpStatus: 403, errorCode: "permission_denied");
            }

            if (statusCodeInt == 401 || errorCodeStr == "unauthorized" ||
                errorCode == (JToken?)"invalid_secret_access_key")
            {
                return new AuthenticationError(message: messageStr,
                    httpBody: JsonConvert.SerializeObject(errorData),
                    httpStatus: 401, errorCode: errorCodeStr);
            }

            if (statusCodeInt == 404 || errorCodeStr == "not_found")
            {
                return new ResourceNotFoundError(message: messageStr,
                    httpBody: JsonConvert.SerializeObject(errorData),
                    httpStatus: 404, errorCode: "permission_denied");
            }


            if (statusCodeInt >= 500 || errorCodeStr == "server_error")
            {
                return new APIServerError(message: messageStr,
                    httpBody: JsonConvert.SerializeObject(errorData),
                    httpStatus: statusCodeInt, errorCode: errorCodeStr);
            }

            if (errorCodeStr == "http_error")
            {
                return new APIConnectionError(message: messageStr,
                    httpBody: JsonConvert.SerializeObject(errorData));
            }


            return new APIError(message: messageStr, httpBody: JsonConvert.SerializeObject(errorData));
        }
    }
}