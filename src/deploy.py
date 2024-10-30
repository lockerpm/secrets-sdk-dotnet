import os
import xmltodict

with open("Locker/Locker.csproj") as xml_file:
    data_dict = xmltodict.parse(xml_file.read())
    version = data_dict['Project']['PropertyGroup']['Version']
    package_id = data_dict['Project']['PropertyGroup']['PackageId']
    os.system(f'nuget setApiKey {os.getenv("NUGET_API_KEY")}')
    os.system(f'nuget push Locker\\bin\\Release\\{package_id}.{version}.nupkg')


