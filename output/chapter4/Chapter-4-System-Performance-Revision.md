# Chapter 4 System Metrics and Performance Revision

This draft replaces the system-observation portions of Sections 4.3.2 and 4.3.3. Final table and figure numbering should follow the thesis sequence. The new manual observation and pre-manual survey are intentionally not analysed here.

## 4.3.2 System Metrics and Performance

### Observation Procedure and Record Selection

The system observation was conducted in two batches on August 12 and 13, 2026, involving 40 and 30 participants, respectively. According to the observation protocol, the same 70 participants tested Fast, Standard, and High-Security modes in that fixed order. This produced 70 successful entry trials per mode, or 210 successful entry trials across the three modes. The number of trials should not be interpreted as 210 distinct participants.

Twenty physical NFC cards, preconfigured with PIN and QR credentials, were reused during the observation. For each mode, the first batch completed two rounds of 20 entries, while the second batch completed rounds of 20 and 10 entries. Between entry rounds, the research team performed exit transactions to reset the card profiles for reuse. Explanations of the succeeding mode were provided between mode changes. Consequently, team-assisted reset exits were retained for record reconciliation but excluded from the participant entry timing summaries.

**Table A. Reconciliation of Exported System Records**

| Mode | Batch 1 entries | Batch 2 entries | Successful entries | Granted reset exits | Denied records | Total records |
|---|---:|---:|---:|---:|---:|---:|
| Fast | 40 | 30 | 70 | 70 | 2 | 142 |
| Standard | 40 | 30 | 70 | 70 | 12 | 152 |
| High-Security | 40 | 30 | 70 | 70 | 4 | 144 |
| Total | 120 | 90 | 210 | 210 | 18 | 438 |

The two exports contained 438 records: 210 successful entries, 210 granted reset exits, and 18 denied records. The primary timing analysis included all records marked Entry and GRANTED, with no removal of slow successful observations. Denied records were described separately because the sessions included demonstrations of error conditions. Their classification as intentional demonstrations or ordinary participant mistakes requires confirmation from the observation notes. Repeated student-profile names were not treated as duplicate participant trials because the same 20 card profiles were reused. No exact duplicate source rows were found.

### Internal System Latency

Internal system latency was analysed using the exported Total System Latency field. Database-query time was reported separately using Total DB Query Time. These measurements describe the instrumented processing components, not the complete duration from a participant approaching the reader to completing physical entry. Database-query time overlaps the internal processing measurements and should not be added to total system latency.

**Table B. Internal Latency for Successful Entry Trials**

| Mode | n | Mean system latency (ms) | Median (ms) | Sample SD (ms) | Minimum (ms) | Maximum (ms) | Mean DB time (ms) |
|---|---:|---:|---:|---:|---:|---:|---:|
| Fast | 70 | 11.49 | 11.30 | 2.20 | 8.26 | 26.36 | 2.88 |
| Standard | 70 | 57.37 | 50.66 | 17.46 | 44.55 | 137.47 | 47.95 |
| High-Security | 70 | 54.06 | 52.64 | 6.34 | 49.44 | 86.36 | 44.90 |

Fast mode had the lowest mean internal latency at 11.49 ms. Standard and High-Security modes recorded means of 57.37 ms and 54.06 ms, respectively. High-Security mode did not exhibit a higher mean internal latency than Standard mode in these observations. Therefore, the results do not support a claim that each additional verification factor necessarily increases internal processing time. Standard mode also exhibited greater dispersion, as indicated by its sample standard deviation of 17.46 ms and maximum of 137.47 ms. These findings describe the observed sessions and do not establish causal differences between modes.

### Recorded User Interaction Duration

The exported user-workflow fields measure the recorded PIN and QR interaction stages. They are reported in seconds after conversion from milliseconds. They do not include every portion of the complete physical verification process. Fast mode bypasses PIN and QR verification; its user-workflow values are therefore reported as not applicable rather than as zero-second transactions.

**Table C. Recorded PIN and QR Interaction Durations**

| Mode | Successful trials | Mean PIN duration (s) | Mean QR duration (s) | Mean total interaction (s) | Median total (s) | Sample SD (s) | Maximum total (s) |
|---|---:|---:|---:|---:|---:|---:|---:|
| Fast | 70 | N/A | N/A | N/A | N/A | N/A | N/A |
| Standard | 70 | 2.505 | N/A | 2.505 | 2.035 | 1.152 | 6.875 |
| High-Security | 70 | 2.162 | 3.637 | 5.799 | 4.348 | 3.878 | 22.604 |

Standard mode recorded a mean PIN interaction duration of 2.505 seconds. High-Security mode recorded a mean PIN duration of 2.162 seconds and a mean QR duration of 3.637 seconds, resulting in a mean combined interaction duration of 5.799 seconds. The High-Security median of 4.348 seconds was lower than its mean, while its maximum reached 22.604 seconds. This indicates that several longer interactions increased the mean. The QR stage displayed greater variability than the PIN stage, but the exports alone cannot establish the cause of the longer QR interactions. All slow successful trials were retained.

The lower mean PIN duration in High-Security mode should not be interpreted as proof that this mode improves PIN performance. Because participants always encountered Standard mode before High-Security mode, prior practice and other order effects may have influenced the observations.

**Figure A. Successful-entry timing by verification mode.** Panel A presents mean internal system latency with sample-standard-deviation error bars. Panel B presents the distribution of recorded PIN/QR interaction durations, including all successful observations. Fast-mode interaction duration is not plotted because PIN and QR were bypassed.

### Recorded-Entry Spacing and Rate

No separate stopwatch or video timing of complete automated transactions was available. An additional descriptive measure was therefore calculated from the recorded timestamps within each entry round. For each round, the elapsed span was the difference between its first and last successful entry timestamps. A round containing n successful entries contributes n - 1 observed between-entry intervals. The first successful entry has no observed preceding interval within that span.

For each mode, the four entry rounds contained 20, 20, 20, and 10 successful entries. These yielded 19 + 19 + 19 + 9 = 66 observed intervals. Pooled mean spacing and recorded-entry rate were calculated as follows:

Mean recorded-entry spacing (s) = sum of within-round elapsed spans / sum of observed intervals.

Recorded-entry rate (intervals/min) = 60 x sum of observed intervals / sum of within-round elapsed spans.

**Table D. Recorded-Entry Spacing and Rate**

| Mode | Entry rounds | Observed intervals | Summed spans (s) | Mean spacing (s) | Recorded-entry rate (/min) | Denied records within spans |
|---|---:|---:|---:|---:|---:|---:|
| Fast | 4 | 66 | 180.200 | 2.730 | 21.98 | 2 |
| Standard | 4 | 66 | 531.167 | 8.048 | 7.46 | 8 |
| High-Security | 4 | 66 | 800.508 | 12.129 | 4.95 | 3 |

Fast mode exhibited the shortest mean spacing and highest recorded-entry rate. Standard and High-Security modes exhibited longer spacing under the observed conditions. These rates exclude team-assisted reset intervals and gaps between entry rounds. However, denied records, demonstrations, and any pauses occurring within an entry round remain part of that round's elapsed span. Thirteen denied records occurred within the selected entry spans. Removing their rows would not remove the time they occupied.

Accordingly, these values should be labelled recorded-entry rates rather than directly measured full-transaction throughput or uninterrupted gate capacity. They are not calculated by dividing 70 by the summed first-to-last spans, which would count an unobserved interval at the beginning of each round. The previously assumed 2.50-second physical-interaction baseline is not used because it was not directly measured in these exports.

**Figure B. Recorded successful-entry rate.** The rates are derived from 66 observed intervals per mode across four entry rounds. Within-round denials and pauses remain included; the figure does not represent stopwatch-measured complete transaction capacity.

## 4.3.3 Logged Verification Outcomes and Data Limitations

### Denied Records and Security Conditions

The exported records included 18 denials in addition to the successful entry and reset-exit records. Seventeen denied records were marked Entry and one was marked Exit. Their logged conditions are presented below.

**Table E. Distribution of Logged Denial Conditions**

| Logged condition | Number of records |
|---|---:|
| PIN_FAILURE | 8 |
| ANTI_TAILGATING_VIOLATION | 5 |
| PIN_LOCKED | 2 |
| QR_REPLACED_OR_MISMATCH | 2 |
| IRREGULAR_EXIT_SEQUENCE | 1 |
| Total | 18 |

The records show that the system logged these denial conditions during the observation sessions. Because the sessions also included demonstrations of invalid scenarios, the 18 denials should not automatically be interpreted as failures affecting ordinary participants. Conversely, the 210 selected successful entry trials should not be reported as 100% authentication accuracy, because the timing dataset was deliberately restricted to successful entries.

Authentication accuracy, false-acceptance rate, and false-rejection rate require independently labelled expected outcomes for valid and invalid attempts. The supplied exports contain system verdicts but do not provide that ground truth for every attempt. A defensible ordinary-user failure rate also requires separating demonstration attempts from genuine participant attempts and accounting for any incomplete or unlogged attempts. Consequently, no authentication-accuracy, natural-failure, or PIN-lockout-rate percentage is claimed from these files alone. The two PIN_LOCKED records are reported as logged records, not automatically as a count of unique lockout episodes.

### Data Integrity and Interpretation

All 438 source records were retained in the analysis workbook with their source batch, worksheet, and Excel row. Successful-entry component totals reconciled with their reported system and user totals. Two denied PIN_LOCKED records contained nonzero total PIN-related time while their component PIN fields were marked N/A in the original exports. These rows were flagged rather than altered or interpreted as zero-duration interactions.

The exported device identifier and device name indicate that the observations were recorded on one device, Admin Office - 1. The results should therefore be interpreted as a descriptive assessment of these sessions, not as evidence of simultaneous multi-kiosk performance or campus-wide capacity. The reuse of 20 card profiles also means that unique participant identity and paired participant-level comparisons cannot be reconstructed from profile names alone.

The absence of a particular logged failure code does not prove that no hardware error, missed scan, or cancelled workflow occurred. The exports only establish the outcomes that were recorded. Claims of zero hardware failure require an independent observation log or complete instrumentation of unsuccessful and incomplete scans.

### Scope of the SOP 6 Comparison

This analysis supplies the automated-system portion of the evidence for SOP 6. The revised manual observation and pre-manual survey will be incorporated separately when available. Until those data are analysed, no revised manual-versus-system improvement ratio or conclusion about reduced unverified enrollment is presented. The automated exports identify entry/exit verification transactions but do not identify a particular event; they should not be presented as separately measured event-attendance recording performance without supporting evidence.

The eventual manual comparison must use compatible tasks, timing boundaries, units, and participant denominators. Recorded PIN/QR interaction durations should not be compared directly with full paper-attendance completion times as though both represented the same complete process.

## Replacement and Consistency Checklist

- Replace the former system database averages, latency averages, interaction durations, and derived throughput values with Tables B-D.
- Remove the assumed 2.50-second physical baseline and the throughput values derived from it.
- Do not retain the previous 9.38x, 2.71x, and 1.66x improvement ratios as findings of this new observation.
- Replace the former 95-attempt, 84-grant, 11-denial summary with the reconciled 438-record classification in Table A. Do not call 438 records 438 participant attempts.
- Replace the former 96.15% accuracy and zero-PIN-lockout statements with the qualified outcome discussion above.
- Preserve the separate survey/ISO evaluation dataset unless it is independently being revised; 210 entry trials do not change survey respondent counts.
- Align Chapter 3's operational metric definitions with the exported measurements: internal latency, recorded interaction duration, and recorded-entry spacing are distinct measures.
- Keep the new manual comparison pending until the teammate's observation data and method are supplied.

## Audit Appendix: Selected Entry Runs

| Batch | Mode | Round | Entries | First grant | Last grant | Span (s) | Denials within span |
|---|---|---:|---:|---|---|---:|---:|
| Aug 12 | Fast | 1 | 20 | 11:32:31.629 | 11:33:22.878 | 51.249 | 0 |
| Aug 12 | Fast | 2 | 20 | 11:34:44.085 | 11:35:44.996 | 60.911 | 0 |
| Aug 12 | Standard | 1 | 20 | 11:38:19.061 | 11:40:53.279 | 154.218 | 0 |
| Aug 12 | Standard | 2 | 20 | 11:42:08.075 | 11:44:56.243 | 168.168 | 4 |
| Aug 12 | High-Security | 1 | 20 | 11:46:17.454 | 11:50:17.183 | 239.729 | 1 |
| Aug 12 | High-Security | 2 | 20 | 11:51:35.764 | 11:54:50.435 | 194.671 | 0 |
| Aug 13 | Fast | 1 | 20 | 10:34:16.684 | 10:35:06.821 | 50.137 | 2 |
| Aug 13 | Fast | 2 | 10 | 10:36:13.727 | 10:36:31.630 | 17.903 | 0 |
| Aug 13 | Standard | 1 | 20 | 10:37:34.499 | 10:39:58.174 | 143.675 | 4 |
| Aug 13 | Standard | 2 | 10 | 10:41:16.973 | 10:42:22.079 | 65.106 | 0 |
| Aug 13 | High-Security | 1 | 20 | 10:43:51.242 | 10:48:19.412 | 268.170 | 1 |
| Aug 13 | High-Security | 2 | 10 | 10:49:28.659 | 10:51:06.597 | 97.938 | 1 |
