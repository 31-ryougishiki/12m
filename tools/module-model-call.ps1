param([Parameter(Mandatory=$true)][string]$InputFile,[Parameter(Mandatory=$true)][string]$OutputFile)
$ErrorActionPreference='Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)
[void][Reflection.Assembly]::LoadFrom((Resolve-Path 'dist/ElymsPet.exe').Path)
$utf8=New-Object Text.UTF8Encoding($false)
$request=[IO.File]::ReadAllText($InputFile)|ConvertFrom-Json
if($request.ExportRules){
 $rules=@{Structure=[ElymsPet.ReadableExplanation]::StructureRules;Reader=[ElymsPet.FlowReaderReview]::ReaderRules;Revision=[ElymsPet.FlowReaderReview]::RevisionRules}
 [IO.File]::WriteAllText($OutputFile,($rules|ConvertTo-Json -Depth 5),$utf8)
 exit
}
$turns=[ElymsPet.ChatTurn[]]@($request.Turns|ForEach-Object {New-Object ElymsPet.ChatTurn -ArgumentList $_.Role,$_.Content})
$state=[ElymsPet.Storage]::Load();$key=[ElymsPet.ApiKey]::Resolve($state)
if(!$key.Configured){throw 'Configured model unavailable.'}
# Only the explicitly supplied turns enter the model. No chat, archive or memory is loaded.
$budget=16384
if($request.Stage -match ' (initial|author[12])$'){$budget=32768}
$r=[ElymsPet.DeepSeek]::Complete($key.Key,$state.ApiBase,$state.ApiModel,$true,$turns,600,$budget,0.1,'high',131072)
$result=@{Ok=$r.Ok;Text=$r.Text;Error=$r.Error;Model=$r.Model;Truncated=$r.Truncated;ResponseClipped=$r.ResponseClipped;FinishReason=$r.FinishReason;Seconds=$r.Seconds;PromptTokens=$r.PromptTokens;CompletionTokens=$r.CompletionTokens}
[IO.File]::WriteAllText($OutputFile,($result|ConvertTo-Json -Depth 6),$utf8)
if(!$r.Ok -or $r.Truncated -or $r.ResponseClipped){exit 2}
