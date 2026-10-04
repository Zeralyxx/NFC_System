# CHAPTER 4 RESULTS AND DISCUSSION

This chapter presents the implementation, documented functionality tests, controlled system observation, and manual paper-attendance baseline of the Risk-Based Two-Factor Student Verification and Event Attendance Management System. The analysis distinguishes software processing, recorded credential interaction, paper-writing duration, and log-derived entry spacing. It also separates expected security-rule rejections from ordinary participant errors.

Revision status: Sections 4.4 and 4.5 are reserved for the teammate's latest demographic and ISO evaluation data, as requested. This draft does not reuse the superseded survey figures. The current evidence does not support a matched end-to-end timing comparison; the remaining methodological requirements are identified in Section 4.6.5.

## 4.1 Implementation Details

### 4.1.1 System Architecture and Development Environment

The prototype uses an ESP32 microcontroller and PN532 NFC reader to acquire credential identifiers. NFC data is transferred through a USB serial connection to a Windows computer running a C# and WinUI 3 application. PIN entry and camera-based QR capture are handled by the application. The processing unit performs student-record lookup, configured eligibility checks, credential validation, transaction-state checks, logging, and administrative monitoring.

The application supports Fast mode using NFC validation, Standard mode using NFC and PIN validation, and High-Security mode using NFC, PIN, and QR credential validation. Operators select the mode according to the configured security requirements. The evaluation does not establish that the system independently inferred risk levels or automatically selected a mode for each participant.

Local MySQL storage supports the prototype's operational records, while Firebase Firestore is used for cloud synchronization. Local state and pending-record handling support operation during temporary connectivity interruptions. These implementation capabilities are distinguished from proof of uninterrupted or failure-free operation under all network conditions.

### 4.1.2 Technical Hurdles Resolved

State synchronization was revised to prioritize confirmed local state changes while remote synchronization was pending. This was intended to reduce stale-state conflicts affecting entry, exit, and PIN-security state. The documented offline-cache and synchronization tests provide evidence for the tested conditions, not a guarantee of universal state continuity.

Hardware lifecycle handling was introduced so the active window could acquire the NFC serial connection while other windows released it. Dialog sequencing and camera lifecycle handling were also revised to reduce overlapping operations and invalid-state exceptions. Timestamp serialization and clock-offset handling were used to improve consistency of exported and synchronized records. Formatting timestamps to millisecond precision does not, by itself, prove atomic database integrity.

## 4.2 Results Presentation

### 4.2.1 System Interfaces

The following figures reproduce the core interface illustrations from the existing thesis draft. They document the application views represented in that source; updated screenshots should replace them if the final build differs.

![Figure 4.1. System Authentication Interface](C:/Users/justine mendoza/source/repos/NFC_System/output/chapter4/full-chapter-assets/Interface-1.png)

The staff authentication interface requests an authorized credential before displaying the appropriate operational view. Its presence illustrates the intended access workflow, while access-control correctness is evaluated through the documented tests.

![Figure 4.2. Administrator Dashboard](C:/Users/justine mendoza/source/repos/NFC_System/output/chapter4/full-chapter-assets/Interface-2.png)

The administrator dashboard provides navigation to credential registration, attendance processing, gate verification, student records, security monitoring, and reports. Administrative operations remain distinct from the participant-facing verification workflow.

![Figure 4.3. Security Personnel Dashboard](C:/Users/justine mendoza/source/repos/NFC_System/output/chapter4/full-chapter-assets/Interface-3.png)

The security view emphasizes verification and monitoring tasks. The role-specific presentation limits the operational modules displayed to the assigned personnel.

![Figure 4.4. Event Organizer Dashboard](C:/Users/justine mendoza/source/repos/NFC_System/output/chapter4/full-chapter-assets/Interface-4.png)

The event-organizer view presents attendance and reporting functions relevant to event administration. The timing exports analysed below identify entry and exit transactions; they are not separately identified event-attendance transactions.

![Figure 4.5. Gate Monitoring Console](C:/Users/justine mendoza/source/repos/NFC_System/output/chapter4/full-chapter-assets/Interface-5.png)

The operator console presents the selected verification mode, student information, verification outcomes, and security alerts. Logged decisions from this workflow form the automated observation dataset.

![Figure 4.6. Student Facing Kiosk](C:/Users/justine mendoza/source/repos/NFC_System/output/chapter4/full-chapter-assets/Interface-6.png)

The kiosk presents access outcomes and the required credential prompts. A visible access decision is distinguished from a measured duration: complete NFC-tap-to-finish timing was not independently recorded in the supplied exports.

### 4.2.2 Verification Mode Routing

After record and eligibility checks, the verification engine routes the active session to the credential stages required by the selected mode. Exit handling is a separate state-reset pathway. The following excerpt illustrates the mode-dependent routing; it does not contain all eligibility, state, or persistence checks.

```csharp
if (mode == VerificationMode.Fast ||
    transactionType == TransactionType.Exit)
{
    return await GrantAsync(session,
        "NFC validation passed.");
}

// Standard and High-Security require PIN.
// After an accepted PIN, High-Security requires QR.
if (session.Mode == VerificationMode.HighSecurity &&
    !session.IsQrFallback)
{
    session.NextStep = VerificationStep.RequiresQr;
}
```
Figure 4.7. Verification Mode Routing Excerpt

## 4.3 Testing and Performance Evaluation

### 4.3.1 Functionality Testing Results

The updated functionality-test record documents the expected and actual results of sixteen selected scenarios. The reported PASS statuses refer to those documented scenarios, not to every observed transaction, every attack type, or all operating conditions. QR mismatch testing is not presented as proof that an identical copy of a valid QR credential can be detected.

**Table 4.1. Documented Functionality Test Cases**

| Case | Test condition | Expected result | Reported actual result | Status |
|---|---|---|---|---|
| T-01 | Valid NFC in Fast mode | Grant; update entry state | Granted; state updated | PASS |
| T-02 | Valid NFC and correct PIN | Grant Standard entry | Granted; PIN validated | PASS |
| T-03 | First or second wrong PIN | Reject PIN; allow retry | Invalid-PIN prompt displayed | PASS |
| T-04 | Third consecutive wrong PIN | Lock credential; generate alert | Lockout and alert reported | PASS |
| T-05 | Valid NFC, PIN and matching QR | Grant High-Security entry | Granted after credential checks | PASS |
| T-06 | Mismatched QR credential | Deny; generate mismatch alert | QR mismatch denial reported | PASS |
| T-07 | Unregistered NFC credential | Deny unknown credential | Unauthorized-credential denial | PASS |
| T-08 | Inactive configured student record | Deny before PIN/QR | Inactive-status denial | PASS |
| T-09 | Expelled configured student record | Deny before PIN/QR | Expelled-status denial | PASS |
| T-10 | Registered restricted-event attendee | Record attendance | Attendance recorded | PASS |
| T-11 | Unregistered restricted-event attendee | Deny event access | Unregistered-event denial | PASS |
| T-12 | Consecutive entry without exit | Deny state-rule violation | Anti-tailgating denial | PASS |
| T-13 | Entry followed by exit | Update inside/outside state | Both transitions reported | PASS |
| T-14 | Network disconnected | Use local verification data | Local verification reported | PASS |
| T-15 | Network restored | Synchronize pending records | Synchronization reported | PASS |
| T-16 | Application close with pending records | Require authorized close action | Admin-PIN interception reported | PASS |

The test-case results demonstrate the documented behavior for the tested inputs. They are not combined with the observation records to create an inflated authentication-accuracy denominator, because the source does not map each test case to every exported attempt.

### 4.3.2 System Metrics and Performance

#### Observation Procedure and Record Selection

System observation was conducted on August 12 and 13, 2026, in batches of 40 and 30 participants. According to the researcher's protocol, the same 70 participants tested Fast, Standard, and High-Security modes in that fixed order. The exports contain 70 successful entry trials per mode, or 210 successful entry trials across modes. This is not a sample of 210 distinct people.

Twenty NFC cards, preconfigured with PIN and QR credentials, were reused. For each mode, the first batch completed two rounds of 20 entries, while the second completed rounds of 20 and 10 entries. The research team performed exit transactions to reset the profiles between rounds. Explanations were provided before switching to the next mode. Reset exits were retained in the record audit but excluded from participant entry timing and authentication-accuracy calculations.

**Table 4.2. Classification of Exported System Records**

| Mode | Successful entries | Granted reset exits | Denied records | Total records |
|---|---|---|---|---|
| Fast | 70 | 70 | 2 | 142 |
| Standard | 70 | 70 | 12 | 152 |
| High-Security | 70 | 70 | 4 | 144 |
| Total | 210 | 210 | 18 | 438 |

All 210 records marked Entry and GRANTED were retained for timing analysis, including slow observations. No exact duplicate source rows were found. Repeated profile names were not removed because test cards were reused. The participant count is supported by the observation protocol, while the exported names identify test profiles rather than 70 uniquely assigned identities.

The researcher confirmed that wrong-PIN, PIN-lockout, QR-mismatch, and irregular-exit records resulted from deliberate demonstrations. Anti-tailgating records resulted from the researcher and students intentionally trying consecutive entry without exiting. These are expected invalid-condition rejections, not ordinary participants unexpectedly failing authentication. The expected-outcome classification comes from the researcher's account rather than from the verdict codes alone.

#### Average Processing Time

Chapter 3 defines average processing time from NFC tap to completed verification. The exports provide internal and interaction-stage timing fields, but not independent start and finish timestamps covering that complete boundary. Accordingly, full automated processing time under the original definition is not determined here. The available component measurements are reported separately and are not combined with an invented physical-interaction baseline.

**Table 4.3. Internal Timing for Successful Entry Trials**

| Mode | n | Mean system (ms) | Median (ms) | Sample SD (ms) | Range (ms) | Mean DB (ms) |
|---|---|---|---|---|---|---|
| Fast | 70 | 11.49 | 11.30 | 2.20 | 8.26-26.36 | 2.88 |
| Standard | 70 | 57.37 | 50.66 | 17.46 | 44.55-137.47 | 47.95 |
| High-Security | 70 | 54.06 | 52.64 | 6.34 | 49.44-86.36 | 44.90 |

Fast mode recorded mean internal latency of 11.49 ms, compared with 57.37 ms for Standard and 54.06 ms for High-Security. The observations do not show a strictly increasing internal latency with each added credential stage. Standard also showed greater dispersion, with sample SD of 17.46 ms and a maximum of 137.47 ms. Database-query duration overlaps the component processing measurements and must not be added again to Total System Latency.

**Table 4.4. Recorded PIN and QR Interaction Durations**

| Mode | n | Mean PIN (s) | Mean QR (s) | Mean total (s) | Median total (s) | Sample SD (s) | Maximum (s) |
|---|---|---|---|---|---|---|---|
| Fast | 70 | N/A | N/A | N/A | N/A | N/A | N/A |
| Standard | 70 | 2.505 | N/A | 2.505 | 2.035 | 1.152 | 6.875 |
| High-Security | 70 | 2.162 | 3.637 | 5.799 | 4.348 | 3.878 | 22.604 |

Standard recorded mean PIN interaction of 2.505 seconds. High-Security recorded mean PIN and QR interactions of 2.162 and 3.637 seconds, yielding mean combined interaction of 5.799 seconds. Its median was 4.348 seconds and maximum was 22.604 seconds, so longer observations increased the mean. Slow successful observations were retained. Fast bypassed PIN and QR; N/A does not mean zero-duration physical entry.

The lower mean High-Security PIN duration cannot be attributed to the mode itself. Mode order was fixed and respondents had already practised PIN entry in Standard mode. The results are descriptive; no causal or paired participant-level inference is made.

![Figure 4.8. Successful Entry Timing by Verification Mode](C:/Users/justine mendoza/source/repos/NFC_System/output/chapter4/Figure-System-Timing.png)

Panel A shows mean internal latency with sample-SD error bars. Panel B shows the distribution of recorded interaction duration. Neither panel measures complete NFC-tap-to-finish processing time.

#### System Throughput Rate

The original Chapter 3 throughput formula requires a successful-verification count and a defined elapsed observation period. Independent complete-round start and finish times were not supplied. Therefore, the following log-derived recorded-entry rate is presented as a supplementary measure, not as measured full-round throughput or uninterrupted campus capacity.

Each mode contained four entry rounds with 20, 20, 20, and 10 entries. A round with n entries contains n - 1 recorded between-entry intervals, giving 19 + 19 + 19 + 9 = 66 intervals per mode. Mean spacing equals summed within-round first-to-last spans divided by 66. Recorded-entry rate equals 60 multiplied by 66 divided by the summed spans in seconds.

**Table 4.5. Supplementary Recorded Entry Spacing and Rate**

| Mode | Rounds | Intervals | Summed spans (s) | Mean spacing (s) | Rate (/min) | Within-span denials |
|---|---|---|---|---|---|---|
| Fast | 4 | 66 | 180.200 | 2.730 | 21.98 | 2 |
| Standard | 4 | 66 | 531.167 | 8.048 | 7.46 | 8 |
| High-Security | 4 | 66 | 800.508 | 12.129 | 4.95 | 3 |

Fast recorded mean spacing of 2.730 seconds and a rate of 21.98 intervals per minute. Standard and High-Security recorded mean spacing of 8.048 and 12.129 seconds and rates of 7.46 and 4.95. Between-round reset intervals and explanations were excluded, while any within-round denials, demonstrations, or pauses remained included. Thirteen denied records fell within the selected spans. Deleting their rows would not delete the associated elapsed time.

![Figure 4.9. Supplementary Recorded Successful Entry Rate](C:/Users/justine mendoza/source/repos/NFC_System/output/chapter4/Figure-Recorded-Entry-Rate.png)

The displayed rates use 66 observed intervals per mode, not 70 complete individually timed transactions. Within-round invalid attempts and pauses remain included.

### 4.3.3 Authentication and Security Metrics

#### Authentication Accuracy Rate

Authentication accuracy was operationalized as agreement between the researcher-confirmed expected credential decision and the recorded actual outcome. The researcher confirmed that granted participant entries used expected-valid test credentials and that no invalid attempts were granted. The incorrect PIN, triggered lockout, and QR-mismatch attempts were expected to be denied. Correct rejection is therefore counted as a correct decision rather than an authentication failure.

The applicable authentication-decision set contains 210 valid granted entries and 12 credential-related expected denials: eight PIN failures, two newly triggered PIN lockouts, and two QR mismatches. Five anti-tailgating records were excluded because they were pre-authentication entry-state violations, and one irregular exit was excluded because it was an exit-state test. The 210 granted reset exits were also excluded. These rules yield 222 recorded applicable authentication decisions, not 222 distinct students or NFC sessions.

**Table 4.6. Observed Credential Decision Accuracy**

| Mode | Expected-valid grants | Expected-invalid denials | Applicable decisions | Correct decisions | Accuracy |
|---|---|---|---|---|---|
| Fast | 70 | 0 | 70 | 70 | 100.00% |
| Standard | 70 | 9 | 79 | 79 | 100.00% |
| High-Security | 70 | 3 | 73 | 73 | 100.00% |
| Total | 210 | 12 | 222 | 222 | 100.00% |

Observed credential-decision accuracy = (222 correct decisions / 222 applicable recorded decisions) x 100 = 100.00%. This is a descriptive result for known valid test credentials and deliberately invalid challenge conditions. It is not proof of universal accuracy, resistance to every attack, or verification of each participant's real identity. Twenty preconfigured cards were reused by 70 people, so the observation primarily tested configured credential validity and rule enforcement, not exclusive ownership of real university credentials. Expected outcomes were classified from the researcher's observation account, not through a blinded independent assessor.

#### Transaction Denial Rate

**Table 4.7. Logged Denial Conditions**

| Condition | Records | Researcher confirmed condition |
|---|---|---|
| PIN_FAILURE | 8 | Deliberately incorrect PIN submissions |
| PIN_LOCKED | 2 | Deliberately reached third consecutive wrong PIN |
| QR_REPLACED_OR_MISMATCH | 2 | Deliberately mismatched QR credential |
| ANTI_TAILGATING_VIOLATION | 5 | Consecutive entry without an intervening exit |
| IRREGULAR_EXIT_SEQUENCE | 1 | Deliberately invalid exit-state sequence |
| Total | 18 | Expected invalid-condition rejections |

**Table 4.8. Recorded Transaction Denial Rate**

| Mode | Granted records | Denied records | Total records | Denial rate |
|---|---|---|---|---|
| Fast | 140 | 2 | 142 | 1.41% |
| Standard | 140 | 12 | 152 | 7.89% |
| High-Security | 140 | 4 | 144 | 2.78% |
| Total | 420 | 18 | 438 | 4.11% |

Recorded transaction denial rate = (18 / 438) x 100 = 4.11%. The denominator includes participant entry records, operator-assisted reset exits, and deliberate invalid-condition records. It is not a participant-only failure rate. Standard's higher denial proportion reflects the tested mix, including deliberate wrong-PIN and lockout demonstrations, and is not evidence that the mode is less reliable.

No hardware-read or database-timeout code appears among these 18 denial conditions. This observation is narrower than a claim of zero hardware or database failures; missed scans, cancellations, and errors not represented by such codes were not independently audited in this dataset.

#### PIN Lockout Rate

The researcher confirmed that both PIN_LOCKED records resulted from deliberately reaching the third consecutive incorrect PIN submission, rather than repeat scans of an already locked credential. The researcher also confirmed that every individual PIN submission was captured in the exports. The recorded denominator was reconstructed from PIN-bearing entry outcomes, including retries and correct PIN stages followed by QR denial.

**Table 4.9. Recorded PIN Submissions and Lockout Rate**

| Mode | PIN stages in granted entries | PIN_FAILURE | Triggered PIN_LOCKED | PIN passed then QR denied | Total PIN submissions | Lockout rate |
|---|---|---|---|---|---|---|
| Standard | 70 | 7 | 2 | 0 | 79 | 2.53% |
| High-Security | 70 | 1 | 0 | 2 | 73 | 0.00% |
| Total | 140 | 8 | 2 | 2 | 152 | 1.32% |

Recorded PIN lockout rate = (2 newly triggered lockouts / 152 recorded PIN submissions) x 100 = 1.32%. The 152 submissions comprise 140 PIN stages in successful entries, eight PIN-failure records, two triggering lockout submissions, and two correct PIN stages followed by QR mismatch. Fast mode and reset exits did not require PIN and were excluded. The denominator counts individual submissions, not participants or successful workflows.

The export hides the PIN component fields for PIN_LOCKED records even though their aggregate timings retain PIN-related time. These two submissions were retained based on the triggered-lockout condition, nonzero aggregate timing, and researcher confirmation; counting only populated PIN component fields would omit them. The percentage describes intentional test-induced lockouts, not spontaneous usability failures among students.

### 4.3.4 Manual Paper Attendance Baseline

The manual baseline contains 70 reported individual durations from a simulated paper-based event-attendance process. Participants wrote their name, course and section, student ID, and signature. The researcher clarified that timing began with the first written character and ended when all required information was completed. Personnel did not check an ID or enrollment status during this simulation.

The measurement therefore represents active paper-attendance writing duration. It excludes any unrecorded time before the first character, queue waiting, acquisition or handover of the pen, and subsequent validation or encoding. It should not be labelled complete manual identity verification time.

**Table 4.10. Manual Paper Attendance Writing Duration**

| Measure | Result |
|---|---|
| Number of reported observations | 70 |
| Sum of individual writing durations | 1,526.24 s |
| Mean writing duration | 21.803 s |
| Median writing duration | 21.135 s |
| Sample standard deviation | 4.436 s |
| Minimum writing duration | 13.21 s |
| Maximum writing duration | 37.63 s |
| Service-time-equivalent rate | 2.75 writing completions/min |

The recalculated mean of 21.803 seconds agrees with the sheet's rounded mean of 21.80 seconds. The median was 21.135 seconds, with sample SD of 4.436 seconds and a range of 13.21-37.63 seconds. The 1,526.24-second sum is accumulated writing time, not an independently measured continuous queue duration.

The service-time-equivalent rate was calculated as 60 / 21.803 = 2.75 writing completions per minute. It describes hypothetical consecutive use of the measured writing time without additional gaps. It is not directly observed queue throughput because transition and waiting intervals were not supplied.

![Figure 4.10. Manual Paper Attendance Writing Duration](C:/Users/justine mendoza/source/repos/NFC_System/output/chapter4/Figure-Manual-Writing-Duration.png)

The distribution presents all 70 supplied writing durations. It does not include queue waiting or identity and enrollment validation.

### 4.3.5 Descriptive Comparison Addressing SOP 6

#### Average Processing Time per Student

The manual observation establishes mean active writing duration of 21.803 seconds. The automated exports establish internal system latency and recorded PIN/QR interaction duration, but not the complete NFC-tap-to-completed-verification duration defined in Chapter 3. Moreover, the manual test records event-attendance writing, whereas the analysed automated exports identify entry/exit verification without a specific event identifier. The datasets therefore differ in both task content and timing boundaries.

**Table 4.11. Timing Measurements and Their Boundaries**

| Method or stage | Available mean | Measurement boundary |
|---|---|---|
| Manual paper attendance | 21.803 s | First character to completion of required fields |
| Fast internal processing | 11.49 ms | Instrumented system components only |
| Standard PIN interaction | 2.505 s | Recorded PIN interaction stage |
| High-Security PIN plus QR interaction | 5.799 s | Recorded PIN and QR stages |
| Full automated processing time | Not established | NFC tap to completed verification |

These measurements may be described alongside one another, but they do not support a valid percentage reduction in complete processing time. No assumed 2.50-second baseline, old six-person manual benchmark, or old speed-improvement multiplier is retained. A matched comparison requires automated timing for the same attendance or verification task and an explicitly comparable start and finish boundary.

#### Students Processed per Minute

The manual service-time-equivalent rate is 2.75 completions per minute. The automated recorded-entry rates are 21.98, 7.46, and 4.95 intervals per minute for Fast, Standard, and High-Security. These rates arise from different denominators: summed manual writing times versus automated first-to-last timestamp intervals that include within-round pauses and invalid attempts. They are supplementary descriptive rates, not matched measures of overall throughput. No manual-versus-system throughput multiplier is claimed.

#### Human Error Proxy Attendance and Unverified Enrollment

Because the manual procedure contained no ID or enrollment-status check, all 70 recorded manual attendance-writing observations proceeded without that validation step. This corresponds to 70/70, or 100%, unchecked enrollment observations under the stated simulation procedure. It does not mean that all respondents were unenrolled or that 100% of entries were false. Student IDs written on paper were self-reported and were not independently validated in the timed procedure.

The automated prototype checks configured credential and student-record conditions and demonstrated rejection of deliberately invalid credentials and entry-state violations. However, its 20 reusable test profiles do not establish live university enrollment verification or unique identity validation for all 70 physical participants. The separate documented inactive/expelled tests show configured rule behavior, not the prevalence of enrollment violations in the observation sample.

No transcription-error audit, proxy-attendance challenge count, independently verified real-enrollment comparison, or pre-manual survey results were included in the supplied manual timing PDF. Consequently, those error and risk rates are not invented. SOP 6's risk comparison is addressed at the level of the observed validation procedure and demonstrated configured controls, with empirical error and proxy-attendance comparisons remaining incomplete.

### 4.3.6 Summary of Measured Results

**Table 4.12. Performance and Security Summary**

| Metric | Result | Scope |
|---|---|---|
| Automated average full processing time | Not established | Original NFC-tap-to-finish timing boundary not supplied |
| Internal latency Fast / Standard / High-Security | 11.49 / 57.37 / 54.06 ms | 70 successful entry trials per mode |
| Recorded Standard / High-Security interaction | 2.505 / 5.799 s | PIN/QR interaction components |
| Observed credential-decision accuracy | 100.00% (222/222) | Researcher-labelled applicable recorded decisions |
| Recorded transaction denial rate | 4.11% (18/438) | Includes reset exits and invalid-condition tests |
| Recorded PIN lockout rate | 1.32% (2/152) | Intentional triggers; individual captured PIN submissions |
| Mean manual writing duration | 21.803 s | 70 reported writing observations |
| Matched complete-task time reduction | Not established | Different tasks and timing boundaries |
| Directly observed complete-round throughput | Not established | No complete-round observation start and finish |
| Manual enrollment check omission | 100% (70/70) | No enrollment check in the stated manual procedure |
| Real-enrollment false acceptance / proxy attendance rate | Not established | No independent enrollment or proxy-attempt audit |

## 4.4 Respondent Demographic Profile

Reserved for the teammate's latest validated respondent data. The former demographic table is not reproduced because the researcher requested that the superseded ISO evaluation dataset be ignored for now. The 70 system participants, 70 manual observations, and any survey respondents must be distinguished; a shared or paired cohort is not assumed without confirmation.

## 4.5 User Evaluation

Reserved for the latest ISO evaluation dataset and respondent-level calculations. No previous survey means, SDs, overall ratings, or group comparisons are carried forward. Weighted means must be recalculated from the latest response frequencies, and SD must use the stated Chapter 3 definition. An overall respondent-score SD cannot be obtained by simply averaging item SDs.

## 4.6 Discussion

### 4.6.1 Interpretation of System Performance

The observations show that internal processing operated on a millisecond scale, while PIN and QR interaction occurred on a seconds scale. Fast had the shortest internal latency and recorded-entry spacing. The High-Security interaction distribution had a longer upper tail, illustrating the variation introduced by the observed QR stage. The exports do not establish the reasons for each longer interaction.

Standard's mean internal latency was slightly greater than High-Security's, so the data do not support a simple monotonic relationship between the number of credential stages and internal latency. The fixed mode order also prevents attributing differences in PIN speed solely to the mode. No claim of causal improvement or general campus capacity is made.

### 4.6.2 Interpretation of Manual and Automated Measurements

The manual baseline expands the documented observation from the previous small benchmark to 70 writing durations. It provides a reproducible estimate for the stated paper-attendance writing task. Its 21.803-second mean does not include verification, waiting, or later encoding, and its service-equivalent rate does not represent a measured continuous queue.

The automated log-derived rates and component timers address different portions of the workflow. They show the observed behavior of the prototype, but cannot be used to assert that it is a specified number of times faster than paper attendance. A fair complete-task benchmark must match the task outcome and the timing boundary. The previous improvement ratios are therefore removed from the discussion and summary.

### 4.6.3 Interpretation of Security Outcomes

The labelled applicable credential decisions agreed with the expected outcomes in the supplied recorded sample. Correctly rejected wrong PINs, triggered lockouts, and mismatched QR credentials contribute to observed decision correctness. Anti-tailgating and irregular-exit conditions provide separate state-rule evidence rather than ordinary authentication failures.

The 1.32% recorded PIN lockout rate reflects deliberate demonstrations, not an estimate that 1.32% of students would become locked out in ordinary use. Similarly, the 4.11% record-based denial proportion reflects the mixture of demonstrations, participant entries, and reset exits. The controlled sample does not establish resistance to all credential copying, identity impersonation, or real-enrollment risks.

### 4.6.4 Practical Advantages and Their Limits

The prototype provides configurable credential requirements, structured transaction logs, and visible rule-based decisions. These features reduce reliance on a paper-only record for the tested verification workflow and provide traceable outcomes for the observed invalid conditions. The manual simulation lacked an identity or enrollment check, highlighting a procedural omission rather than a measured prevalence of fraudulent attendance.

The software's monitoring, export, and local synchronization capabilities are practical implementation features. Claims about reduced transcription errors, universal reliability, or large-scale operational benefit require separate evidence and are not inferred from timing alone. The latest user evaluation remains pending and is not used to support acceptance conclusions in this revision.

### 4.6.5 Methodological Alignment and Remaining Requirements

Authentication-decision accuracy, record-based transaction denial, and recorded PIN lockout rate are now reported with explicit inclusion rules and researcher-confirmed expected conditions. Timing and throughput measures remain limited by the available observation boundaries. Listing all five Chapter 3 metric headings does not mean every original planned measurement has been fulfilled.

To fully satisfy the original automated processing-time and throughput definitions, the study needs end-to-end NFC-tap-to-outcome durations and a complete observation-period timer for a specified task. To fully answer SOP 6 comparatively, that task and its timing boundary must correspond to the manual task. The required Chapter 3 clarifications should describe the actual measurements and supplementary analyses transparently; they must not imply that missing timing data were collected.

The manual enrollment-check omission can be reported from the stated procedure, but empirical transcription-error, proxy-attendance, and real-enrollment acceptance rates require an independent audit or relevant test outcomes. The pending pre-manual survey and latest ISO data should be added only when their actual records and calculations are supplied.

Other limitations include a controlled prototype setting, one recorded device, 20 reusable test profiles, fixed mode order, non-blinded researcher classification of expected outcomes, and interspersed invalid-condition tests. The linkage between manual and system participants has not yet been confirmed, so the analysis remains descriptive rather than paired or causal.

## 4.7 Summary

The revised observation contains 438 system records, including 210 successful entry trials, 210 granted reset exits, and 18 expected invalid-condition denials. Internal latency averaged 11.49, 57.37, and 54.06 ms for Fast, Standard, and High-Security. Recorded interaction averaged 2.505 seconds in Standard and 5.799 seconds in High-Security.

Researcher-labelled applicable credential decisions yielded observed accuracy of 100.00% (222/222), while the overall recorded denial proportion was 4.11% (18/438). Two deliberately triggered lockouts among 152 captured PIN submissions yielded a recorded lockout rate of 1.32%. These are controlled recorded-sample findings, not universal accuracy or ordinary-user failure estimates.

The manual paper-attendance baseline contained 70 writing observations with mean duration of 21.803 seconds and sample SD of 4.436 seconds. The no-check procedure left all timed manual entries without ID or enrollment validation. A matched end-to-end performance comparison, independent risk-rate audit, and the latest user-evaluation results remain necessary before the chapter can be treated as a complete answer to all methodological commitments.
