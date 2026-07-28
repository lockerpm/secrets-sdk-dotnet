.PHONY: format check test build pack

format:
	dotnet format src/Locker.sln

check:
	dotnet format src/Locker.sln --verify-no-changes

test:
	dotnet test src/LockerTests/LockerTests.csproj --configuration Release

build:
	dotnet build src/Locker.sln --configuration Release

pack:
	dotnet pack src/Locker/Locker.csproj --configuration Release --no-build --output artifacts
