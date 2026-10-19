# End-to-end double-booking stress test against a RUNNING TutorScheduler API.
# Fires genuinely parallel HTTP requests and checks the outcomes.
#
# Usage (API running on the http profile):
#   powershell -ExecutionPolicy Bypass -File scripts\double-booking-stress-test.ps1
#
# It registers throwaway users (*@e2e.test) and slots far in the future, so point
# the API at a separate database if you don't want them in your dev data.
param([string]$BaseUrl = 'http://localhost:5041/api')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
[Net.ServicePointManager]::DefaultConnectionLimit = 200   # .NET Framework default is 2 - would serialize everything

$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromSeconds(60)
$run = [Guid]::NewGuid().ToString('N').Substring(0, 8)
$summary = New-Object System.Collections.ArrayList

function New-Req($method, $path, $token, $body) {
    $req = New-Object System.Net.Http.HttpRequestMessage((New-Object System.Net.Http.HttpMethod($method)), "$BaseUrl$path")
    if ($token) { $req.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Bearer', $token) }
    if ($null -ne $body) { $req.Content = New-Object System.Net.Http.StringContent(($body | ConvertTo-Json -Compress), [Text.Encoding]::UTF8, 'application/json') }
    return $req
}
function Read-Resp($resp) {
    $text = $resp.Content.ReadAsStringAsync().Result
    $parsed = $null
    if ($text) { try { $parsed = $text | ConvertFrom-Json } catch { $parsed = $text } }
    [pscustomobject]@{ Status = [int]$resp.StatusCode; Body = $parsed }
}
function Call($method, $path, $token, $body) { Read-Resp ($client.SendAsync((New-Req $method $path $token $body)).Result) }

# Build all requests first, then fire them back-to-back so they really overlap on the server.
function Invoke-Concurrently($specs) {
    $reqs = @($specs | ForEach-Object { New-Req $_.Method $_.Path $_.Token $_.Body })
    $tasks = @($reqs | ForEach-Object { $client.SendAsync($_) })
    [Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]$tasks)
    @($tasks | ForEach-Object { Read-Resp $_.Result })
}
function Check($name, [bool]$ok, $detail) {
    [void]$summary.Add([pscustomobject]@{ Result = $(if ($ok) { 'PASS' } else { 'FAIL' }); Test = $name; Detail = $detail })
}
function Codes($responses) { ($responses | Group-Object Status | Sort-Object Name | ForEach-Object { "$($_.Count)x$($_.Name)" }) -join ' ' }
function Register($name, $role) {
    $r = Call POST '/auth/register' $null @{ fullName = $name; email = "$($name.ToLower())-$run@e2e.test"; password = 'Passw0rd!'; role = $role }
    if ($r.Status -ne 200) { throw "register $name failed: $($r.Status) $($r.Body | ConvertTo-Json -Compress)" }
    $r.Body.token
}
function Iso([DateTime]$d) { $d.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'") }
function New-Slot($token, $subjectId, [DateTime]$start, [int]$minutes = 60) {
    Call POST '/lessonslots' $token @{ subjectId = $subjectId; startTimeUtc = (Iso $start); endTimeUtc = (Iso $start.AddMinutes($minutes)) }
}

# ---- setup -------------------------------------------------------------
$tutorA = Register "TutorA" 'Tutor'
$tutorB = Register "TutorB" 'Tutor'
$tutorC = Register "TutorC" 'Tutor'
$subject = (Call POST '/subjects' $tutorA @{ name = "E2E-$run" }).Body.id
$students = @(1..20 | ForEach-Object { Register "Student$_" 'Student' })
# Random far-future base so repeated runs never collide with each other.
$base = [DateTime]::UtcNow.Date.AddDays(100 + (Get-Random -Maximum 2000))

# ---- 1. 20 different students race for the same slot ------------------
$slot = (New-Slot $tutorA $subject $base).Body.id
$res = Invoke-Concurrently ($students | ForEach-Object { @{ Method = 'POST'; Path = "/lessonslots/$slot/bookings"; Token = $_ } })
$wins = @($res | Where-Object Status -eq 201).Count
$conf = @($res | Where-Object Status -eq 409).Count
Check '20 students book the same slot at once -> exactly 1 wins, 19 get 409' ($wins -eq 1 -and $conf -eq 19) (Codes $res)

# ---- 2. same student double-clicks the same slot 10 times -------------
$slot2 = (New-Slot $tutorA $subject $base.AddHours(2)).Body.id
$res = Invoke-Concurrently (1..10 | ForEach-Object { @{ Method = 'POST'; Path = "/lessonslots/$slot2/bookings"; Token = $students[0] } })
Check 'One student fires 10 parallel bookings for one slot -> exactly 1 wins' (@($res | Where-Object Status -eq 201).Count -eq 1) (Codes $res)

# ---- 3. one student, two DIFFERENT overlapping slots, at the same moment
# Tutor A 10:00-11:00 and Tutor B 10:30-11:30 (different tutors, so both slots legitimately exist).
$doubleBooked = 0; $rounds = 15; $roundCodes = @()
for ($i = 0; $i -lt $rounds; $i++) {
    $t = $base.AddDays(1).AddHours($i * 3)
    $sA = (New-Slot $tutorA $subject $t).Body.id
    $sB = (New-Slot $tutorB $subject $t.AddMinutes(30)).Body.id
    $student = $students[$i + 1]
    $res = Invoke-Concurrently @(
        @{ Method = 'POST'; Path = "/lessonslots/$sA/bookings"; Token = $student },
        @{ Method = 'POST'; Path = "/lessonslots/$sB/bookings"; Token = $student })
    $ok = @($res | Where-Object Status -eq 201).Count
    if ($ok -gt 1) { $doubleBooked++ }
    $roundCodes += ($res.Status -join '/')
}
Check "One student books 2 overlapping slots in parallel ($rounds rounds) -> never both" ($doubleBooked -eq 0) "rounds where student got BOTH: $doubleBooked/$rounds  [$($roundCodes -join ' ')]"

# ---- 4. sequential overlap is rejected; adjacent (touching) slots are allowed
$t = $base.AddDays(10).AddHours(10)   # its own day: section 3 fills days 1-2
$s1 = (New-Slot $tutorA $subject $t).Body.id                      # 10:00-11:00
$s2 = (New-Slot $tutorB $subject $t.AddMinutes(59)).Body.id       # 10:59-11:59 overlaps by 1 minute
$s3 = (New-Slot $tutorB $subject $t.AddMinutes(-60)).Body.id      # 09:00-10:00 touches the start
$s4 = (New-Slot $tutorC $subject $t.AddMinutes(60)).Body.id       # 11:00-12:00 touches the end
$st = $students[17]
$r1 = Call POST "/lessonslots/$s1/bookings" $st
$r2 = Call POST "/lessonslots/$s2/bookings" $st
$r3 = Call POST "/lessonslots/$s3/bookings" $st
$r4 = Call POST "/lessonslots/$s4/bookings" $st
Check 'Overlap by 1 minute (sequential) -> rejected' ($r1.Status -eq 201 -and $r2.Status -eq 400) "first=$($r1.Status) overlapping=$($r2.Status)"
Check 'Back-to-back slots 09-10 / 10-11 / 11-12 -> all allowed' ($r3.Status -eq 201 -and $r4.Status -eq 201) "before=$($r3.Status) after=$($r4.Status)"

# ---- 5. cancel then re-book --------------------------------------------
$bookingId = $r1.Body.bookingId
$c = Call DELETE "/bookings/$bookingId" $st
$again = Call POST "/lessonslots/$s1/bookings" $students[18]
$cancelTwice = Call DELETE "/bookings/$bookingId" $st
Check 'Cancel frees the slot; another student can then book it' ($c.Status -eq 204 -and $again.Status -eq 201) "cancel=$($c.Status) rebook=$($again.Status)"
Check 'Cancelling the same booking twice -> rejected' ($cancelTwice.Status -ge 400) "second cancel=$($cancelTwice.Status)"

# ---- 6. tutor cannot have two overlapping slots ------------------------
$t = $base.AddDays(3)
$o1 = New-Slot $tutorC $subject $t
$o2 = New-Slot $tutorC $subject $t.AddMinutes(30)
Check 'Tutor creates an overlapping slot (sequential) -> rejected with 409' ($o1.Status -eq 201 -and $o2.Status -eq 409) "first=$($o1.Status) overlapping=$($o2.Status)"

$t = $base.AddDays(4)
$res = Invoke-Concurrently (1..5 | ForEach-Object { @{ Method = 'POST'; Path = '/lessonslots'; Token = $tutorC; Body = @{ subjectId = $subject; startTimeUtc = (Iso $t); endTimeUtc = (Iso $t.AddHours(1)) } } })
Check 'Tutor submits the same slot 5 times in parallel -> exactly 1 created' (@($res | Where-Object Status -eq 201).Count -eq 1) (Codes $res)

# ---- 7. role / ownership checks ---------------------------------------
$tutorBooks = Call POST "/lessonslots/$s4/bookings" $tutorA
$anon = Call POST "/lessonslots/$s4/bookings" $null
$otherTutorDelete = Call DELETE "/lessonslots/$s1" $tutorB
Check 'Tutor tries to book -> 403; no token -> 401' ($tutorBooks.Status -eq 403 -and $anon.Status -eq 401) "tutor=$($tutorBooks.Status) anonymous=$($anon.Status)"
Check "Tutor deletes another tutor's slot -> 403" ($otherTutorDelete.Status -eq 403) "status=$($otherTutorDelete.Status)"

# ---- 8. 409 body is a clear JSON message --------------------------------
$late = Call POST "/lessonslots/$slot/bookings" $students[19]
Check '409 response carries a readable error message' ($late.Status -eq 409 -and $late.Body.error) "message: $($late.Body.error)"

Write-Output "run id: $run"
$summary | Format-Table -AutoSize -Wrap
$fails = @($summary | Where-Object Result -eq 'FAIL').Count
Write-Output "TOTAL: $($summary.Count - $fails) passed, $fails failed"

