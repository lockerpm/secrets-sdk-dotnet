namespace Locker
{
    public class RequestOptions
    {
        public string AccessKey { get; set; }
        public string ApiBase { get; set; }
        public string ApiVersion { get; set; }
        public Dictionary<string, object> Headers { get; set; }
        public int Timeout { get; set; } = 30;

        internal RequestOptions Clone()
        {
            return (RequestOptions)this.MemberwiseClone();
        }
    }
}