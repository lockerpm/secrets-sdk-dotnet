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
dotnet add package Locker
```

Using the [NuGet Command Line Interface (CLI)][nuget-cli]:

```sh
nuget install Locker
```

Using the [Package Manager Console][package-manager-console]:

```powershell
Install-Package Locker
```

From within Visual Studio:

1. Open the Solution Explorer.
2. Right-click on a project within your solution.
3. Click on *Manage NuGet Packages...*
4. Click on the *Browse* tab and search for "Locker".
5. Click on the Locker package, select the appropriate version in the
   right-tab and click *Install*.

## Usages

### Set up access key

The SDK needs to be configured with your access key which is available in your Locker Secret Dashboard.
Initialize the `access_key_id` and `access_key_secret` to its value.
You also need to set `api_base` value (default is `https://secrets-core.locker.io`).

If you need to set your custom headers, you also need to set `headers` value in the `options` param:

```csharp
using Locker;


string accessKeyId = "your_access_key_id";
string accessKeySecret = "your_access_key_secret";
string apiBase = "your_api_base";
Dictionary<string, string> headers = new Dictionary<string, string>()
{
    { "CF-Access-Client-Id", "your_cf_access_client_id" },
    { "CF-Access-Client-Secret", "your_cf_access_client_secret" }
};
LockerConfiguration.Instance.Init(
    accessKeyId: accessKeyId,
    accessKeySecret: accessKeySecret,
    apiBase: apiBase,
    headers: headers
);

// setting by .env file
LockerConfiguration.Instance.Init(
    envPath: "your_env_file_path"
);
```

Now, you can use SDK to get or set values:

### List secrets

```csharp
var service = new SecretService();
var secrets = service.List();
```

### Get a secret value by secret key

```csharp

var service = new SecretService();

// Get a secret value by secret key.
// If they Key does not exist, SDK will return the defaultValue
var secretValue = service.Get(
    id: "REDIS_CONNECTION",
    defaultValue: "TheDefaultValue"
    )
Console.WriteLine(secretValue);

// Get a secret value by secret key and specific environment name.
// If the Key does not exist, SDK will return the defaultValue
secretValue = service.Get(
    id: "REDIS_CONNECTION",
    environmentName: "staging",
    defaultValue: "TheDefaultValue"
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
    id: "YOUR_SECRET_KEY",
    updateOptions:option
    );

// Update a secret value by secret key and a specific environment name
var updated_secret = service.Modify(
    id: "YOUR_SECRET_KEY",
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

var environment = Service.Get(id: "YOUR_ENV_NAME", options);
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
    id: "YOUR_ENV_NAME,
    updateOptions:opton
    );
```

## Examples

See the [examples' folder](/src/LockerExample).

## Development

Run all tests from the `src/LockerTests` directory:

```sh
dotnet test
```

Run some tests, filtering by name:

```sh
dotnet test --filter FullyQualifiedName~InvoiceServiceTest
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
