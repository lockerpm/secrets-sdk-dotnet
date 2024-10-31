import os
import xmltodict
import subprocess

with open("Locker/Locker.csproj") as xml_file:
    data_dict = xmltodict.parse(xml_file.read())
    version = data_dict['Project']['PropertyGroup']['Version']
    
    subprocess.run(['nuget', 'setApiKey'], input=os.getenv('NUGET_API_KEY'), text=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

    os.system(f'nuget push Locker\\bin\\Release\\locker-secrets.{version}.nupkg')