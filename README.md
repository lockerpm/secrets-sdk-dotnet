# Locker Secret .NET SDK

<p align="center">
  <img src="https://cystack.net/images/logo-black.svg" alt="CyStack" width="50%"/>
</p>


---

The Locker Secret .Net SDK provides convenient access to the Locker Secret API from applications written in the
C# language. It includes a pre-defined set of classes for API resources that initialize themselves dynamically
from API responses which makes it compatible with a wide range of versions of the Locker Secret API.

## The Developer - CyStack

The Locker Secret .NET SDK is developed by CyStack, one of the leading cybersecurity companies in Vietnam.
CyStack is a member of Vietnam Information Security Association (VNISA) and Vietnam Association of CyberSecurity
Product Development. CyStack is a partner providing security solutions and services for many large domestic and
international enterprises.

CyStack’s research has been featured at the world’s top security events such as BlackHat USA (USA),
BlackHat Asia (Singapore), T2Fi (Finland), XCon - XFocus (China)... CyStack experts have been honored by global
corporations such as Microsoft, Dell, Deloitte, D-link...

## Documentation

The documentation will be updated later.

## Requirements

- .NET >  3.+

## Installation

Using the [.NET Core command-line interface (CLI) tools][dotnet-core-cli-tools]:

```sh
dotnet add package lockerpm
```

Using the [NuGet Command Line Interface (CLI)][nuget-cli]:

```sh
nuget install lockerpm
```

Using the [Package Manager Console][package-manager-console]:

```powershell
Install-Package lockerpm
```

From within Visual Studio:

1. Open the Solution Explorer.
2. Right-click on a project within your solution.
3. Click on *Manage NuGet Packages...*
4. Click on the *Browse* tab and search for "Locker.net".
5. Click on the Locker package, select the appropriate version in the
   right-tab and click *Install*.

## Usages

### Configuration access key

The SDK needs to be configured with your access key id and your secret access key, which is available in your Locker
Secret Dashboard. These keys must not be disclosed.These keys must not be disclosed. If you reveal these keys, you need
to revoke them immediately. Environment variables are a good solution and they are easy to consume in most programming
languages.

#### Set up credentials on Linux/MacOS

```shell
export ACCESS_KEY_ID=<YOUR_ACCESS_KEY_ID>
export SECRET_ACCESS_KEY=<YOUR_SECRET_ACCESS_KEY>
```

#### Set up credentials on Windows

Powershell

```shell
$Env:ACCESS_KEY_ID = '<YOUR_ACCESS_KEY_ID>'
$Env:SECRET_ACCESS_KEY = '<SECRET_ACCESS_KEY>'
```

Command Prompt

```shell
set ACCESS_KEY_ID=<YOUR_ACCESS_KEY_ID>
set SECRET_ACCESS_KEY=<YOUR_SECRET_ACCESS_KEY>
```

You also need to set `api_base` value (default is `https://api.locker.io/locker_secrets`).
If you need to set your custom headers, you also need to set `headers` value in the `options` param:

```csharp
using Locker;

string apiBase = "YOUR_API_BASE";
Dictionary<string, string> headers = new Dictionary<string, string>()
{
    { "CF-Access-Client-Id", "YOUR_CF_ACCESS_CLIENT_ID" },
    { "CF-Access-Client-Secret", "YOUR_CF_ACCESS_CLIENT_SECRET" }
};
LockerConfiguration.Instance.Init(
    apiBase: apiBase,
    headers: headers
);


```

You can also pass parameters in the Init() method or use the shared credential file (~/.locker/credentials), but we do
not recommend these ways.

```csharp
using Locker;

string accessKeyId = "YOUR_ACCESS_KEY_ID";
string secretAccessKey = "YOUR_SECRET_ACCESS_KEY";
string apiBase = "YOUR_API_BASE";
Dictionary<string, string> headers = new Dictionary<string, string>()
{
    { "CF-Access-Client-Id", "YOUR_CF_ACCESS_CLIENT_ID" },
    { "CF-Access-Client-Secret", "YOUR_CF_ACCESS_CLIENT_SECRET" }
};
LockerConfiguration.Instance.Init(
    accessKeyId: accessKeyId,
    secretAccessKey: secretAccessKey,
    apiBase: apiBase,
    headers: headers
);

// setting by .env file
LockerConfiguration.Instance.Init(
    envPath: "YOUR_ENV_FILE_PATH"
);
```

### Per-request configuration

All of the service methods accept an optional `RequestOptions` object. This is
used if you want to pass the access key, headers on each method, or you want set type of return value (default type is
string, if you want type of object use `IsJson=true`)

```c#
var requestOptions = new RequestOptions();
requestOptions.AccessKeyId = "ACCESS KEY ID";
requestOptions.SecretAccessKey = "SECRET ACCESS KEY";
requestOptions.ApiBase = "API BASE";
requestOptions.IsJson = true;
```

Now, you can use SDK to get or set values:

### List secrets

```csharp
var service = new SecretService();
var secrets = service.List();
```

### Get a secret value by secret key

```csharp
// Get a secret value by secret key.
// If they Key does not exist, SDK will return the defaultValue
var secretValue = service.GetSecret(
    name: "REDIS_CONNECTION",
    defaultValue: "Default Value"
    )
Console.WriteLine(secretValue);

// Get a secret value by secret key and specific environment name.
// If the Key does not exist, SDK will return the defaultValue
secretValue = service.GetSecret(
    name: "REDIS_CONNECTION",
    environmentName: "staging",
    defaultValue: "Default Value"
    )
Console.WriteLine(secretValue);

// Get a secret value by secret key.
// If they Key does not exist, SDK will throw exception
var options = new SecretRetrieveOptions();
var requestOptions = new RequestOptions();
var secretValue = service.Get(
    name: "REDIS_CONNECTION",
    retrieveOptions: options,
    requestOptions:requestOptions
    )
Console.WriteLine(secretValue);

// Get a secret value by secret key and specific environment name.
// If the Key does not exist, SDK will throw exception
var options = new SecretRetrieveOptions();
var requestOptions = new RequestOptions();
var secretValue = service.Get(
    name: "REDIS_CONNECTION",
    environmentName: "staging",
    retrieveOptions: options,
    requestOptions:requestOptions
    )
Console.WriteLine(secretValue);
```

### Create new secret

```csharp

var service = new SecretService();
var option = new SecretCreateOptions
   {
       Key = "YOUR_NEW_SECRET_KEY",
       Value = "YOUR_NEW_SECRET_VALUE",
   };
var newSecret = service.Create(option);
```

### Update secret

```csharp

var service = new SecretService();
var option = new SecretUpdateOptions
   {
       Key = "YOUR_UPDATE_SECRET_KEY",
       Value = "YOUR_UPDATED_SECRET_VALUE",
   };

// Update a secret value by secret key
var updated_secret = service.Modify(
    name: "YOUR_SECRET_KEY",
    updateOptions:option
    );

// Update a secret value by secret key and a specific environment name
var updated_secret = service.Modify(
    name: "YOUR_SECRET_KEY",
    environmentName: "YOUR_ENV_NAME",
    updateOptions:option
    );
```

### List environments

```csharp
var service = new EnvironmentService();
var environments = service.List();
```

### Get an environment object by name

```csharp

var service = new EnvironmentService();
var environment = service.Get(name: "YOUR_ENV_NAME");
Console.WriteLine(environment);
```

### Create new environment

```csharp

var service = new EnvironmentService();
var option = new EnvironmentCreateOptions()
   {
       Name = "YOUR_NEW_ENV_NAME",
       ExternalUrl = "YOUR_NEW_ENV_EXTERTAL_URL",
       Description = "YOUR_NEW_ENV_DESCRIPTION"
   };
var newEnv = service.Create(option);
```

### Update an environment by name

```csharp
var service = new EnvironmentService();
var option = new EnvironmentUpdateOptions()
   {
       Name = "YOUR_UPDATE_ENV_NAME",
       ExternalUrl = "YOUR_UPDATE_EXTERNAL_URL"
   };
var updatedEnv = service.Modify(
    name: "YOUR_ENV_NAME,
    updateOptions:opton
    );
```

### Error Handling

Locker Secret SDK offers some kinds of errors. They can reflect external events, like invalid credentials, network
interruptions, or code problems, like invalid API calls.

If an immediate problem prevents a function from continuing, the SDK raises an exception. It’s a best practice to catch
and handle exceptions. To catch an exception, use C Sharp’s `try/catch` syntax. Catch `LockerError` or its
subclasses to handle Locker-specific exceptions only. Each subclass represents a different kind of exception. When you
catch an exception, you can use its class to choose a response.

Example:

```csharp
using Locker;

LockerConfiguration.Instance.Init();
SecretService service = new SecretService();
var secretCreateOptions = new SecretCreateOptions
{
    Key = "your_secret_key",
    Value = "your_secret_value",
    Description = "your_secret_description",
    EnvironemntName = "your_secret_environment_name"
};
try
{
    Secret newSecret = (Secret)service.Create(secretCreateOptions);
    Console.WriteLine(newSecret);
}
catch (LockerError e)
{
    Console.WriteLine(e);
    throw;
}
```

In the SDK, error objects belong to LockerError and its subclasses. Use the documentation for each class
for advice about how to respond.

| Name                    | Class                 | Description                                                                                                                                        |
|-------------------------|-----------------------|----------------------------------------------------------------------------------------------------------------------------------------------------|
| Authentication Error    | AuthenticationError   | Invalid `access_client_id` or `invalid secret_access_key`                                                                                          |
| Permission Denied Error | PermissionDeniedError | Your credential does not have enough permission to execute this operation                                                                          |
| RateLimit Error         | RateLimitError        | Too many requests                                                                                                                                  |
| API Error               | APIError              | You made an API call with the wrong parameters, in the wrong state, or in an invalid way or Something went wrong on Locker’s end (These are rare.) |
| CLI Run Error           | CliRunError           | The encryption/decryption binary runs errors by invalid local data, process interruptions, or invalid `secret_access_key`                          |

## Examples

See the [examples' folder](/src/LockerExample).

## Development

Run all tests from the `src/LockerTests` directory:

```sh
dotnet test
```

Run some tests, filtering by name:

```sh
dotnet test --filter FullyQualifiedName~SecretServiceTest
```

The library uses [`dotnet-format`][dotnet-format] for code formatting. Code
must be formatted before PRs are submitted, otherwise CI will fail. Run the
formatter with:

```sh
dotnet format src/Locker.sln
```

## Reporting security issues

We take the security and our users' trust very seriously. If you found a security issue in Locker SDK .NET, please
report the issue by contacting us at <contact@locker.io>. Do not file an issue on the tracker.

## Contributing

Please check [CONTRIBUTING](CONTRIBUTING.md) before making a contribution.

## Help and media

- FAQ: https://support.locker.io

- Community Q&A: https://forum.locker.io

- News: https://locker.io/blog

## License
