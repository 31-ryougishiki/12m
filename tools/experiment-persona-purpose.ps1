param(
    [Parameter(Mandatory=$true)][string]$Exe,
    [ValidateSet('main','ablation','stable')][string]$Stage='main',
    [switch]$Live
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
[void][Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $Exe).Path)
function Read-Text([string]$path) {
    return [IO.File]::ReadAllText((Join-Path $repo $path),[Text.Encoding]::UTF8)
}
$root = 'tests/persona/experiment/'
$basePrompt = (Read-Text ($root+'core.txt')) + "`n" + (Read-Text ($root+'scene.txt')) + "`n" + (Read-Text ($root+'examples.txt'))
$motive = Read-Text 'tests/persona/purpose/motive.txt'
$prompts = @{
    baseline=$basePrompt
    motive=$basePrompt+"`n"+$motive
    directed=$basePrompt+"`n"+$motive+"`n"+(Read-Text 'tests/persona/purpose/directed.txt')
    adaptive=$basePrompt+"`n"+$motive+"`n"+(Read-Text 'tests/persona/purpose/adaptive.txt')
}
$arms = @('baseline','motive','directed','adaptive')
if ($Stage -eq 'ablation') {
    $neutral = (Read-Text ($root+'core.txt'))+"`n"+$motive
    $prompts = @{
        neutral_directed=$neutral+"`n"+(Read-Text 'tests/persona/purpose/directed.txt')
        neutral_adaptive=$neutral+"`n"+(Read-Text 'tests/persona/purpose/adaptive.txt')
    }
    $arms = @('neutral_directed','neutral_adaptive')
}
if ($Stage -eq 'stable') {
    $prompts = @{stable_help=$basePrompt+"`n"+(Read-Text 'tests/persona/purpose/stable-help.txt')}
    $arms = @('stable_help')
}
$questions = ConvertFrom-Json -InputObject (Read-Text 'tests/persona/purpose/turns.json')
if ($questions.Count -ne 3 -or $questions[0] -isnot [string]) { throw 'Expected three plain-text questions.' }
$raw = Read-Text 'tests/persona/fixtures/ai-news.txt'
$policy = Read-Text ($root+'answer-policy.txt')
$secret = [ElymsPet.ApiKey]::Clean($env:DEEPSEEK_API_KEY)
if ($Live -and ![ElymsPet.ApiKey]::Valid($secret)) { throw 'Live experiment requires the process environment key.' }
$resultDir = Join-Path $repo ('artifacts/persona-purpose/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $resultDir -Force | Out-Null
$rng = New-Object Random 20261110
$sessions = @()
foreach ($arm in $arms) {
    foreach ($repeat in 1..3) {
        $history = New-Object 'System.Collections.Generic.List[ElymsPet.ChatTurn]'
        $history.Add((New-Object ElymsPet.ChatTurn -ArgumentList 'system',$prompts[$arm]))
        $sessions += [pscustomobject]@{id=$arm+'-'+$repeat;arm=$arm;repeat=$repeat;history=$history;stopped=$false}
    }
}
$failures = 0
$requests = 0
$index = 0
try {
    foreach ($turn in 0..2) {
        $schedule = @($sessions | ForEach-Object { [pscustomobject]@{session=$_;order=$rng.Next()} } | Sort-Object order)
        foreach ($entry in $schedule) {
            $session = $entry.session
            if ($session.stopped) { continue }
            $question = $questions[$turn]
            $user = $question
            if ($turn -eq 0) { $user = $policy.Replace('{evidence}',$raw).Replace('{question}',$question) }
            $session.history.Add((New-Object ElymsPet.ChatTurn -ArgumentList 'user',$user))
            if ($Live) {
                $outcome = [ElymsPet.DeepSeek]::Complete($secret,'https://api.deepseek.com','deepseek-flash',$true,$session.history.ToArray(),45,1600,0.7)
                $requests++
            } else {
                $outcome = [pscustomobject]@{Ok=$true;Text='DRY RUN';Seconds=0;PromptTokens=0;CompletionTokens=0;Model='';FinishReason='';Truncated=$false;Status=0}
            }
            $index++
            $record = [ordered]@{
                id='P'+$index.ToString('00');stage=$Stage;session=$session.id;arm=$session.arm;repeat=$session.repeat;turn=$turn+1
                live=[bool]$Live;question=$question;messages=@($session.history | ForEach-Object { @{role=$_.Role;content=$_.Content} })
                thinking=$true;reasoningEffort='low';requestedTemperature=0.7;temperatureEffective=$false;maxTokens=1600
                model=$outcome.Model;ok=$outcome.Ok;answer=$outcome.Text;seconds=$outcome.Seconds
                promptTokens=$outcome.PromptTokens;completionTokens=$outcome.CompletionTokens
                finishReason=$outcome.FinishReason;truncated=$outcome.Truncated;status=$outcome.Status
            }
            $record | ConvertTo-Json -Depth 8 -Compress | Add-Content -LiteralPath (Join-Path $resultDir 'results.jsonl') -Encoding UTF8
            if (!$outcome.Ok -or $outcome.Truncated) {
                $session.stopped=$true
                $failures++
            } else {
                $session.history.Add((New-Object ElymsPet.ChatTurn -ArgumentList 'assistant',$outcome.Text))
            }
            Write-Output ($record.id+': '+$(if($outcome.Ok -and !$outcome.Truncated){'received'}else{'failed or truncated'}))
            if ($failures -ge 3) { throw 'Stopped after three failed or truncated replies.' }
        }
    }
    Write-Output ('Experiment output: '+$resultDir)
    Write-Output ('API requests: '+$requests)
} finally {
    $secret = $null
}
