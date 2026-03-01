#!/usr/bin/env bash

# If no argument is provided, run all tasks
RUN_ALL=false
if [ $# -eq 0 ]; then
	RUN_ALL=true
fi

#pushd ./EasyDotnet.IDE/

INPUTS_NETCOREDBGARMVERSION="3.1.2-1054"
INPUTS_NETCOREDBGVERSION="3.1.2-1054"
INPUTS_ROSLYNOUTPUTPATH="./EasyDotnet.IDE/bin/Release/net8.0/Tools"
INPUTS_ROSLYNPACKAGEID="Microsoft.CodeAnalysis.LanguageServer.neutral"
INPUTS_ROSLYNPACKAGEVERSION="5.4.0-2.26080.13"

OUTPUT_PATH="$INPUTS_ROSLYNOUTPUTPATH/Roslyn/Analyzers"

#
# Restore
#
if $RUN_ALL || [ "$1" = "restore" ]; then
  dotnet restore
fi

#
# Build
#
if $RUN_ALL || [ "$1" = "build" ]; then
  dotnet build -c Release --no-restore
fi

#
# Build and Copy Custom Analyzer
#
if $RUN_ALL || [ "$1" = "custom" ]; then
	mkdir -p "$OUTPUT_PATH"
	cp EasyDotnet.RoslynLanguageServices/bin/Release/net8.0/EasyDotnet.RoslynLanguageServices.dll "$OUTPUT_PATH/"
	echo "✓ Custom analyzer copied to $OUTPUT_PATH"
fi

#
# Download and Extract Roslyn DLLs
#
if $RUN_ALL || [ "$1" = "roslyn" ]; then
	ROSLYN_PACKAGE_ID=$INPUTS_ROSLYNPACKAGEID
	ROSLYN_PACKAGE_VERSION=$INPUTS_ROSLYNPACKAGEVERSION
	ROSLYNATOR_VERSION=4.14.0
	OUTPUT_PATH=$INPUTS_ROSLYNOUTPUTPATH/Roslyn

	mkdir -p "$OUTPUT_PATH/LanguageServer"
	mkdir -p "$OUTPUT_PATH/Analyzers"

	mkdir -p artifacts/roslyn
	curl -L -o artifacts/roslyn/roslyn.nupkg \
		"https://pkgs.dev.azure.com/azure-public/vside/_apis/packaging/feeds/vs-impl/nuget/packages/${ROSLYN_PACKAGE_ID}/versions/${ROSLYN_PACKAGE_VERSION}/content?api-version=6.0-preview.1"

	unzip -o artifacts/roslyn/roslyn.nupkg -d artifacts/roslyn/extracted
	cp -r artifacts/roslyn/extracted/content/LanguageServer/neutral/* "$OUTPUT_PATH/LanguageServer/"

	mkdir -p artifacts/roslynator
	curl -L -o artifacts/roslynator/roslynator.nupkg \
		"https://www.nuget.org/api/v2/package/Roslynator.Analyzers/${ROSLYNATOR_VERSION}"

	unzip -o artifacts/roslynator/roslynator.nupkg -d artifacts/roslynator/extracted
	cp artifacts/roslynator/extracted/analyzers/dotnet/roslyn4.7/cs/*.dll "$OUTPUT_PATH/Analyzers/"
fi

#
# Download and Extract netcoredbg
#
if $RUN_ALL || [ "$1" = "dbg" ]; then
	NETCOREDBG_VERSION=$INPUTS_NETCOREDBGVERSION
	NETCOREDBG_MACOS_ARM_VERSION=$INPUTS_NETCOREDBGARMVERSION

	OUTPUT_PATH="$INPUTS_ROSLYNOUTPUTPATH/netcoredbg"
	ARTIFACTS_DIR="artifacts/netcoredbg"

	mkdir -p "$ARTIFACTS_DIR"
	mkdir -p "$OUTPUT_PATH"

	declare -A platforms=(
		["linux-amd64"]="linux-x64"
		["linux-arm64"]="linux-arm64"
		["osx-amd64"]="osx-x64"
		["win64"]="win-x64"
	)

	# Download Samsung repo platforms
	for platform in "${!platforms[@]}"; do
		target_dir="${platforms[$platform]}"
		echo "Downloading netcoredbg for $platform..."
		
		if [[ "$platform" == "win64" ]]; then
			curl -L -o "$ARTIFACTS_DIR/netcoredbg-${platform}.zip" \
				"https://github.com/Samsung/netcoredbg/releases/download/${NETCOREDBG_VERSION}/netcoredbg-${platform}.zip"
			unzip -q "$ARTIFACTS_DIR/netcoredbg-${platform}.zip" -d "$ARTIFACTS_DIR/${platform}"
		else
			curl -L -o "$ARTIFACTS_DIR/netcoredbg-${platform}.tar.gz" \
				"https://github.com/Samsung/netcoredbg/releases/download/${NETCOREDBG_VERSION}/netcoredbg-${platform}.tar.gz"
			mkdir -p "$ARTIFACTS_DIR/${platform}"
			tar -xzf "$ARTIFACTS_DIR/netcoredbg-${platform}.tar.gz" -C "$ARTIFACTS_DIR/${platform}"
		fi
		
		mkdir -p "$OUTPUT_PATH/${target_dir}"
		cp -r "$ARTIFACTS_DIR/${platform}/netcoredbg/"* "$OUTPUT_PATH/${target_dir}/"
		echo "✓ Extracted netcoredbg to $OUTPUT_PATH/${target_dir}"
	done

	ARM_PLATFORM="osx-arm64"
	ARM_TARGET_DIR="osx-arm64"
	echo "Downloading netcoredbg (macOS ARM64) from Cliffback..."

	DOWNLOAD_URL="https://github.com/Cliffback/netcoredbg-macOS-arm64.nvim/releases/download/${NETCOREDBG_MACOS_ARM_VERSION}/netcoredbg-osx-arm64.tar.gz"
	DOWNLOAD_PATH="$ARTIFACTS_DIR/netcoredbg-${ARM_PLATFORM}.tar.gz"

	curl -sSL -o "$DOWNLOAD_PATH" "$DOWNLOAD_URL"
	mkdir -p "$ARTIFACTS_DIR/${ARM_PLATFORM}"

	if file "$DOWNLOAD_PATH" | grep -qi 'gzip compressed'; then
		tar -xzf "$DOWNLOAD_PATH" -C "$ARTIFACTS_DIR/${ARM_PLATFORM}"
		mkdir -p "$OUTPUT_PATH/${ARM_TARGET_DIR}"
		cp -r "$ARTIFACTS_DIR/${ARM_PLATFORM}/netcoredbg/"* "$OUTPUT_PATH/${ARM_TARGET_DIR}/"
		echo "✓ Extracted netcoredbg (macOS ARM64) to $OUTPUT_PATH/${ARM_TARGET_DIR}"
	else
		echo "::error::Downloaded netcoredbg-osx-arm64.tar.gz is not a valid gzipped tarball!"
		exit 1
	fi

	echo "All netcoredbg platforms bundled successfully!"
fi

#
# Pack
#
if $RUN_ALL || [ "$1" = "pack" ]; then
  dotnet pack ./EasyDotnet.IDE \
    -p:PackageVersion=2.9.9-pre2
fi


dotnet tool uninstall -g easydotnet
dotnet tool install -g --add-source ./EasyDotnet.IDE/bin/Release/ --prerelease easydotnet




exit
dotnet publish -f net8.0 -o artifacts
dotnet pack
popd
