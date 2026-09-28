SHELL := /bin/bash

.PHONY: help restore build test format format-check changesets changeset pack push clean

SOLUTION := NinjaVault.slnx
CONFIGURATION ?= Release
PACKAGES_DIR := artifacts/packages
NUGET_SOURCE ?= https://api.nuget.org/v3/index.json
# Optional: limit pack/push/changesets to one package, e.g. make pack PACKAGE=NinjaVault.Cdn
PACKAGE ?=

help:
	@echo "NinjaVault commands:"
	@echo "  make build                         - Restore + build (warnings are errors)"
	@echo "  make test                          - Build + run all tests"
	@echo "  make format                        - Apply dotnet format"
	@echo "  make format-check                  - Verify formatting without changing files"
	@echo "  make changesets                    - Check every package version has changesets/<Id>/<Version>.md"
	@echo "  make changeset PACKAGE=<Id> [TYPE=Patch|Minor|Major]"
	@echo "                                     - Create the change-set for <Id>'s current <Version>"
	@echo "  make pack [PACKAGE=<Id>]           - Test, check change-sets, then pack .nupkg/.snupkg into $(PACKAGES_DIR)/"
	@echo "  make push [PACKAGE=<Id>]           - Push packed packages to nuget.org (needs NUGET_API_KEY)"
	@echo "  make clean                         - Remove bin/, obj/ and artifacts/"
	@echo ""
	@echo "Release flow: bump <Version> in src/<Id>/<Id>.csproj -> make changeset PACKAGE=<Id> -> fill it in"
	@echo "              -> make pack -> upload $(PACKAGES_DIR)/*.nupkg on nuget.org (or make push)"

restore:
	dotnet restore $(SOLUTION)

build: restore
	dotnet build $(SOLUTION) -c $(CONFIGURATION) -warnaserror --no-restore

test: build
	dotnet test $(SOLUTION) -c $(CONFIGURATION) --no-build

format:
	dotnet format $(SOLUTION)

format-check:
	dotnet format $(SOLUTION) --verify-no-changes

changesets:
	@bash scripts/check-changesets.sh $(PACKAGE)

changeset:
	@if [ -z "$(PACKAGE)" ]; then echo "Usage: make changeset PACKAGE=NinjaVault.Cdn [TYPE=Patch]"; exit 1; fi
	@bash scripts/new-changeset.sh $(PACKAGE) $(or $(TYPE),Patch)

pack: test changesets
	@rm -rf $(PACKAGES_DIR)
	@if [ -n "$(PACKAGE)" ]; then \
	    dotnet pack src/$(PACKAGE)/$(PACKAGE).csproj -c $(CONFIGURATION) --no-build -p:ContinuousIntegrationBuild=true; \
	else \
	    for project in src/*/*.csproj; do \
	        dotnet pack "$$project" -c $(CONFIGURATION) --no-build -p:ContinuousIntegrationBuild=true || exit 1; \
	    done; \
	fi
	@echo ""
	@echo "Packages ready in $(PACKAGES_DIR)/:"
	@ls -1 $(PACKAGES_DIR)

push:
	@if [ -z "$$NUGET_API_KEY" ]; then echo "Set NUGET_API_KEY first (nuget.org > API Keys, scoped to NinjaVault.*)."; exit 1; fi
	@if [ ! -d "$(PACKAGES_DIR)" ]; then echo "Nothing to push - run make pack first."; exit 1; fi
	@# Http first: NinjaVault.Cdn depends on it. --skip-duplicate makes re-running safe.
	@for id in NinjaVault.Http NinjaVault.Cdn; do \
	    if [ -n "$(PACKAGE)" ] && [ "$(PACKAGE)" != "$$id" ]; then continue; fi; \
	    for pkg in $(PACKAGES_DIR)/$$id.[0-9]*.nupkg; do \
	        [ -e "$$pkg" ] || continue; \
	        dotnet nuget push "$$pkg" --api-key "$$NUGET_API_KEY" --source $(NUGET_SOURCE) --skip-duplicate || exit 1; \
	    done; \
	done

clean:
	@rm -rf artifacts
	@find . -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
