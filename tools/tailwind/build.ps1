<#
.SYNOPSIS
    Builds SpeiseDirekt3\wwwroot\css\tailwind.css (the back office's Tailwind CSS) with the standalone Tailwind CLI.
.DESCRIPTION
    Windows counterpart of tools/tailwind/build.sh, doing the same:
      powershell -ExecutionPolicy Bypass -File tools\tailwind\build.ps1          write the CSS (run it after changing
                                                                                classes in markup, then commit the CSS)
      powershell -ExecutionPolicy Bypass -File tools\tailwind\build.ps1 -Check   build to a temp file; exit 1 if the
                                                                                committed CSS differs (what CI checks)
    The CLI (pinned version, SHA-256 checked) is downloaded once from the official GitHub release into
    %LOCALAPPDATA%\speisedirekt3\tailwind\<version>\ (elsewhere: ~/.cache/speisedirekt3/tailwind/<version>/), never into
    the repository. Works with Windows PowerShell 5.1 and PowerShell 7.
#>
[CmdletBinding()]
param([switch] $Check)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # the progress bar makes Invoke-WebRequest very slow in Windows PowerShell

$Version = '3.4.17'
# sha256sums.txt of the v3.4.17 release
$Sha256 = @{
    'tailwindcss-windows-x64.exe'   = '67f1c5e3f5a03406a7bf5badf5ada09b79f3ae78ec43450c15f7e983068da346'
    'tailwindcss-windows-arm64.exe' = '76f516476784c00f1562160b5758e3d8f0e6c48957efb26b5b50fbdfd76aa382'
    'tailwindcss-linux-x64'         = '7d24f7fa191d2193b78cd5f5a42a6093e14409521908529f42d80b11fde1f1d4'
    'tailwindcss-linux-arm64'       = '69b1378b8133192d7d2feb12a116fa12d035594f58db3eff215879e4ad8cf39b'
    'tailwindcss-macos-x64'         = '6cbdad74be776c087ffa5e9a057512c54898f9fe8828d3362212dfe32fc933a3'
    'tailwindcss-macos-arm64'       = 'a1d0c7985759accca0bf12e51ac1dcbf0f6cf2fffb62e6e0f62d091c477a10a3'
}

$Here = $PSScriptRoot
$Repo = (Resolve-Path (Join-Path $Here '..\..')).Path
$Output = Join-Path $Repo 'SpeiseDirekt3\wwwroot\css\tailwind.css'
$Config = Join-Path $Here 'tailwind.config.js'
$InputCss = Join-Path $Here 'input.css'

# Platform -> release asset ($IsWindows does not exist in Windows PowerShell 5.1)
if ($env:OS -eq 'Windows_NT') {
    $arch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64' -or $env:PROCESSOR_ARCHITEW6432 -eq 'ARM64') { 'arm64' } else { 'x64' }
    $asset = "tailwindcss-windows-$arch.exe"
    $cacheRoot = Join-Path $env:LOCALAPPDATA 'speisedirekt3\tailwind'
} else {
    $osArch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    $arch = switch ($osArch) { 'X64' { 'x64' } 'Arm64' { 'arm64' } default { throw "Unsupported CPU architecture: $osArch" } }
    $os = if ($IsMacOS) { 'macos' } else { 'linux' }
    $asset = "tailwindcss-$os-$arch"
    $cacheBase = if ($env:XDG_CACHE_HOME) { $env:XDG_CACHE_HOME } else { Join-Path $HOME '.cache' }
    $cacheRoot = Join-Path $cacheBase 'speisedirekt3/tailwind'
}
$expected = $Sha256[$asset]

function Get-Sha256([string] $Path) { (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant() }

# Download once, verify every time
$dir = Join-Path $cacheRoot $Version
$cli = Join-Path $dir $asset
if (-not (Test-Path -LiteralPath $cli) -or (Get-Sha256 $cli) -ne $expected) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $url = "https://github.com/tailwindlabs/tailwindcss/releases/download/v$Version/$asset"
    Write-Host "Downloading $url"
    $tmp = Join-Path $dir ("$asset." + [System.Guid]::NewGuid().ToString('N') + '.download')
    try {
        # Windows PowerShell 5.1 does not offer TLS 1.2 by default
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $url -OutFile $tmp -UseBasicParsing
        $actual = Get-Sha256 $tmp
        if ($actual -ne $expected) { throw "SHA-256 mismatch for ${asset}: expected $expected, got $actual" }
        if ($env:OS -ne 'Windows_NT') { & chmod +x $tmp }
        Move-Item -LiteralPath $tmp -Destination $cli -Force
    } finally {
        if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Force }
    }
}

# The browserslist data inside the pinned CLI is as old as the CLI; its warning is noise here
$env:BROWSERSLIST_IGNORE_OLD_DATA = '1'

if ($Check) {
    $tmp = Join-Path ([System.IO.Path]::GetTempPath()) ('tailwind-check-' + [System.Guid]::NewGuid().ToString('N') + '.css')
    try {
        & $cli -c $Config -i $InputCss -o $tmp --minify
        if ($LASTEXITCODE -ne 0) { throw "Tailwind CLI failed with exit code $LASTEXITCODE" }
        $same = (Test-Path -LiteralPath $Output) -and (Get-Sha256 $tmp) -eq (Get-Sha256 $Output)
    } finally {
        if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Force }
    }
    if (-not $same) {
        Write-Host 'SpeiseDirekt3/wwwroot/css/tailwind.css is not what the markup needs (classes changed?).'
        Write-Host 'Run tools\tailwind\build.ps1 (Linux/macOS: bash tools/tailwind/build.sh) and commit the CSS.'
        exit 1
    }
    Write-Host 'SpeiseDirekt3/wwwroot/css/tailwind.css is up to date.'
} else {
    & $cli -c $Config -i $InputCss -o $Output --minify
    if ($LASTEXITCODE -ne 0) { throw "Tailwind CLI failed with exit code $LASTEXITCODE" }
}
