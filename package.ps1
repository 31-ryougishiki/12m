param()
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'test.ps1')
$versionSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src/AppVersion.cs') -Raw
if ($versionSource -notmatch 'const string Value = "([0-9]+\.[0-9]+\.[0-9]+)"') { throw 'AppVersion.Value is missing' }
$version = $Matches[1]
$releaseRoot = Join-Path $PSScriptRoot 'artifacts/releases'
$stage = Join-Path $releaseRoot ('ElymsPet-' + $version + '-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
& (Join-Path $PSScriptRoot 'build.ps1') -Out (Join-Path $stage 'ElymsPet.exe') -Tests
foreach ($name in @('README.md','CHANGELOG.md','THIRD_PARTY_NOTICES.md','install-dsh-plugins.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $stage
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $stage '使用说明.md')
foreach ($plugin in @('dsh-plugin','dsh-bridge-plugin')) {
    $target = Join-Path $stage $plugin
    New-Item -ItemType Directory -Path $target | Out-Null
    foreach ($name in @('lib','package.json','cordis.patch.yml','README.md')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "$plugin/$name") -Destination $target -Recurse
    }
}
$research = Join-Path $stage 'research'
New-Item -ItemType Directory -Path $research | Out-Null
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'research') -File | Where-Object { $_.Extension -in @('.md','.txt') } | Copy-Item -Destination $research
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs') -Destination $stage -Recurse
$manifest = Get-ChildItem -LiteralPath $stage -File -Recurse | Sort-Object FullName | ForEach-Object {
    $relative = $_.FullName.Substring($stage.Length + 1)
    (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash + '  ' + $relative
}
$manifest | Set-Content -LiteralPath (Join-Path $stage 'SHA256SUMS.txt') -Encoding UTF8
$zip = Join-Path $releaseRoot ('ElymsPet-v' + $version + '-Windows.zip')
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
Write-Output ('Portable folder: ' + $stage)
Write-Output ('Portable ZIP: ' + $zip)
