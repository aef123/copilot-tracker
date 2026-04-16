BeforeAll {
    # Force-import the module so we can test both exported and internal functions
    $modulePath = "$PSScriptRoot\..\..\plugins\copilot-session-tracker\shared\CopilotTracker.psm1"
    Import-Module $modulePath -Force
}

Describe "CopilotTracker Module" {

    # ── URL Resolution ────────────────────────────────────────────────

    Describe "Initialize-TrackerConnection" {

        BeforeEach {
            # Reset module state between tests
            InModuleScope CopilotTracker {
                $script:BaseUrl = $null
                $script:SessionId = $null
                $script:ConfigLoaded = $false
                $script:ResourceId = "test-resource-id"
                $script:TenantId = "test-tenant-id"
            }
        }

        It "uses explicit BaseUrl parameter when provided" {
            InModuleScope CopilotTracker { $script:ConfigLoaded = $true }
            Initialize-TrackerConnection -BaseUrl "https://custom.example.com"
            InModuleScope CopilotTracker { $script:BaseUrl } | Should -Be "https://custom.example.com"
        }

        It "trims trailing slash from BaseUrl" {
            InModuleScope CopilotTracker { $script:ConfigLoaded = $true }
            Initialize-TrackerConnection -BaseUrl "https://custom.example.com/"
            InModuleScope CopilotTracker { $script:BaseUrl } | Should -Be "https://custom.example.com"
        }

        It "reads serverUrl from config file" {
            InModuleScope CopilotTracker {
                $script:ConfigLoaded = $true
                $script:BaseUrl = "https://from-config.example.com"
            }
            Initialize-TrackerConnection
            InModuleScope CopilotTracker { $script:BaseUrl } | Should -Be "https://from-config.example.com"
        }

        It "explicit BaseUrl overrides config file" {
            InModuleScope CopilotTracker {
                $script:ConfigLoaded = $true
                $script:BaseUrl = "https://from-config.example.com"
            }
            Initialize-TrackerConnection -BaseUrl "https://override.example.com"
            InModuleScope CopilotTracker { $script:BaseUrl } | Should -Be "https://override.example.com"
        }

        It "sets MachineId to COMPUTERNAME" {
            InModuleScope CopilotTracker { $script:ConfigLoaded = $true }
            Initialize-TrackerConnection -BaseUrl "https://test.example.com"
            InModuleScope CopilotTracker { $script:MachineId } | Should -Be $env:COMPUTERNAME
        }
    }

    # ── Auth Headers ──────────────────────────────────────────────────

    Describe "Get-TrackerHeaders" {

        It "returns correct Authorization header format" {
            InModuleScope CopilotTracker {
                Mock az { return "test-token-abc123" }
                $headers = Get-TrackerHeaders
                $headers.Authorization | Should -Be "Bearer test-token-abc123"
            }
        }

        It "returns Content-Type header" {
            InModuleScope CopilotTracker {
                Mock az { return "test-token" }
                $headers = Get-TrackerHeaders
                $headers."Content-Type" | Should -Be "application/json"
            }
        }

        It "calls az with correct resource ID" {
            InModuleScope CopilotTracker {
                Mock az { return "token" } -Verifiable -ParameterFilter {
                    # az is called with positional args; verify the resource ID is in there
                    $args -contains $script:ResourceId
                }
                Get-TrackerHeaders | Out-Null
                Should -InvokeVerifiable
            }
        }

        It "returns null when az CLI fails" {
            InModuleScope CopilotTracker {
                Mock az { return $null }
                $result = Get-TrackerHeaders
                $result | Should -BeNullOrEmpty
            }
        }

        It "writes a warning when az CLI fails" {
            InModuleScope CopilotTracker {
                Mock az { return $null }
                $result = Get-TrackerHeaders -WarningVariable warn 3>$null
                # The function should warn about failed token
                $warn | Should -Not -BeNullOrEmpty
            }
        }
    }

    # ── REST API Helper ───────────────────────────────────────────────

    Describe "Invoke-TrackerApi" {

        BeforeEach {
            InModuleScope CopilotTracker {
                $script:BaseUrl = "https://test-server.example.com"
            }
        }

        It "constructs the correct URI from path" {
            InModuleScope CopilotTracker {
                Mock Get-TrackerHeaders { return @{ Authorization = "Bearer fake"; "Content-Type" = "application/json" } }
                Mock Invoke-RestMethod {
                    $Uri | Should -Be "https://test-server.example.com/api/sessions/MACHINE/123"
                    return @{ id = "123" }
                }
                Invoke-TrackerApi -Path "/api/sessions/MACHINE/123"
            }
        }

        It "defaults to GET method" {
            InModuleScope CopilotTracker {
                Mock Get-TrackerHeaders { return @{ Authorization = "Bearer fake"; "Content-Type" = "application/json" } }
                Mock Invoke-RestMethod {
                    $Method | Should -Be "GET"
                    return @{}
                }
                Invoke-TrackerApi -Path "/api/test"
            }
        }

        It "supports POST method with body" {
            InModuleScope CopilotTracker {
                Mock Get-TrackerHeaders { return @{ Authorization = "Bearer fake"; "Content-Type" = "application/json" } }
                Mock Invoke-RestMethod {
                    $Method | Should -Be "POST"
                    $Body | Should -Not -BeNullOrEmpty
                    return @{ id = "new" }
                }
                Invoke-TrackerApi -Path "/api/sessions" -Method "POST" -Body '{"machineId":"PC"}'
            }
        }

        It "returns null when auth fails" {
            InModuleScope CopilotTracker {
                Mock Get-TrackerHeaders { return $null }
                $result = Invoke-TrackerApi -Path "/api/test"
                $result | Should -BeNullOrEmpty
            }
        }

        It "propagates HTTP errors" {
            InModuleScope CopilotTracker {
                Mock Get-TrackerHeaders { return @{ Authorization = "Bearer fake"; "Content-Type" = "application/json" } }
                Mock Invoke-RestMethod { throw "The remote server returned an error: (500) Internal Server Error." }
                { Invoke-TrackerApi -Path "/api/test" } | Should -Throw "*500*"
            }
        }
    }

    # ── Set-TrackerTask ───────────────────────────────────────────────

    Describe "Set-TrackerTask" {

        BeforeEach {
            InModuleScope CopilotTracker {
                $script:BaseUrl = "https://test-server.example.com"
                $script:SessionId = "session-abc"
                $script:MachineId = "TEST-PC"
            }
        }

        It "calls POST /api/tasks" {
            InModuleScope CopilotTracker {
                Mock Invoke-TrackerApi {
                    $Path | Should -Be "/api/tasks"
                    $Method | Should -Be "POST"
                    return @{ id = "task-001" }
                }
                Set-TrackerTask -Title "Build project" -Status "started"
            }
        }

        It "includes sessionId in body" {
            InModuleScope CopilotTracker {
                Mock Invoke-TrackerApi {
                    $parsed = $Body | ConvertFrom-Json
                    $parsed.sessionId | Should -Be "session-abc"
                    return @{ id = "task-001" }
                }
                Set-TrackerTask -Title "Build project" -Status "started"
            }
        }

        It "includes Title and Status in body" {
            InModuleScope CopilotTracker {
                Mock Invoke-TrackerApi {
                    $parsed = $Body | ConvertFrom-Json
                    $parsed.title | Should -Be "Run tests"
                    $parsed.status | Should -Be "done"
                    return @{ id = "task-002" }
                }
                Set-TrackerTask -Title "Run tests" -Status "done"
            }
        }

        It "includes optional TaskId when provided" {
            InModuleScope CopilotTracker {
                Mock Invoke-TrackerApi {
                    $parsed = $Body | ConvertFrom-Json
                    $parsed.taskId | Should -Be "existing-task-99"
                    return @{ id = "existing-task-99" }
                }
                Set-TrackerTask -TaskId "existing-task-99" -Title "Update" -Status "done"
            }
        }

        It "includes Result when provided" {
            InModuleScope CopilotTracker {
                Mock Invoke-TrackerApi {
                    $parsed = $Body | ConvertFrom-Json
                    $parsed.result | Should -Be "All 12 tests passed"
                    return @{ id = "task-003" }
                }
                Set-TrackerTask -Title "Tests" -Status "done" -Result "All 12 tests passed"
            }
        }

        It "includes ErrorMessage when provided" {
            InModuleScope CopilotTracker {
                Mock Invoke-TrackerApi {
                    $parsed = $Body | ConvertFrom-Json
                    $parsed.errorMessage | Should -Be "Compile error in auth.cs"
                    return @{ id = "task-004" }
                }
                Set-TrackerTask -Title "Build" -Status "failed" -ErrorMessage "Compile error in auth.cs"
            }
        }

        It "returns the task ID from response" {
            InModuleScope CopilotTracker {
                Mock Invoke-TrackerApi { return @{ id = "task-xyz" } }
                $id = Set-TrackerTask -Title "Work" -Status "started"
                $id | Should -Be "task-xyz"
            }
        }

        It "warns and returns nothing when no active session" {
            InModuleScope CopilotTracker {
                $script:SessionId = $null
                $result = Set-TrackerTask -Title "No session" -Status "started" -WarningVariable warn 3>$null
                $result | Should -BeNullOrEmpty
            }
        }

        It "defaults Source to 'prompt'" {
            InModuleScope CopilotTracker {
                Mock Invoke-TrackerApi {
                    $parsed = $Body | ConvertFrom-Json
                    $parsed.source | Should -Be "prompt"
                    return @{ id = "task-005" }
                }
                Set-TrackerTask -Title "Default source" -Status "started"
            }
        }

        It "defaults QueueName to 'default'" {
            InModuleScope CopilotTracker {
                Mock Invoke-TrackerApi {
                    $parsed = $Body | ConvertFrom-Json
                    $parsed.queueName | Should -Be "default"
                    return @{ id = "task-006" }
                }
                Set-TrackerTask -Title "Default queue" -Status "started"
            }
        }
    }

    # ── Add-TrackerLog ────────────────────────────────────────────────

    Describe "Add-TrackerLog" {

        BeforeEach {
            InModuleScope CopilotTracker {
                $script:BaseUrl = "https://test-server.example.com"
            }
        }

        It "calls POST /api/tasks/default/{taskId}/logs with correct body" {
            InModuleScope CopilotTracker {
                Mock Invoke-TrackerApi {
                    $Path | Should -Be "/api/tasks/default/task-abc/logs"
                    $Method | Should -Be "POST"
                    $parsed = $Body | ConvertFrom-Json
                    $parsed.logType | Should -Be "progress"
                    $parsed.message | Should -Be "Step 2 complete"
                }
                Add-TrackerLog -TaskId "task-abc" -LogType "progress" -Message "Step 2 complete"
            }
        }

        It "does nothing when BaseUrl is not set" {
            InModuleScope CopilotTracker {
                $script:BaseUrl = $null
                Mock Invoke-TrackerApi { throw "Should not be called" }
                # Should not throw
                Add-TrackerLog -TaskId "task-abc" -LogType "error" -Message "fail"
            }
        }
    }

    # ── Start-TrackerSession ──────────────────────────────────────────

    Describe "Start-TrackerSession" {

        BeforeEach {
            InModuleScope CopilotTracker {
                $script:BaseUrl = "https://test-server.example.com"
                $script:SessionId = $null
                $script:MachineId = "TEST-PC"
            }
        }

        It "calls POST /api/sessions" {
            InModuleScope CopilotTracker {
                Mock Invoke-TrackerApi {
                    $Path | Should -Be "/api/sessions"
                    $Method | Should -Be "POST"
                    return @{ id = "new-session-1" }
                }
                Mock Start-HeartbeatJob {}
                Start-TrackerSession
            }
        }

        It "stores session ID in module state" {
            InModuleScope CopilotTracker {
                Mock Invoke-TrackerApi { return @{ id = "new-session-2" } }
                Mock Start-HeartbeatJob {}
                Start-TrackerSession
                $script:SessionId | Should -Be "new-session-2"
            }
        }

        It "returns the session ID" {
            InModuleScope CopilotTracker {
                Mock Invoke-TrackerApi { return @{ id = "new-session-3" } }
                Mock Start-HeartbeatJob {}
                $result = Start-TrackerSession
                $result | Should -Be "new-session-3"
            }
        }

        It "includes repo and branch in body when provided" {
            InModuleScope CopilotTracker {
                Mock Invoke-TrackerApi {
                    $parsed = $Body | ConvertFrom-Json
                    $parsed.repository | Should -Be "my/repo"
                    $parsed.branch | Should -Be "feature-x"
                    return @{ id = "s1" }
                }
                Mock Start-HeartbeatJob {}
                Start-TrackerSession -Repo "my/repo" -Branch "feature-x"
            }
        }

        It "starts heartbeat job on success" {
            InModuleScope CopilotTracker {
                Mock Invoke-TrackerApi { return @{ id = "s1" } }
                Mock Start-HeartbeatJob {} -Verifiable
                Start-TrackerSession
                Should -InvokeVerifiable
            }
        }

        It "initializes connection if BaseUrl is not set" {
            InModuleScope CopilotTracker {
                $script:BaseUrl = $null
                Mock Initialize-TrackerConnection {} -Verifiable
                Mock Invoke-TrackerApi { return @{ id = "s1" } }
                Mock Start-HeartbeatJob {}
                Start-TrackerSession
                Should -InvokeVerifiable
            }
        }
    }

    # ── Complete-TrackerSession ───────────────────────────────────────

    Describe "Complete-TrackerSession" {

        BeforeEach {
            InModuleScope CopilotTracker {
                $script:BaseUrl = "https://test-server.example.com"
                $script:SessionId = "active-session"
                $script:MachineId = "TEST-PC"
            }
        }

        It "calls POST /api/sessions/{machineId}/{id}/complete" {
            InModuleScope CopilotTracker {
                Mock Stop-HeartbeatJob {}
                Mock Invoke-TrackerApi {
                    $Path | Should -Be "/api/sessions/TEST-PC/active-session/complete"
                    $Method | Should -Be "POST"
                }
                Complete-TrackerSession
            }
        }

        It "includes summary when provided" {
            InModuleScope CopilotTracker {
                Mock Stop-HeartbeatJob {}
                Mock Invoke-TrackerApi {
                    $parsed = $Body | ConvertFrom-Json
                    $parsed.summary | Should -Be "All done"
                }
                Complete-TrackerSession -Summary "All done"
            }
        }

        It "clears SessionId after completion" {
            InModuleScope CopilotTracker {
                Mock Stop-HeartbeatJob {}
                Mock Invoke-TrackerApi {}
                Complete-TrackerSession
                $script:SessionId | Should -BeNullOrEmpty
            }
        }

        It "stops heartbeat job" {
            InModuleScope CopilotTracker {
                Mock Stop-HeartbeatJob {} -Verifiable
                Mock Invoke-TrackerApi {}
                Complete-TrackerSession
                Should -InvokeVerifiable
            }
        }

        It "does nothing when no active session" {
            InModuleScope CopilotTracker {
                $script:SessionId = $null
                Mock Stop-HeartbeatJob { throw "Should not be called" }
                Mock Invoke-TrackerApi { throw "Should not be called" }
                Complete-TrackerSession
            }
        }
    }

    # ── Get-TrackerSession ────────────────────────────────────────────

    Describe "Get-TrackerSession" {

        BeforeEach {
            InModuleScope CopilotTracker {
                $script:BaseUrl = "https://test-server.example.com"
                $script:SessionId = "current-session"
                $script:MachineId = "TEST-PC"
            }
        }

        It "calls the REST API with correct path" {
            InModuleScope CopilotTracker {
                Mock Invoke-TrackerApi {
                    $Path | Should -Be "/api/sessions/TEST-PC/current-session"
                    return @{ id = "current-session" }
                }
                Get-TrackerSession
            }
        }

        It "uses explicit SessionId and MachineId parameters" {
            InModuleScope CopilotTracker {
                Mock Invoke-TrackerApi {
                    $Path | Should -Be "/api/sessions/OTHER-PC/other-session"
                    return @{ id = "other-session" }
                }
                Get-TrackerSession -SessionId "other-session" -MachineId "OTHER-PC"
            }
        }

        It "initializes connection if BaseUrl is not set" {
            InModuleScope CopilotTracker {
                $script:BaseUrl = $null
                Mock Initialize-TrackerConnection {} -Verifiable
                Mock Invoke-TrackerApi { return @{ id = "s1" } }
                Get-TrackerSession
                Should -InvokeVerifiable
            }
        }
    }

    # ── Send-TrackerHeartbeat ─────────────────────────────────────────

    Describe "Send-TrackerHeartbeat" {

        It "calls POST /api/sessions/{machineId}/{id}/heartbeat" {
            InModuleScope CopilotTracker {
                $script:BaseUrl = "https://test-server.example.com"
                $script:SessionId = "hb-session"
                $script:MachineId = "HB-PC"
                Mock Invoke-TrackerApi {
                    $Path | Should -Be "/api/sessions/HB-PC/hb-session/heartbeat"
                    $Method | Should -Be "POST"
                }
                Send-TrackerHeartbeat
            }
        }

        It "does nothing when no session is active" {
            InModuleScope CopilotTracker {
                $script:SessionId = $null
                $script:BaseUrl = "https://test-server.example.com"
                Mock Invoke-TrackerApi { throw "Should not be called" }
                Send-TrackerHeartbeat
            }
        }

        It "does nothing when BaseUrl is not set" {
            InModuleScope CopilotTracker {
                $script:SessionId = "some-session"
                $script:BaseUrl = $null
                Mock Invoke-TrackerApi { throw "Should not be called" }
                Send-TrackerHeartbeat
            }
        }
    }

    # ── Module Exports ────────────────────────────────────────────────

    Describe "Module Exports" {

        It "exports expected functions" {
            $exported = (Get-Module CopilotTracker).ExportedFunctions.Keys
            $expected = @(
                'Initialize-TrackerConnection',
                'Start-TrackerSession',
                'Send-TrackerHeartbeat',
                'Complete-TrackerSession',
                'Get-TrackerSession',
                'Set-TrackerTask',
                'Add-TrackerLog'
            )
            foreach ($fn in $expected) {
                $exported | Should -Contain $fn
            }
        }

        It "does not export internal helpers" {
            $exported = (Get-Module CopilotTracker).ExportedFunctions.Keys
            $exported | Should -Not -Contain "Get-TrackerHeaders"
            $exported | Should -Not -Contain "Invoke-TrackerApi"
            $exported | Should -Not -Contain "Start-HeartbeatJob"
            $exported | Should -Not -Contain "Stop-HeartbeatJob"
        }
    }
}
