param(
    [Parameter(Mandatory=$true)][string]$Exe,
    [switch]$Live
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$assembly = (Resolve-Path -LiteralPath $Exe).Path
[void][Reflection.Assembly]::LoadFrom($assembly)
$assetRoot = Join-Path $repo 'tests/persona/experiment'
function Read-Asset([string]$name) { [IO.File]::ReadAllText((Join-Path $assetRoot $name),[Text.Encoding]::UTF8) }
$core = Read-Asset 'core.txt'
$scene = Read-Asset 'scene.txt'
$examples = Read-Asset 'examples.txt'
$curated = Read-Asset 'fact-card.txt'
$raw = [IO.File]::ReadAllText((Join-Path $repo 'tests/persona/fixtures/ai-news.txt'),[Text.Encoding]::UTF8)
# Read text from UTF-8 fixture to keep Windows PowerShell 5.1 script encoding immaterial.
$question = (Get-Content -LiteralPath (Join-Path $repo 'tests/persona/web-opinion.json') -Raw -Encoding UTF8 | ConvertFrom-Json)[0].input
$policy = Read-Asset 'answer-policy.txt'
$extractPolicy = Read-Asset 'extract-policy.txt'
$resultDir = Join-Path $repo ('artifacts/persona-experiment/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $resultDir -Force | Out-Null
$originalFolder = [ElymsPet.Storage]::Folder
[ElymsPet.Storage]::Folder = $resultDir
$secret = [ElymsPet.ApiKey]::Clean($env:DEEPSEEK_API_KEY)
if ($Live -and ![ElymsPet.ApiKey]::Valid($secret)) { throw 'Live experiment requires the process environment key.' }
$script:failures = 0
$script:requestNumber = 0
$timestamp = [DateTime]::new(2026,11,9,18,0,0)
$state = New-Object ElymsPet.PetState
$rng = New-Object Random 20261109
$arms = @('current','lean','scene','examples','fact_card','two_stage')
$schedule = @()
foreach ($repeat in 1..3) {
    foreach ($arm in $arms) {
        foreach ($thinking in @($true,$false)) {
            $schedule += [pscustomobject]@{ arm=$arm; repeat=$repeat; thinking=$thinking; order=$rng.Next() }
        }
    }
}
$schedule = @($schedule | Sort-Object order)
function Save-Record($record) {
    $record | ConvertTo-Json -Depth 10 -Compress | Add-Content -LiteralPath (Join-Path $resultDir 'results.jsonl') -Encoding UTF8
}
function Invoke-Sample([string]$system,[string]$user,[bool]$thinking,[int]$tokens) {
    if (!$Live) { return [pscustomobject]@{ Ok=$true; Text='DRY RUN'; Seconds=0; PromptTokens=0; CompletionTokens=0; Model=''; FinishReason=''; Truncated=$false; Status=0 } }
    $turns = [ElymsPet.ChatTurn[]]@((New-Object ElymsPet.ChatTurn -ArgumentList 'system',$system),(New-Object ElymsPet.ChatTurn -ArgumentList 'user',$user))
    $outcome = [ElymsPet.DeepSeek]::Complete($secret,'https://api.deepseek.com','deepseek-flash',$thinking,$turns,45,$tokens,0.7)
    $script:requestNumber++
    Write-Output -InputObject $outcome
}
function New-Record($kind,$id,$arm,$repeat,$thinking,$system,$user,$outcome) {
    return [ordered]@{ kind=$kind; id=$id; arm=$arm; repeat=$repeat; thinking=$thinking; live=[bool]$Live; question=$question; personaVersion=[ElymsPet.CharacterPersona]::Version; system=$system; user=$user; requestedTemperature=0.7; temperatureEffective=(!$thinking); model=$outcome.Model; ok=$outcome.Ok; answer=$outcome.Text; seconds=$outcome.Seconds; promptTokens=$outcome.PromptTokens; completionTokens=$outcome.CompletionTokens; finishReason=$outcome.FinishReason; truncated=$outcome.Truncated; status=$outcome.Status }
}
try {
    $cards = @{}
    foreach ($repeat in 1..3) {
        $outcome = Invoke-Sample $extractPolicy $raw $true 1200
        $record = New-Record 'extract' ('extract-'+$repeat) 'two_stage' $repeat $true $extractPolicy $raw $outcome
        Save-Record $record
        if (!$outcome.Ok -or $outcome.Truncated) { throw 'Fact extraction failed or truncated; no substitution performed.' }
        $cards[$repeat] = $record
    }
    $index = 0
    foreach ($entry in $schedule) {
        $index++
        $system = $core
        if ($entry.arm -in @('scene','examples','fact_card','two_stage')) { $system += "`n" + $scene }
        if ($entry.arm -in @('examples','fact_card','two_stage')) { $system += "`n" + $examples }
        $evidence = $raw
        if ($entry.arm -eq 'fact_card') { $evidence = $curated }
        if ($entry.arm -eq 'two_stage') { $evidence = $cards[$entry.repeat].answer }
        $user = $policy.Replace('{evidence}',$evidence).Replace('{question}',$question)
        if ($entry.arm -eq 'current') {
            $request = New-Object ElymsPet.ConversationRequest -ArgumentList $state,$question,'','','','',$raw,$timestamp
            $system = $request.SystemPrompt
            $user = $request.UserPrompt
        }
        $outcome = Invoke-Sample $system $user $entry.thinking 1600
        $record = New-Record 'reply' ('S'+$index.ToString('00')) $entry.arm $entry.repeat $entry.thinking $system $user $outcome
        $record.pipelineSeconds = $outcome.Seconds
        if ($entry.arm -eq 'two_stage') {
            $record.extractionId = $cards[$entry.repeat].id
            $record.pipelineSeconds += $cards[$entry.repeat].seconds
        }
        Save-Record $record
        if (!$outcome.Ok) { $script:failures++ }
        Write-Output ($record.id + ': ' + $(if ($outcome.Ok) {'received'} else {'failed'}))
        if ($script:failures -ge 3) { throw 'Stopped after three failed final replies.' }
    }
    Write-Output ('Experiment output: ' + $resultDir)
    Write-Output ('API requests: ' + $script:requestNumber)
} finally {
    [ElymsPet.Storage]::Folder = $originalFolder
    $secret = $null
}
