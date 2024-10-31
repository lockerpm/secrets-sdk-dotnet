import os
import xmltodict
import subprocess

with open("Locker/Locker.csproj") as xml_file:
    data_dict = xmltodict.parse(xml_file.read())
    version = data_dict['Project']['PropertyGroup']['Version']
    package_id = data_dict['Project']['PropertyGroup']['PackageId']

    subprocess.run(['nuget', 'setApiKey'], input=os.getenv('NUGET_API_KEY'), text=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

    os.system(f'nuget push Locker\\bin\\Release\\{package_id}.{version}.nupkg')