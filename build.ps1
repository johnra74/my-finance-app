<#
.SYNOPSIS
    Build, test, run and package MyFinance on Windows 11.

.DESCRIPTION
    The Windows counterpart to build.sh. Windows is the only platform where the WPF app
    can actually run, so this script carries the two commands the Linux script cannot
    offer -- 'run' and 'publish' -- alongside the shared build and test gates.

    Compatible with the Windows PowerShell 5.1 that ships with Windows 11, and with
    PowerShell 7+. Deliberately avoids PS7-only syntax so it works out of the box.

.PARAMETER Task
    build    Compile the whole solution.
    test     Run every test suite.
    all      Build then test. The default, and the gate to run before committing.
    run      Launch the WPF application.
    docs     Build the documentation and pack it for embedding in the executable.
    publish  Produce a self-contained single-file .exe in artifacts\publish.
    clean    Remove all build output, including stray bin/obj directories.
    ef       Pass the remaining arguments to dotnet-ef, installing it if needed.

.PARAMETER Configuration
    Debug (default) or Release.

.EXAMPLE
    .\build.ps1
    Build and test everything.

.EXAMPLE
    .\build.ps1 run
    Launch the app to check a change by eye.

.EXAMPLE
    .\build.ps1 publish
    Produce a distributable single-file executable.

.EXAMPLE
    .\build.ps1 ef migrations add AddSomething --project src\MyFinance.Data --output-dir Migrations
    Scaffold a new database migration.
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('build', 'test', 'all', 'run', 'docs', 'publish', 'clean', 'ef')]
    [string] $Task = 'all',

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',

    # Everything after the task, forwarded verbatim to dotnet-ef.
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $Rest
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

$Solution = 'MyFinance.slnx'
$AppProject = 'src\MyFinance.App\MyFinance.App.csproj'
$DataProject = 'src\MyFinance.Data\MyFinance.Data.csproj'
$RequiredSdkMajor = 10
$EfToolVersion = '10.0.0'

Push-Location $PSScriptRoot
try {

    function Write-Step {
        param([string] $Message)
        Write-Host ''
        Write-Host "==> $Message" -ForegroundColor Cyan
    }

    function Write-Note {
        param([string] $Message)
        Write-Host "    $Message" -ForegroundColor DarkGray
    }

    # dotnet reports failures through its exit code rather than by throwing, so every
    # invocation is checked. Without this a failing test run would look like a success.
    function Invoke-Dotnet {
        param([Parameter(ValueFromRemainingArguments = $true)][string[]] $Arguments)

        Write-Note "dotnet $($Arguments -join ' ')"
        & dotnet @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
        }
    }

    function Assert-DotnetSdk {
        $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
        if (-not $dotnet) {
            throw @"
The .NET SDK is not installed, or dotnet is not on PATH.

Install the .NET $RequiredSdkMajor SDK, then reopen your terminal:

    winget install Microsoft.DotNet.SDK.$RequiredSdkMajor

or download it from https://dotnet.microsoft.com/download
"@
        }

        $sdks = & dotnet --list-sdks
        $matching = @($sdks | Where-Object { $_ -match "^$RequiredSdkMajor\." })

        if ($matching.Count -eq 0) {
            $installed = ($sdks | ForEach-Object { ($_ -split ' ')[0] }) -join ', '
            throw @"
.NET $RequiredSdkMajor SDK not found. Installed SDKs: $installed

The projects target net$RequiredSdkMajor.0, so an older SDK cannot build them. Install it with:

    winget install Microsoft.DotNet.SDK.$RequiredSdkMajor
"@
        }

        Write-Note "Using SDK $((($matching[-1]) -split ' ')[0])"
    }

    function Assert-EfTool {
        # dotnet-ef is a separate tool, not part of the SDK. Install it on first use so a
        # fresh clone can scaffold migrations without a documented extra step.
        $listing = & dotnet tool list --global
        $installed = @($listing | Select-String -Pattern '^dotnet-ef\s').Count -gt 0

        if (-not $installed) {
            Write-Step "Installing dotnet-ef $EfToolVersion (one time)"
            Invoke-Dotnet tool install --global dotnet-ef --version $EfToolVersion
            Write-Note 'If dotnet-ef is not found below, reopen your terminal so PATH picks up %USERPROFILE%\.dotnet\tools.'
        }
    }

    function Invoke-Build {
        Write-Step "Building ($Configuration)"
        Invoke-Dotnet build $Solution --configuration $Configuration
    }

    function Invoke-Test {
        Write-Step "Testing ($Configuration)"
        Invoke-Dotnet test $Solution --configuration $Configuration
    }

    function Invoke-Run {
        Write-Step 'Launching MyFinance'
        Write-Note 'Close the window to return to this prompt.'
        Invoke-Dotnet run --project $AppProject --configuration $Configuration
    }

    <#
        Builds the documentation and packs it for embedding.

        Optional on purpose. A machine without Python still builds and tests the whole
        solution; Help then reports that this build shipped without its pages, exactly as
        the application behaves when the embedding model is absent. -W matches
        .readthedocs.yaml: a broken cross-reference must not ship inside the executable.

        Returns $true when there is a help.zip to embed.
    #>
    function Invoke-Docs {
        Write-Step 'Building the documentation'

        $python = Get-Command python -ErrorAction SilentlyContinue
        if (-not $python) {
            Write-Note 'Python is not installed, so the documentation was not built.'
            return $false
        }

        # Tested on the interpreter rather than the folder: a half-created or partly-cleaned
        # environment leaves the folder there, and testing for the folder would skip the
        # repair and fail on the next step instead.
        $venv = Join-Path $PSScriptRoot '.venv-docs'
        if (-not (Test-Path (Join-Path $venv 'Scripts\pip.exe'))) {
            & $python.Source -m venv --clear $venv
        }

        $pip = Join-Path $venv 'Scripts\pip.exe'
        $sphinx = Join-Path $venv 'Scripts\sphinx-build.exe'

        & $pip install -q -r (Join-Path $PSScriptRoot 'docs\requirements.txt')

        $html = Join-Path $PSScriptRoot 'docs\_build\html'
        if (Test-Path $html) { Remove-Item -LiteralPath $html -Recurse -Force }

        & $sphinx -W -q -b html (Join-Path $PSScriptRoot 'docs') $html
        if ($LASTEXITCODE -ne 0) {
            throw 'The documentation has warnings, which are errors. Fix them before publishing.'
        }

        # Sphinx's own doctree cache and the theme's source maps are most of the size and
        # none of the use.
        $doctrees = Join-Path $html '.doctrees'
        if (Test-Path $doctrees) { Remove-Item -LiteralPath $doctrees -Recurse -Force }
        Get-ChildItem -Path $html -Recurse -Filter '*.map' | Remove-Item -Force

        $help = Join-Path $PSScriptRoot 'src\MyFinance.App\Assets\help.zip'
        if (Test-Path $help) { Remove-Item -LiteralPath $help -Force }

        Compress-Archive -Path (Join-Path $html '*') -DestinationPath $help

        $sizeKb = [math]::Round((Get-Item $help).Length / 1KB)
        Write-Host "Documentation: $help ($sizeKb KB)" -ForegroundColor Green
        return $true
    }

    function Invoke-Publish {
        $rid = if ($Rest) { $Rest[0] } else { 'win-x64' }
        $output = Join-Path $PSScriptRoot "artifacts\publish\$rid"

        if (Test-Path $output) {
            Remove-Item -LiteralPath $output -Recurse -Force
        }

        # Before the build, so the pages end up inside the single file. A failure is a
        # warning rather than a stop: a release without documentation is worse than one
        # with, and much better than no release at all.
        if (-not (Invoke-Docs)) {
            Write-Note 'Publishing without embedded documentation.'
        }

        Write-Step 'Publishing self-contained single-file build'

        # The -p: arguments are quoted, and must stay quoted. Unquoted, PowerShell reads
        # `-p:Name=Value` as its own `-parameter:value` syntax rather than as a string to
        # hand to dotnet: it binds `-p` to the common parameter `-PipelineVariable` and,
        # with four of them, fails before dotnet is ever invoked --
        #
        #     Cannot bind parameter because parameter 'p' is specified more than once.
        #
        # Quoting makes each one an argument again. Double-dash arguments such as
        # --configuration are unaffected; only a single dash is read as a parameter name.
        #
        # Self-contained so the machine needs no .NET runtime installed.
        # IncludeNativeLibrariesForSelfExtract matters here specifically: SQLCipher ships as
        # a native library, and without it the single file would launch and then fail the
        # moment it tried to open a book.
        # Not trimmed -- WPF relies on reflection that the trimmer cannot see.
        Invoke-Dotnet publish $AppProject `
            --configuration Release `
            --runtime $rid `
            --self-contained true `
            --output $output `
            '-p:PublishSingleFile=true' `
            '-p:IncludeNativeLibrariesForSelfExtract=true' `
            '-p:EnableCompressionInSingleFile=true' `
            '-p:DebugType=embedded'

        # Beside the executable as well as inside it. The embedded copies are what satisfy
        # the notice requirements when somebody has only the .exe; these are what a person
        # reads without launching anything, and what a package maintainer expects to find.
        Copy-Item -Path (Join-Path $PSScriptRoot 'LICENSE'), (Join-Path $PSScriptRoot 'NOTICE') `
            -Destination $output -Force

        $exe = Join-Path $output 'MyFinance.exe'
        if (Test-Path $exe) {
            $sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)

            # One file is the whole application, so a zip of the folder is the release.
            $version = ([xml](Get-Content (Join-Path $PSScriptRoot 'Directory.Build.props'))).Project.PropertyGroup.Version
            $zip = Join-Path $PSScriptRoot "artifacts\MyFinance-$version-$rid.zip"

            if (Test-Path $zip) { Remove-Item -LiteralPath $zip -Force }
            Compress-Archive -Path (Join-Path $output '*') -DestinationPath $zip

            Write-Host ''
            Write-Host "Published: $exe ($sizeMb MB)" -ForegroundColor Green
            Write-Host "Archive:   $zip" -ForegroundColor Green
        }
    }

    function Invoke-Clean {
        Write-Step 'Cleaning'
        Invoke-Dotnet clean $Solution --configuration $Configuration

        # dotnet clean leaves bin/obj behind, and stale obj folders are a common cause of
        # confusing restore errors after a branch switch.
        $stale = @(Get-ChildItem -Path 'src', 'tests' -Recurse -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -eq 'bin' -or $_.Name -eq 'obj' })

        foreach ($directory in $stale) {
            Write-Note "Removing $($directory.FullName)"
            Remove-Item -LiteralPath $directory.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }

        foreach ($path in @('artifacts', 'docs\_build', 'src\MyFinance.App\Assets\help.zip')) {
            if (Test-Path $path) {
                Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }

    function Invoke-Ef {
        Assert-EfTool

        $arguments = @('ef')
        if ($Rest) {
            $arguments += $Rest
        }
        else {
            # Bare './build.ps1 ef' is most usefully a listing rather than a usage error.
            $arguments += @('migrations', 'list', '--project', $DataProject)
        }

        Write-Step 'Entity Framework tools'
        Invoke-Dotnet @arguments
    }

    Assert-DotnetSdk

    switch ($Task) {
        'build'   { Invoke-Build }
        'test'    { Invoke-Test }
        'all'     { Invoke-Build; Invoke-Test }
        'run'     { Invoke-Run }
        'docs'    { [void](Invoke-Docs) }
        'publish' { Invoke-Publish }
        'clean'   { Invoke-Clean }
        'ef'      { Invoke-Ef }
    }

    Write-Host ''
    Write-Host "Done: $Task" -ForegroundColor Green
    exit 0
}
catch {
    Write-Host ''
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
finally {
    Pop-Location
}
