# Subtitle Alignment Debugging Notes

## Scope

This document records the alignment failures found during real-output review, the root causes behind them, and the staged fixes that were applied so the pipeline does not just trade one failure mode for another.

## Final audit baseline

The final audit used a fresh full rerun against local samples `dubbing\1..7`, written to a temporary session folder instead of the repository `output\` tree.

Observed non-benign quality flags in that rerun:

| Flag | Count | Meaning |
| --- | ---: | --- |
| `targeted_subtitle_timing_repair` | 34 | Segment kept its chosen word window but timing was repaired toward subtitle timing because the edge evidence was weak. |
| `edge_word_low_confidence` | 29 | Boundary words exist, but ASR confidence is weak enough that subtitle timing is safer. |
| `alignment_risk` | 24 | Text/timing evidence is ambiguous and should stay reviewable. |
| `neighbor_boundary_drift` | 12 | A neighbor boundary is still tight or fragile enough to keep visible in review. |
| `word_duration_outlier` | 6 | Boundary word duration looks suspicious and can distort the segment edge. |
| `subtitle_start_shifted` | 4 | Final segment start still differs noticeably from the subtitle start. |
| `subtitle_end_shifted` | 3 | Final segment end still differs noticeably from the subtitle end. |
| `alignment_failed` + `subtitle_timing_fallback` | 3 | ASR could not provide a trustworthy word window, so the segment falls back to subtitle timing directly. |
| `low_alignment_confidence` | 2 | Token similarity is weak enough to require manual attention. |

The important point is that the remaining flagged segments are now explicit review cases, not silent text corruption.

## Root causes found in real outputs

### 1. SRT text was not authoritative

The pipeline could align timing from SRT, then still replace the final segment text with ASR text when the strings looked "close enough". That produced outputs where the source SRT was correct but the final segment text was wrong.

**Fix:** in SRT mode, the final exported segment text now always comes from the subtitle span. ASR is used for timing only.

### 2. Local greedy alignment stole words from neighboring segments

The old aligner chose each segment independently. In repeated or similar phrases, one segment could take an extra leading or trailing word and force the next segment to start from the wrong place.

**Example:** sample 3 segment 20 previously consumed the next segment's boundary word. The corrected window is now `[140,147]`, which keeps segment 21 independent.

**Fix:** SRT spans now build multiple candidates first, then a global monotonic path selector chooses the best non-overlapping sequence across neighboring segments.

### 3. Candidate search windows were too narrow for short or repeated phrases

Even with global selection, the correct answer can never be chosen if the candidate generator fails to include it. This showed up in short fragments such as `here comes ...` / `everyone!`.

**Fix:** SRT candidate collection now supports asymmetric backtracking, with extra reach for short spans so earlier boundary words are not excluded before scoring starts.

### 4. Correct word windows could still produce clipped audio edges

Some outputs had the right words but cut too tightly at the boundary, swallowing trailing sounds such as final `s` or the end of `playground`.

**Fix:** after word-window selection, SRT-backed segments now apply a safer timing policy:

1. high-risk short segments can fall back to subtitle timing;
2. otherwise, boundaries relax toward subtitle timing when there is safe gap;
3. final flags are refreshed after repair so the exported diagnostics match the final timings.

### 5. ASR orthography differences created fake boundary mismatches

Differences such as curly apostrophes or light punctuation changes were being treated as boundary mismatches even when the spoken word was effectively the same.

**Fix:** boundary comparisons now normalize through token signatures instead of raw token text, which reduces false alarms such as `Miller's` vs `Millers`.

### 6. Some segments are genuinely unresolvable from ASR words alone

Short exclamations, onomatopoeia, or badly recognized words can have no trustworthy ASR token window at all.

**Examples from the final rerun:** `Snow!`, `CLOP, CLOP, CLOP.`, and `BAA, BAA, BAA.`.

**Fix:** these segments now fail loudly into `subtitle_timing_fallback` instead of pretending to have precise ASR-driven boundaries.

## Why this is not a "fix B, break A" patch set

The stabilizing changes are layered by responsibility instead of mixing concerns:

1. **Text authority** and **timing alignment** are separated. SRT mode no longer lets ASR rewrite content.
2. **Candidate generation** is widened first, then **path selection** is solved globally. This prevents local boundary theft.
3. **Timing repair** only activates on explicit risk signals such as low-confidence edges, suspicious word durations, failed alignment, or fragile short segments.
4. **Boundary quality now checks the matched ASR word window itself**, not the already-corrected SRT segment text. This prevents wrong ASR edge words such as `Whoa` / `Michelle` from passing as a "strong" boundary match.
5. **Boundary relaxation** only uses safe subtitle gaps, so stronger ASR matches keep their word-driven precision.
6. **Quality flags stay visible** after repair, so risky segments remain auditable instead of silently "looking fixed".

Because of that separation, fixing one class of defect no longer requires weakening every segment globally.

## Incremental fixes applied

1. Made SRT text authoritative in final segments.
2. Replaced per-segment greedy choice with global monotonic candidate selection for SRT spans.
3. Added SRT selection bonuses for complete boundary-preserving matches.
4. Expanded candidate search with asymmetric backtracking for short spans.
5. Normalized token comparison with signature-based matching.
6. Switched boundary-match checks to the actual matched ASR word window instead of the final SRT text.
7. Added targeted subtitle timing repair for high-risk segments.
8. Added safe subtitle-boundary relaxation to prevent clipped edges.
9. Refreshed exported quality flags after repair so diagnostics describe final timings instead of stale pre-repair state.

## Final status by sample set

- **Sample 3:** the segment 20/21 boundary-stealing failure is fixed by the global path selector.
- **Sample 4:** short repeated phrases now keep the missing prefix words, and clipped tail sounds are reduced by timing repair plus safe boundary relaxation.
- **Samples 5-7:** remaining hard cases are mostly real ASR weakness; these now surface as `targeted_subtitle_timing_repair` or `subtitle_timing_fallback` instead of exporting silent misalignment.

## Practical review guidance

- Treat `alignment_failed` and `subtitle_timing_fallback` as expected subtitle-timed segments, not precise word-aligned ones.
- Treat `neighbor_boundary_drift`, `subtitle_start_shifted`, and `subtitle_end_shifted` as "review this edge" diagnostics.
- If a future tuning change improves one sample, rerun the full local corpus and compare the flag distribution rather than validating against one segment in isolation.
