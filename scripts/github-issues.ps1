#!/usr/bin/env pwsh
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Position = 0, Mandatory)]
    [ValidateSet(
        "new-outcome",
        "add-deliverable",
        "status",
        "ready",
        "check",
        "close-deliverable",
        "close-outcome")]
    [string]$Action,

    [string]$Repository,
    [int]$Parent,
    [int]$Issue,
    [int]$PullRequest,
    [ValidatePattern("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    [string]$Key,
    [string]$Title,
    [string]$Outcome,
    [string]$Context,
    [string]$Result,
    [string[]]$Constraint = @(),
    [string[]]$Done = @(),
    [string[]]$Requirement = @(),
    [string[]]$Evidence = @(),
    [int[]]$BlockedBy = @(),
    [ValidateSet("requirements", "evidence", "done")]
    [string]$Section = "requirements",
    [int]$Index,
    [switch]$Undo,
    [switch]$Force,
    [switch]$Json
)

$ErrorActionPreference = "Stop"

function Assert-Command {
    param([Parameter(Mandatory)][string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found on PATH."
    }
}

function Invoke-Gh {
    param([Parameter(Mandatory)][string[]]$Arguments)

    $output = & gh @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "gh $($Arguments -join ' ') failed:`n$($output -join [Environment]::NewLine)"
    }

    return $output
}

function Resolve-Repository {
    if ($Repository) {
        return $Repository
    }

    $resolved = Invoke-Gh @("repo", "view", "--json", "nameWithOwner", "--jq", ".nameWithOwner")
    if (-not $resolved) {
        throw "Could not resolve the current GitHub repository. Pass -Repository owner/name."
    }

    return "$resolved".Trim()
}

function Assert-Value {
    param(
        [Parameter(Mandatory)][string]$Name,
        [AllowEmptyString()][string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw "$Name is required for action '$Action'."
    }
}

function Format-Checklist {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]]$Items
    )

    if ($Items | Where-Object { [string]::IsNullOrWhiteSpace($_) }) {
        throw "Checklist items cannot be empty."
    }

    return ($Items | ForEach-Object { "- [ ] $($_.Trim())" }) -join "`n"
}

function Format-Bullets {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]]$Items
    )

    if ($Items.Count -eq 0) {
        return "_None recorded._"
    }
    if ($Items | Where-Object { [string]::IsNullOrWhiteSpace($_) }) {
        throw "Bullet items cannot be empty."
    }

    return ($Items | ForEach-Object { "- $($_.Trim())" }) -join "`n"
}

function New-OutcomeBody {
    Assert-Value "Key" $Key
    Assert-Value "Outcome" $Outcome
    Assert-Value "Context" $Context
    if ($Done.Count -eq 0) {
        throw "At least one -Done item is required for an outcome."
    }

    $constraints = Format-Bullets $Constraint
    $doneItems = Format-Checklist $Done
    return @"
<!-- rt-workflow:outcome:$Key -->
## Desired outcome

$Outcome

## Context

$Context

## Constraints

$constraints

## Definition of Done

<!-- rt-workflow:done:start -->
$doneItems
<!-- rt-workflow:done:end -->

Delivery is tracked through the official sub-issues attached to this issue.
"@
}

function New-DeliverableBody {
    Assert-Value "Key" $Key
    Assert-Value "Result" $Result
    if ($Requirement.Count -eq 0) {
        throw "At least one -Requirement item is required for a deliverable."
    }
    if ($Evidence.Count -eq 0) {
        throw "At least one -Evidence item is required for a deliverable."
    }

    $requirements = Format-Checklist $Requirement
    $evidenceItems = Format-Checklist $Evidence
    $constraints = Format-Bullets $Constraint
    $blocked = if ($BlockedBy.Count -eq 0) {
        "_None._"
    } else {
        ($BlockedBy | ForEach-Object { "- #$_" }) -join "`n"
    }

    return @"
<!-- rt-workflow:deliverable:$Key -->
## Deliverable result

$Result

## Testable requirements

<!-- rt-workflow:requirements:start -->
$requirements
<!-- rt-workflow:requirements:end -->

## Verification evidence

<!-- rt-workflow:evidence:start -->
$evidenceItems
<!-- rt-workflow:evidence:end -->

## Constraints

$constraints

## Blocked by

<!-- rt-workflow:blocked-by:start -->
$blocked
<!-- rt-workflow:blocked-by:end -->
"@
}

function Write-BodyFile {
    param([Parameter(Mandatory)][string]$Body)

    $path = Join-Path ([System.IO.Path]::GetTempPath()) "rt-issue-$([Guid]::NewGuid().ToString('N')).md"
    Set-Content -LiteralPath $path -Value $Body -Encoding utf8NoBOM
    return $path
}

function Get-AllIssues {
    $raw = Invoke-Gh @(
        "issue", "list",
        "--repo", $script:Repo,
        "--state", "all",
        "--limit", "1000",
        "--json", "number,title,state,body,url")
    return @($raw | ConvertFrom-Json)
}

function Get-Issue {
    param([Parameter(Mandatory)][int]$Number)

    $raw = Invoke-Gh @(
        "issue", "view", "$Number",
        "--repo", $script:Repo,
        "--json", "number,title,state,stateReason,body,url,updatedAt,blockedBy,parent,closedByPullRequestsReferences")
    return $raw | ConvertFrom-Json
}

function Get-SubIssues {
    param([Parameter(Mandatory)][int]$ParentNumber)

    $raw = Invoke-Gh @(
        "api",
        "-H", "X-GitHub-Api-Version: 2026-03-10",
        "repos/$($script:Repo)/issues/$ParentNumber/sub_issues?per_page=100")
    return @($raw | ConvertFrom-Json)
}

function Get-BlockedBy {
    param([Parameter(Mandatory)]$IssueData)

    return @($IssueData.blockedBy | ForEach-Object { [int]$_.number })
}

function Get-ChildState {
    param([Parameter(Mandatory)]$Child)

    $blockedBy = @(Get-BlockedBy $Child)
    $unresolved = @(
        $Child.blockedBy |
            Where-Object state -ne "CLOSED" |
            ForEach-Object { [int]$_.number }
    )

    return [pscustomobject]@{
        number = $Child.number
        title = $Child.title
        state = $Child.state
        stateReason = $Child.stateReason
        ready = $Child.state -eq "OPEN" -and $unresolved.Count -eq 0
        blockedBy = $blockedBy
        unresolved = $unresolved
        url = $Child.url
    }
}

function Get-WorkflowStatus {
    param([Parameter(Mandatory)][int]$ParentNumber)

    $parentIssue = Get-Issue $ParentNumber
    $childStates = @(
        Get-SubIssues $ParentNumber |
            ForEach-Object { Get-Issue $_.number } |
            ForEach-Object { Get-ChildState $_ }
    )

    return [pscustomobject]@{
        parent = [pscustomobject]@{
            number = $parentIssue.number
            title = $parentIssue.title
            state = $parentIssue.state
            url = $parentIssue.url
        }
        progress = [pscustomobject]@{
            total = $childStates.Count
            closed = @($childStates | Where-Object state -eq "CLOSED").Count
            ready = @($childStates | Where-Object ready).Count
        }
        children = $childStates
    }
}

function Set-ChecklistItem {
    param(
        [Parameter(Mandatory)][string]$Body,
        [Parameter(Mandatory)][string]$SectionName,
        [Parameter(Mandatory)][int]$ItemIndex,
        [Parameter(Mandatory)][bool]$Checked
    )

    if ($ItemIndex -lt 1) {
        throw "-Index is one-based and must be greater than zero."
    }

    $start = "<!-- rt-workflow:$SectionName`:start -->"
    $end = "<!-- rt-workflow:$SectionName`:end -->"
    $startIndex = $Body.IndexOf($start, [StringComparison]::Ordinal)
    $endIndex = $Body.IndexOf($end, [StringComparison]::Ordinal)
    if ($startIndex -lt 0 -or $endIndex -le $startIndex) {
        throw "Issue does not contain a managed '$SectionName' checklist."
    }

    $contentStart = $startIndex + $start.Length
    $sectionText = $Body.Substring($contentStart, $endIndex - $contentStart)
    $matches = [regex]::Matches($sectionText, "(?m)^- \[(?<mark>[ xX])\] (?<text>.+)$")
    if ($ItemIndex -gt $matches.Count) {
        throw "Checklist '$SectionName' has $($matches.Count) item(s); index $ItemIndex is out of range."
    }

    $match = $matches[$ItemIndex - 1]
    $mark = if ($Checked) { "x" } else { " " }
    $replacement = "- [$mark] $($match.Groups['text'].Value)"
    $updatedSection = $sectionText.Remove($match.Index, $match.Length).Insert(
        $match.Index,
        $replacement)

    return $Body.Substring(0, $contentStart) +
        $updatedSection +
        $Body.Substring($endIndex)
}

function Assert-ChecklistComplete {
    param(
        [AllowNull()]
        [AllowEmptyString()]
        [string]$Body,
        [Parameter(Mandatory)][string]$SectionName
    )

    $Body = "$Body"
    $start = "<!-- rt-workflow:$SectionName`:start -->"
    $end = "<!-- rt-workflow:$SectionName`:end -->"
    if ($Body -notmatch "(?s)$([regex]::Escape($start))(.*?)$([regex]::Escape($end))") {
        if ($Force) {
            return
        }
        throw "Issue does not contain a managed '$SectionName' checklist. Use -Force to bypass."
    }

    $section = $Matches[1]
    $contentLines = @(
        $section -split "\r?\n" |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
    if ($contentLines.Count -eq 0) {
        throw "Cannot close: checklist '$SectionName' has no items."
    }

    foreach ($line in $contentLines) {
        if ($line -notmatch "^- \[(?<mark>[ xX])\] .+$") {
            throw "Cannot close: checklist '$SectionName' contains malformed content: $line"
        }
        if ($Matches["mark"] -eq " ") {
            throw "Cannot close: checklist '$SectionName' still has unchecked items."
        }
    }
}

function Enter-WorkflowMutex {
    param([Parameter(Mandatory)][string]$Resource)

    $safeResource = $Resource -replace "[^a-zA-Z0-9_.-]", "-"
    $mutex = [Threading.Mutex]::new($false, "rt-github-$safeResource")
    if (-not $mutex.WaitOne([TimeSpan]::FromSeconds(30))) {
        $mutex.Dispose()
        throw "Timed out waiting for GitHub workflow resource '$Resource'."
    }

    return $mutex
}

function Exit-WorkflowMutex {
    param([Parameter(Mandatory)][Threading.Mutex]$Mutex)

    try { $Mutex.ReleaseMutex() } catch [ApplicationException] { }
    $Mutex.Dispose()
}

function Assert-MergedPullRequestReferencesIssue {
    param(
        [Parameter(Mandatory)][int]$PullRequestNumber,
        [Parameter(Mandatory)]$TargetIssue
    )

    $pr = Invoke-Gh @(
        "pr", "view", "$PullRequestNumber",
        "--repo", $script:Repo,
        "--json", "number,mergedAt,url,body") | ConvertFrom-Json
    if (-not $pr.mergedAt) {
        throw "Pull request #$PullRequestNumber is not merged."
    }

    $shortReference = "(?m)(^|[\s(])#$($TargetIssue.number)(?=$|[\s,.;:)])"
    $fullReference = [regex]::Escape("$($TargetIssue.url)")
    if ($pr.body -notmatch $shortReference -and $pr.body -notmatch $fullReference) {
        throw "Pull request #$PullRequestNumber does not reference issue #$($TargetIssue.number)."
    }
}

function Write-Result {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        $Value
    )

    if ($Json) {
        ConvertTo-Json -InputObject $Value -Depth 10
        return
    }

    $Value
}

Assert-Command "gh"
$script:Repo = Resolve-Repository

switch ($Action) {
    "new-outcome" {
        Assert-Value "Title" $Title
        Assert-Value "Key" $Key
        $mutex = Enter-WorkflowMutex "$($script:Repo)-outcome-$Key"
        try {
            $marker = "<!-- rt-workflow:outcome:$Key -->"
            $existing = Get-AllIssues |
                Where-Object { $_.body -like "*$marker*" } |
                Select-Object -First 1
            if ($existing) {
                Write-Result $existing
                break
            }

            $body = New-OutcomeBody
            $bodyFile = Write-BodyFile $body
            try {
                if ($PSCmdlet.ShouldProcess("$script:Repo issue '$Title'", "Create outcome")) {
                    $url = Invoke-Gh @(
                        "issue", "create",
                        "--repo", $script:Repo,
                        "--title", $Title,
                        "--body-file", $bodyFile)
                    Write-Result ([pscustomobject]@{
                        number = [int]("$url".Trim().Split("/")[-1])
                        title = $Title
                        url = "$url".Trim()
                        created = $true
                    })
                }
            }
            finally {
                Remove-Item -LiteralPath $bodyFile -Force
            }
        }
        finally {
            Exit-WorkflowMutex $mutex
        }
    }

    "add-deliverable" {
        if ($Parent -le 0) { throw "-Parent is required for add-deliverable." }
        Assert-Value "Title" $Title
        Assert-Value "Key" $Key
        $parentIssue = Get-Issue $Parent
        if ($parentIssue.state -ne "OPEN") {
            throw "Parent issue #$Parent must be open."
        }
        if (-not $Force -and $parentIssue.body -notmatch "<!-- rt-workflow:outcome:[^>]+ -->") {
            throw "Parent issue #$Parent is not a managed outcome. Use -Force only for a legacy parent."
        }

        $mutex = Enter-WorkflowMutex "$($script:Repo)-deliverable-$Parent-$Key"
        try {
            $marker = "<!-- rt-workflow:deliverable:$Key -->"
            $existing = Get-SubIssues $Parent |
                ForEach-Object { Get-Issue $_.number } |
                Where-Object { $_.body -like "*$marker*" } |
                Select-Object -First 1
            if ($existing) {
                Write-Result $existing
                break
            }

            $body = New-DeliverableBody
            $bodyFile = Write-BodyFile $body
            try {
                if ($PSCmdlet.ShouldProcess("$script:Repo issue '$Title' under #$Parent", "Create deliverable")) {
                    $createArgs = @(
                        "issue", "create",
                        "--repo", $script:Repo,
                        "--parent", "$Parent",
                        "--title", $Title,
                        "--body-file", $bodyFile)
                    if ($BlockedBy.Count -gt 0) {
                        $createArgs += @("--blocked-by", ($BlockedBy -join ","))
                    }
                    $url = Invoke-Gh $createArgs
                    Write-Result ([pscustomobject]@{
                        number = [int]("$url".Trim().Split("/")[-1])
                        parent = $Parent
                        title = $Title
                        url = "$url".Trim()
                        created = $true
                    })
                }
            }
            finally {
                Remove-Item -LiteralPath $bodyFile -Force
            }
        }
        finally {
            Exit-WorkflowMutex $mutex
        }
    }

    "status" {
        if ($Parent -le 0) { throw "-Parent is required for status." }
        Write-Result (Get-WorkflowStatus $Parent)
    }

    "ready" {
        if ($Parent -le 0) { throw "-Parent is required for ready." }
        $ready = (Get-WorkflowStatus $Parent).children | Where-Object ready
        Write-Result @($ready)
    }

    "check" {
        if ($Issue -le 0) { throw "-Issue is required for check." }
        if ($Index -le 0) { throw "-Index is required for check." }
        $mutex = Enter-WorkflowMutex "$($script:Repo)-issue-$Issue"
        try {
            $target = Get-Issue $Issue
            $updated = Set-ChecklistItem $target.body $Section $Index (-not $Undo)
            $bodyFile = Write-BodyFile $updated
            try {
                if ($PSCmdlet.ShouldProcess("$script:Repo issue #$Issue", "Update $Section item $Index")) {
                    $latest = Get-Issue $Issue
                    if ($latest.updatedAt -ne $target.updatedAt -or $latest.body -ne $target.body) {
                        throw "Issue #$Issue changed during the update. Re-run the command."
                    }
                    Invoke-Gh @(
                        "issue", "edit", "$Issue",
                        "--repo", $script:Repo,
                        "--body-file", $bodyFile) | Out-Null
                    $written = Get-Issue $Issue
                    $expected = Set-ChecklistItem $written.body $Section $Index (-not $Undo)
                    if ($expected -ne $written.body) {
                        throw "Issue #$Issue did not retain the requested checklist state."
                    }
                    Write-Result $written
                }
            }
            finally {
                Remove-Item -LiteralPath $bodyFile -Force
            }
        }
        finally {
            Exit-WorkflowMutex $mutex
        }
    }

    "close-deliverable" {
        if ($Issue -le 0) { throw "-Issue is required for close-deliverable." }
        $target = Get-Issue $Issue
        if (-not $Force) {
            if ($target.body -notmatch "<!-- rt-workflow:deliverable:[^>]+ -->") {
                throw "Issue #$Issue is not a managed deliverable."
            }
            if (-not $target.parent) {
                throw "Issue #$Issue is not attached to an outcome parent."
            }
            $parentIssue = Get-Issue $target.parent.number
            if ($parentIssue.body -notmatch "<!-- rt-workflow:outcome:[^>]+ -->") {
                throw "Parent issue #$($parentIssue.number) is not a managed outcome."
            }
        }
        Assert-ChecklistComplete $target.body "requirements"
        Assert-ChecklistComplete $target.body "evidence"
        if ($target.state -eq "CLOSED") {
            if (-not $Force -and $target.stateReason -ne "COMPLETED") {
                throw "Issue #$Issue is closed with reason '$($target.stateReason)', not completed."
            }
            Write-Result $target
            break
        }
        if (-not $Force) {
            if ($parentIssue.state -ne "OPEN") {
                throw "Parent outcome #$($parentIssue.number) must remain open until its deliverables complete."
            }
            if ($PullRequest -le 0) {
                throw "-PullRequest is required to close a deliverable."
            }
            Assert-MergedPullRequestReferencesIssue $PullRequest $target
        }
        if ($PSCmdlet.ShouldProcess("$script:Repo issue #$Issue", "Close deliverable")) {
            Invoke-Gh @(
                "issue", "close", "$Issue",
                "--repo", $script:Repo,
                "--comment", "All managed requirements are complete.") | Out-Null
            Write-Result (Get-Issue $Issue)
        }
    }

    "close-outcome" {
        if ($Parent -le 0) { throw "-Parent is required for close-outcome." }
        $status = Get-WorkflowStatus $Parent
        $openChildren = @($status.children | Where-Object state -ne "CLOSED")
        if ($openChildren.Count -gt 0) {
            throw "Cannot close outcome #$Parent: $($openChildren.Count) sub-issue(s) remain open."
        }
        $nonCompletedChildren = @(
            $status.children | Where-Object stateReason -ne "COMPLETED"
        )
        if ($nonCompletedChildren.Count -gt 0) {
            throw "Cannot close outcome #$Parent: $($nonCompletedChildren.Count) sub-issue(s) were not completed."
        }

        $target = Get-Issue $Parent
        Assert-ChecklistComplete $target.body "done"
        if ($target.state -eq "CLOSED") {
            if (-not $Force -and $target.stateReason -ne "COMPLETED") {
                throw "Outcome #$Parent is closed with reason '$($target.stateReason)', not completed."
            }
            Write-Result $target
            break
        }
        if ($PSCmdlet.ShouldProcess("$script:Repo issue #$Parent", "Close outcome")) {
            Invoke-Gh @(
                "issue", "close", "$Parent",
                "--repo", $script:Repo,
                "--comment", "All sub-issues and managed Definition of Done checks are complete.") | Out-Null
            Write-Result (Get-Issue $Parent)
        }
    }
}
