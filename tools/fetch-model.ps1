<#
.SYNOPSIS
    Fetches the sentence-embedding model used for category suggestions.

.DESCRIPTION
    The Windows counterpart to tools/fetch-model.sh, and deliberately the same tool: the
    same two files, the same pinned hashes, the same wording on every line it prints, so a
    transcript from one platform can be compared with a transcript from the other.

    The weights are ~23 MB and are deliberately NOT committed: they are a build input,
    fetched once and verified against a pinned hash. A model that changed underneath us
    would not fail loudly -- it would quietly start making different suggestions -- so the
    hash is the point of this script, not the download.

    Compatible with the Windows PowerShell 5.1 that ships with Windows 11, and with
    PowerShell 7+.

.EXAMPLE
    .\tools\fetch-model.ps1
    Fetch anything missing or stale, and verify what is already there.
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Invoke-WebRequest on Windows PowerShell 5.1 renders a progress bar per chunk, which on a
# 23 MB download costs far more time than the download itself.
$ProgressPreference = 'SilentlyContinue'

# 5.1 defaults to TLS 1.0 on some Windows builds, and Hugging Face refuses that outright.
# Additive rather than assigned, so nothing already enabled is turned off.
[Net.ServicePointManager]::SecurityProtocol =
    [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    # Forward slashes: PowerShell and the .NET path APIs accept them on Windows exactly as
    # they do backslashes, and it means this script can be smoke-tested on the Linux box the
    # rest of the build already runs on.
    $dest = 'src/MyFinance.Semantics/Assets'
    $repo = 'https://huggingface.co/Xenova/all-MiniLM-L6-v2/resolve/main'

    $files = @(
        [pscustomobject]@{
            Name   = 'model_quantized.onnx'
            Remote = 'onnx/model_quantized.onnx'
            Sha256 = 'afdb6f1a0e45b715d0bb9b11772f032c399babd23bfc31fed1c170afc848bdb1'
        },
        [pscustomobject]@{
            Name   = 'vocab.txt'
            Remote = 'vocab.txt'
            Sha256 = '07eced375cec144d27c900241f3e339478dec958f92fddbc551f295c992038a3'
        }
    )

    New-Item -ItemType Directory -Force -Path $dest | Out-Null

    function Get-Sha256 {
        param([string] $Path)

        # Lower case to match sha256sum, so the two scripts print the same digest for the
        # same file and a hash can be pasted from one into the other.
        (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
    }

    foreach ($file in $files) {
        $target = Join-Path $dest $file.Name

        if (Test-Path -LiteralPath $target) {
            if ((Get-Sha256 $target) -eq $file.Sha256) {
                Write-Host "ok       $($file.Name)"
                continue
            }

            Write-Host "stale    $($file.Name) (re-fetching)"
        }

        Write-Host "fetching $($file.Name) ..."

        $partial = "$target.partial"
        Invoke-WebRequest -Uri "$repo/$($file.Remote)" -OutFile $partial -UseBasicParsing

        $have = Get-Sha256 $partial

        # A hash written as HASH_... is a placeholder for a file being added before its
        # digest is known: the download is kept and the digest printed to be pasted in.
        if (-not $file.Sha256.StartsWith('HASH_') -and $have -ne $file.Sha256) {
            Remove-Item -LiteralPath $partial -Force

            # Straight to stderr rather than Write-Error, which under an ErrorActionPreference
            # of Stop renders a boxed multi-line exception with source context. This is one
            # clean line, the same line fetch-model.sh prints.
            [Console]::Error.WriteLine("FAILED   $($file.Name): expected $($file.Sha256), got $have")
            exit 1
        }

        Move-Item -LiteralPath $partial -Destination $target -Force
        Write-Host "ok       $($file.Name)  sha256=$have"
    }
}
finally {
    Pop-Location
}
