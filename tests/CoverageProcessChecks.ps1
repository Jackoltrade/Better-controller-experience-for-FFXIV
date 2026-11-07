param([string]$MeasureScriptPath = (Join-Path $PSScriptRoot '..\tools\measure-coverage.ps1'),
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\artifacts\coverage-process-checks'))
$ErrorActionPreference = 'Stop'
$checks = 0
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message }; $script:checks++ }
$root = Join-Path $OutputPath ([guid]::NewGuid().ToString('N') + ' paths with spaces')
New-Item -ItemType Directory -Force $root | Out-Null
$root = (Resolve-Path $root).Path
$probe = Join-Path $root 'Collector Probe.exe'
# A process stand-in only: tests actual script arguments/copy/hash/error handling,
# not collector instrumentation fidelity (covered by the separate installed-collector probe).
Add-Type -ReferencedAssemblies @('System.dll', 'System.Core.dll', 'System.Xml.dll') -OutputAssembly $probe -OutputType ConsoleApplication -TypeDefinition @'
using System;
using System.IO;
using System.Linq;
using System.Xml;
public static class CoverageProbe {
 public static int Main(string[] args) {
  if(args.Contains("--version")){Console.WriteLine("18.12.0");return 0;}
  if(args.Contains("--help")){Console.WriteLine("fixture help");return 0;}
  int output=Array.IndexOf(args,"--output"), settings=Array.IndexOf(args,"--settings"), plugin=Array.IndexOf(args,"--plugin");
  if(output<0 || settings<0 || plugin<0)return 41;
  var xml=new XmlDocument();xml.Load(args[settings+1]);
  var dirs=xml.SelectNodes("/Configuration/CodeCoverage/ModulePaths/IncludeDirectories/Directory");
  if(dirs.Count!=2 || xml.SelectSingleNode("//EnableStaticManagedInstrumentation").InnerText!="True" || xml.SelectSingleNode("//EnableDynamicManagedInstrumentation").InnerText!="False")return 42;
  if(Path.GetDirectoryName(args[plugin+1])!=dirs[1].InnerText || Path.GetDirectoryName(args[plugin-1])!=dirs[0].InnerText)return 43;
  File.AppendAllText(args[plugin+1]," instrumented shadow");File.AppendAllText(args[plugin-1]," instrumented shadow");
  string mode=Environment.GetEnvironmentVariable("COVERAGE_PROCESS_FIXTURE_MODE");
  string original=Environment.GetEnvironmentVariable("COVERAGE_PROCESS_FIXTURE_ORIGINAL");
  if(mode=="changed")File.AppendAllText(original," changed original");
  if(mode=="deleted" || mode=="deleted-fail")File.Delete(original);
  if(mode=="missing")return 0;
  using(var w=XmlWriter.Create(args[output+1])){
   w.WriteStartElement("coverage");w.WriteStartElement("packages");
   if(mode!="empty"){
    w.WriteStartElement("package");w.WriteAttributeString("name","DualSenseLighting");w.WriteStartElement("classes");w.WriteStartElement("class");w.WriteAttributeString("filename",Environment.GetEnvironmentVariable("COVERAGE_PROCESS_FIXTURE_SOURCE"));
    w.WriteStartElement("lines");w.WriteStartElement("line");w.WriteAttributeString("number","1");w.WriteAttributeString("hits","1");w.WriteEndElement();w.WriteEndElement();w.WriteEndElement();w.WriteEndElement();w.WriteEndElement();
   }
   w.WriteEndElement();w.WriteEndElement();
  }
  return mode=="fail" || mode=="deleted-fail"?37:0;
 }
}
'@
$checksInputDirectory = Join-Path $root 'original checks'; $plugin = Join-Path $root 'original plugin'
New-Item -ItemType Directory $checksInputDirectory, $plugin | Out-Null
$checksDll = Join-Path $checksInputDirectory 'LightingChecks.dll'; $pluginDll = Join-Path $plugin 'DualSenseLighting.dll'
$originalFiles = @($checksDll, (Join-Path $checksInputDirectory 'LightingChecks.pdb'), (Join-Path $checksInputDirectory 'LightingChecks.deps.json'),
    (Join-Path $checksInputDirectory 'LightingChecks.runtimeconfig.json'), $pluginDll, (Join-Path $plugin 'DualSenseLighting.pdb'))
foreach ($file in $originalFiles) { Set-Content -LiteralPath $file 'original input' }
Set-Content (Join-Path $checksInputDirectory 'sfx-trace-should-not-copy.json') 'private archive'
New-Item -ItemType Directory (Join-Path $checksInputDirectory 'recordings') | Out-Null
Set-Content (Join-Path $checksInputDirectory 'recordings\nested.json') 'private archive'
Set-Content (Join-Path $plugin 'unrelated.json') 'not a plugin dependency'
$tools = Join-Path $root 'tools'; $collector = Join-Path $tools '.store\dotnet-coverage\18.12.0\dotnet-coverage\18.12.0\tools\net8.0\any\dotnet-coverage.dll'
New-Item -ItemType Directory -Force (Split-Path $collector) | Out-Null
Set-Content $collector 'collector stand-in'
$previousMode = $env:COVERAGE_PROCESS_FIXTURE_MODE; $previousSource = $env:COVERAGE_PROCESS_FIXTURE_SOURCE
$previousOriginal = $env:COVERAGE_PROCESS_FIXTURE_ORIGINAL
$env:COVERAGE_PROCESS_FIXTURE_ORIGINAL = $checksDll
$env:COVERAGE_PROCESS_FIXTURE_SOURCE = (Resolve-Path (Join-Path $PSScriptRoot '..\src\DualSenseLighting\Core\HapticSettings.cs')).Path
try {
 foreach ($mode in @('success', 'fail', 'empty', 'missing', 'changed', 'deleted', 'deleted-fail')) {
  foreach ($file in $originalFiles) { Set-Content -LiteralPath $file 'original input' }
  $env:COVERAGE_PROCESS_FIXTURE_MODE=$mode
  $out = Join-Path $root $mode
  $failed=$false; $errorText=''
  try { & $MeasureScriptPath -ChecksPath $checksDll -PluginPath $pluginDll -DalamudLibPath 'SDK path with spaces' -DotnetPath $probe -ToolPath $tools -OutputPath $out }
  catch { $failed=$true; $errorText=$_.Exception.ToString() }
  Check ($failed -eq ($mode -ne 'success')) "Unexpected coverage process outcome: $mode $errorText"
  $run = @(Get-ChildItem -LiteralPath $out -Directory)[0].FullName
  $fingerprints = Get-Content -LiteralPath (Join-Path $run 'input-fingerprints.json') -Raw | ConvertFrom-Json
  $integrityFailure = $mode -in @('changed', 'deleted', 'deleted-fail')
  Check ($fingerprints.Count -eq 6 -and @($fingerprints | Where-Object { -not $_.Unchanged }).Count -eq $(if ($integrityFailure) { 1 } else { 0 })) 'Original input receipt lost entries or integrity result'
  if (-not $integrityFailure) {
   Check ((Get-Content -LiteralPath $checksDll -Raw).Trim() -eq 'original input' -and (Get-Content -LiteralPath $pluginDll -Raw).Trim() -eq 'original input') 'Collector modified original binaries'
  } else {
   Check ($errorText -match 'integrity verification failed') 'Original input integrity failure was not surfaced'
   $originalReceipt = $fingerprints | Where-Object { $_.OriginalPath -eq $checksDll }
   if ($mode -like 'deleted*') { Check ($null -eq $originalReceipt.AfterSha256 -and $originalReceipt.Error) 'Deleted original did not retain hash error and null after-hash' }
   if ($mode -eq 'deleted-fail') { Check ($errorText -match 'exit 37' -and (Get-Content (Join-Path $run 'failure.txt') -Raw) -match 'exit 37') 'Integrity failure masked the primary collector exit37' }
  }
  Check (-not (Test-Path (Join-Path $run 'checks\recordings')) -and -not (Test-Path (Join-Path $run 'checks\sfx-trace-should-not-copy.json')) -and -not (Test-Path (Join-Path $run 'plugin\unrelated.json'))) 'Coverage shadow copied trace archives/unrelated plugin files'
  Check (Test-Path (Join-Path $run 'run.log')) 'Process log missing'
  $transcriptStillOpen = $false
  try { Stop-Transcript | Out-Null; $transcriptStillOpen = $true } catch { }
  Check (-not $transcriptStillOpen) 'Coverage left its transcript open after collection/hash failure'
  if ($mode -eq 'success') { Check (Test-Path (Join-Path $run 'summary.json')) 'Populated report summary missing' }
  if ($mode -eq 'fail') { Check ($errorText -match 'exit 37' -and (Test-Path (Join-Path $run 'coverage.cobertura.xml'))) 'Collector exit failure lost or failed report removed' }
  if ($mode -eq 'empty') { Check ($errorText -match 'exit 1' -and (Test-Path (Join-Path $run 'failure.txt'))) 'Empty mapped coverage was accepted or diagnostic lost' }
  if ($mode -eq 'missing') { Check ($errorText -match 'STRUCTURAL_COVERAGE_UNAVAILABLE') 'Missing report was accepted' }
 }
 Write-Output "Coverage process fixtures passed: $checks checks; artifacts $root"
} finally { $env:COVERAGE_PROCESS_FIXTURE_MODE=$previousMode; $env:COVERAGE_PROCESS_FIXTURE_SOURCE=$previousSource; $env:COVERAGE_PROCESS_FIXTURE_ORIGINAL=$previousOriginal }
