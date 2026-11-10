param(
    [Parameter(Mandatory=$true)][string]$Exe,
    [switch]$Live,
    [ValidateRange(1,100)][int]$Limit = 30,
    [string]$CasesFile,
    [string[]]$Ids,
    [switch]$UseEnvironmentKey,
    [string]$ApiBase,
    [string]$Model
)
# Windows PowerShell 5.1：仅发送合成场景，不读取聊天/画像，不执行生成的工具标记。
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$assemblyPath = (Resolve-Path -LiteralPath $Exe).Path
[void][System.Reflection.Assembly]::LoadFrom($assemblyPath)
if (!$CasesFile) { $CasesFile = Join-Path $repo 'tests/persona/cases.json' }
$cases = @(Get-Content -LiteralPath $CasesFile -Raw -Encoding UTF8 | ConvertFrom-Json | ForEach-Object { $_ })
if ($cases.Count -eq 0 -or @($cases.id | Select-Object -Unique).Count -ne $cases.Count) { throw 'Expected nonempty cases with unique IDs.' }
foreach ($case in $cases) {
    if (!$case.input -or !$case.expect -or !$case.avoid -or !$case.evidence) { throw "Invalid case: $($case.id)" }
}
if ($Ids) {
    foreach ($id in $Ids) { if ($id -notin $cases.id) { throw "Unknown case: $id" } }
    $cases = @($cases | Where-Object { $_.id -in $Ids })
}
$resultDir = Join-Path $repo ('artifacts/persona-eval/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $resultDir -Force | Out-Null
$key = $null
if ($Live) {
    $config = [ElymsPet.Storage]::Load()
    if ($UseEnvironmentKey) {
        # Explicit synthetic evaluation; never change the app's persisted settings.
        $key = [ElymsPet.ApiKey]::Clean($env:DEEPSEEK_API_KEY)
        if (!$ApiBase) { throw 'An explicit API base is required with the environment key.' }
    } else {
        if (!$config.AiChat) { throw 'Live evaluation requires enabled AI chat.' }
        $key = [ElymsPet.ApiKey]::Resolve($config).Key
    }
    if (![ElymsPet.ApiKey]::Valid($key)) { throw 'Live evaluation requires a configured key.' }
    if ($ApiBase) { $config.ApiBase = $ApiBase }
    if ($Model) { $config.ApiModel = $Model }
}
# API error logs, if any, go to this isolated folder. No user history or memory is loaded.
$originalFolder = [ElymsPet.Storage]::Folder
[ElymsPet.Storage]::Folder = $resultDir
$failed = 0
try {
    $state = New-Object ElymsPet.PetState
    foreach ($case in ($cases | Select-Object -First $Limit)) {
        $prompt = [ElymsPet.Persona]::SystemPrompt($state,[DateTime]::Now,'',[string]$case.dsh,[string]$case.web,[string]$case.memory)
        if ($case.mode -eq 'interview') { $prompt = [ElymsPet.ArchiveInterview]::BuildSystem($null) }
        $userPrompt = $case.input
        $temperature = [ElymsPet.DeepSeek]::DefaultTemperature
        $maxTokens = 2048
        if ($case.mode -eq 'web') {
            $evidenceText = [string]$case.webEvidence
            if ($case.evidenceFile) {
                $evidenceText = Get-Content -LiteralPath (Join-Path (Split-Path -Parent $CasesFile) $case.evidenceFile) -Raw -Encoding UTF8
            }
            $request = New-Object ElymsPet.ConversationRequest -ArgumentList $state,$case.input,'',[string]$case.dsh,[string]$case.web,[string]$case.memory,$evidenceText,([DateTime]::Now)
            $prompt = $request.SystemPrompt
            $userPrompt = $request.UserPrompt
            # Match ChatService.OnWebOutcome; this is the answer stage, not a search.
            $temperature = 0.4
            $maxTokens = 1600
        }
        $record = [ordered]@{ id=$case.id; personaVersion=[ElymsPet.CharacterPersona]::Version; input=$case.input; userPrompt=$userPrompt; history=@($case.history); expected=$case.expect; avoid=$case.avoid; live=[bool]$Live; prompt=$prompt }
        if ($Live) {
            $turns = [ElymsPet.ChatTurn[]]@(
                (New-Object ElymsPet.ChatTurn -ArgumentList 'system',$prompt)
            )
            foreach ($turn in $case.history) {
                if ($turn.role -notin @('user','assistant')) { throw 'Invalid synthetic history role.' }
                $turns += New-Object ElymsPet.ChatTurn -ArgumentList $turn.role,$turn.content
            }
            $turns += New-Object ElymsPet.ChatTurn -ArgumentList 'user',$userPrompt
            $answer = [ElymsPet.DeepSeek]::Complete($key,$config.ApiBase,$config.ApiModel,$true,$turns,45,$maxTokens,$temperature)
            $record.model = $config.ApiModel
            $record.temperature = $temperature
            $record.maxTokens = $maxTokens
            $record.responseModel = $answer.Model
            $record.finishReason = $answer.FinishReason
            $record.truncated = $answer.Truncated
            $record.ok = $answer.Ok
            $record.answer = $answer.Text
            $record.error = $answer.Error
            $record.promptTokens = $answer.PromptTokens
            $record.completionTokens = $answer.CompletionTokens
            $record.status = $answer.Status
            $record.seconds = $answer.Seconds
            # Inspect the production parsers; never dispatch the extracted requests.
            $webText = [string]$answer.Text
            $webIntent = [ElymsPet.WebTask]::Extract([ref]$webText)
            $record.webRequest = $webIntent.Value
            $dshText = [string]$answer.Text
            $record.dshRequest = [ElymsPet.DshTask]::Extract([ref]$dshText)
            if ($case.mode -eq 'interview') {
                try { $null = $answer.Text | ConvertFrom-Json -ErrorAction Stop; $record.validJson = $true }
                catch { $record.validJson = $false }
            }
            if (!$answer.Ok) { $failed++ }
            Write-Output ($case.id + ': ' + $(if ($answer.Ok) {'received'} else {'request failed'}))
        }
        $record | ConvertTo-Json -Depth 8 -Compress | Add-Content -LiteralPath (Join-Path $resultDir 'results.jsonl') -Encoding UTF8
        if ($failed -ge 3) { throw 'Stopped after three failed requests; inspect isolated results.' }
    }
} finally {
    [ElymsPet.Storage]::Folder = $originalFolder
    $key = $null
}
Write-Output ('Evaluation output: ' + $resultDir)
if ($failed -gt 0) { throw "$failed requests failed. Results are not a passing evaluation." }
