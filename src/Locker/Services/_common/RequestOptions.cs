namespace Locker;

public class RequestOptions
{
    public string? AccessKeyId { get; set; }
    public string? SecretAccessKey { get; set; }
    public string? ApiBase { get; set; }
    public string? ApiVersion { get; set; }
    public string? CliPath { get; set; }
    public Dictionary<string, object>? Headers { get; set; }
    public int Timeout { get; set; } = 30;
    public bool IsJson { get; set; } = true;

    internal RequestOptions Clone() => (RequestOptions)MemberwiseClone();
}
