param(
    [string]$ProjectPath = (Join-Path $PSScriptRoot '..\src\DualSenseLighting\BetterControllerExperienceForFFXIV.csproj'),
    [string]$DotnetPath = 'dotnet',
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\artifacts\dependency-audit'),
    [string]$AuditSource = 'https://data.nuget.org/v3/index.json'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Invoke-Dotnet.ps1')
$dotnet = $DotnetPath
New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null
$OutputPath = (Resolve-Path $OutputPath).Path
# MSBuild evaluates the real SDK's imported PackageReference versions; no duplicate version table.
$evaluation = (Invoke-Dotnet -Arguments @('msbuild', $ProjectPath, '-getItem:PackageReference')) -join "`n"
$evaluation | Set-Content (Join-Path $OutputPath 'evaluated-packages.json')
$items = ($evaluation | ConvertFrom-Json).Items.PackageReference
[xml]$project = Get-Content -Raw $ProjectPath
$sdk = $project.Project.Sdk.Split('/')
if ($sdk.Count -ne 2 -or -not $sdk[0] -or -not $sdk[1]) { throw 'Expected a pinned package SDK in the production project.' }
$document = New-Object Xml.XmlDocument
$root = $document.CreateElement('Project'); $root.SetAttribute('Sdk', 'Microsoft.NET.Sdk'); $null = $document.AppendChild($root)
$properties = $document.CreateElement('PropertyGroup'); $null = $root.AppendChild($properties)
foreach ($pair in @(@('TargetFramework','net10.0-windows'), @('NuGetAudit','true'), @('NuGetAuditMode','all'),
    @('NuGetAuditLevel','low'), @('WarningsAsErrors','NU1900;NU1901;NU1902;NU1903;NU1904;NU1905'),
    @('EnableDefaultCompileItems','false'))) {
    $node = $document.CreateElement($pair[0]); $node.InnerText = $pair[1]; $null = $properties.AppendChild($node)
}
$references = $document.CreateElement('ItemGroup'); $null = $root.AppendChild($references)
$packages = @(@{ Identity=$sdk[0]; Version=$sdk[1] }) + @($items)
foreach ($package in $packages) {
    if (-not $package.Identity -or -not $package.Version) { throw 'Evaluated dependency lacks an exact package ID/version.' }
    $node = $document.CreateElement('PackageReference'); $node.SetAttribute('Include', $package.Identity)
    $node.SetAttribute('Version', "[$($package.Version)]"); $node.SetAttribute('ExcludeAssets', 'all')
    $null = $references.AppendChild($node)
}
$auditProject = Join-Path $OutputPath 'DependencyAudit.csproj'; $document.Save($auditProject)
[xml]$configuration = '<configuration><packageSources><clear/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources><auditSources><clear/><add key="official-audit"/></auditSources></configuration>'
$configuration.configuration.auditSources.add.SetAttribute('value', $AuditSource)
$configPath = Join-Path $OutputPath 'NuGet.Config'; $configuration.Save($configPath)
# Audit the actual locked production graph first, with isolated intermediate output.
# The SDK resolver package is outside that graph, so audit it explicitly below as well.
Invoke-Dotnet -Arguments @('restore', $ProjectPath, '--locked-mode', '--force', '--configfile', $configPath,
    "-p:MSBuildProjectExtensionsPath=$OutputPath\production-obj\", '-p:NuGetAudit=true', '-p:NuGetAuditMode=all',
    '-p:NuGetAuditLevel=low', '-warnaserror:NU1900,NU1901,NU1902,NU1903,NU1904,NU1905') |
    Tee-Object -FilePath (Join-Path $OutputPath 'production-restore.txt')
# Built-in restore handles advisory version ranges, all transitive packages, and feed failures.
Invoke-Dotnet -Arguments @('restore', $auditProject, '--force', '--configfile', $configPath) |
    Tee-Object -FilePath (Join-Path $OutputPath 'restore.txt')
