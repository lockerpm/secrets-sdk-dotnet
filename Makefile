.PHONY: update-version codegen-format test ci-test
update-version:
	@echo "$(VERSION)" > VERSION
	@perl -pi -e 's|<Version>[.\-\d\w]+</Version>|<Version>$(VERSION)</Version>|' src/Locker/Locker.csproj

codegen-format:
	dotnet format src/Locker/Locker.csproj

ci-test:
	dotnet test src/LockerTests/LockerTests.csproj -c Debug

test:
	dotnet test -f net7.0 src/StripeTests/LockerTests.csproj -c Debug
