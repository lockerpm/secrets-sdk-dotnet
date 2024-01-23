import os
import xmltodict

with open("Locker/Locker.csproj") as xml_file:
    data_dict = xmltodict.parse(xml_file.read())
    version = data_dict['Project']['PropertyGroup']['Version']
    os.system(f'nuget push Locker\\bin\\Release\\locker-secrets.{version}.nupkg')
