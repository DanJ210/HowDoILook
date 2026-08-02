# Recommendation Telemetry Research Brief

Use this brief to investigate which existing research, datasets, and modeling methods could help HowDoILook learn which hairstyles are preferred for different face and hair telemetry profiles.

## Product Context

A user uploads one portrait. The system analyzes it, automatically selects its current best hairstyle recommendation, and generates the primary preview. The user does not choose a requested hairstyle.

During controlled experiments, the system may generate additional eligible styles and ask for optional pairwise feedback after delivering the primary result. These comparisons produce learning labels for future ranking models.

The current ranker is heuristic. The examples below are synthetic schema examples, not validated claims that a style suits a telemetry profile.

## Current Telemetry Available

The worker currently emits schema-v2 telemetry containing:

- Image and detection:
  - `imageInfo.width`
  - `imageInfo.height`
  - `faceDetection.faceCount`
  - `faceDetection.primaryFace.{x,y,width,height,confidence}`
- Image quality:
  - `quality.brightness`
  - `quality.contrast`
  - `quality.blurScore`
  - `quality.centerOffset`
- Landmark and geometry:
  - `landmarks.landmarkConfidence`
  - `landmarks.yaw`
  - `landmarks.pitch`
  - `landmarks.jawWidthRatio`
  - `landmarks.foreheadHeightRatio`
  - `landmarks.faceElongation`
- Region estimates:
  - `segmentation.hairDensityEstimate`
  - `segmentation.beardDensityEstimate`
- Derived/context fields:
  - `faceShape`
  - overall `analysisConfidence`
  - telemetry source and schema version
  - detector, landmark, and region model versions

Prefer continuous measurements over `faceShape` as model inputs. `faceShape` is a derived bucket and can lose information near category boundaries.

## Proposed Style Catalog Features

A model needs stable descriptions of the styles it ranks. Add versioned, human-reviewed attributes to each style rather than forcing the model to learn only from opaque style IDs.

Example attributes:

```json
{
  "styleId": "layered-lob-v1",
  "catalogVersion": "2026-08-01",
  "displayName": "Layered Lob",
  "attributes": {
    "length": "medium",
    "fringe": "none",
    "part": "center",
    "topVolume": 0.45,
    "sideVolume": 0.65,
    "verticalEmphasis": 0.30,
    "horizontalEmphasis": 0.70,
    "foreheadCoverage": 0.10,
    "jawlineCoverage": 0.55,
    "texture": "soft-waves",
    "symmetry": "symmetric",
    "maintenance": "medium"
  },
  "generationTemplateVersion": "layered-lob-v1"
}
```

The numerical values above are illustrative normalized descriptors, not research conclusions.

## Proposed Exposure Record

Store one immutable exposure record for every recommendation session, including sessions with no feedback.

```json
{
  "exposureId": "exp_01JEXAMPLE",
  "analysisJobId": "00000000-0000-0000-0000-000000000101",
  "anonymousUserKey": "stable-one-way-user-key",
  "occurredAtUtc": "2026-08-01T18:20:00Z",
  "telemetry": {
    "source": "worker-v1-staged-analysis",
    "schemaVersion": 2,
    "faceDetectionConfidence": 0.96,
    "landmarkConfidence": 0.88,
    "yaw": 0.03,
    "pitch": -0.02,
    "jawWidthRatio": 0.82,
    "foreheadHeightRatio": 0.61,
    "faceElongation": 0.54,
    "hairDensityEstimate": 0.73,
    "beardDensityEstimate": 0.0,
    "analysisConfidence": 0.90,
    "brightness": 0.53,
    "contrast": 0.18,
    "blurScore": 0.24,
    "centerOffset": 0.04,
    "derivedFaceShape": "Oval"
  },
  "decision": {
    "rankerType": "heuristic",
    "rankerVersion": "style-mapper-v1",
    "catalogVersion": "2026-08-01",
    "systemSelectedStyleId": "layered-lob-v1",
    "primaryGenerationJobId": "00000000-0000-0000-0000-000000000201"
  },
  "eligibleCandidates": [
    {
      "styleId": "layered-lob-v1",
      "rank": 1,
      "rankingScore": 0.81
    },
    {
      "styleId": "side-swept-pixie-v1",
      "rank": 2,
      "rankingScore": 0.76
    },
    {
      "styleId": "soft-curtain-bangs-v1",
      "rank": 3,
      "rankingScore": 0.72
    },
    {
      "styleId": "textured-bob-v1",
      "rank": 4,
      "rankingScore": 0.69
    }
  ],
  "displayedCandidates": [
    {
      "styleId": "layered-lob-v1",
      "generationJobId": "00000000-0000-0000-0000-000000000201",
      "role": "primary",
      "selectionProbability": 1.0,
      "shownPosition": 2,
      "generation": {
        "model": "flux-kontext-apps/change-haircut",
        "modelVersion": "resolved-version-id",
        "templateVersion": "layered-lob-v1",
        "status": "Succeeded",
        "qualityOutcome": "valid"
      }
    },
    {
      "styleId": "textured-bob-v1",
      "generationJobId": "00000000-0000-0000-0000-000000000202",
      "role": "experimental",
      "selectionProbability": 0.25,
      "shownPosition": 1,
      "generation": {
        "model": "flux-kontext-apps/change-haircut",
        "modelVersion": "resolved-version-id",
        "templateVersion": "textured-bob-v1",
        "status": "Succeeded",
        "qualityOutcome": "valid"
      }
    }
  ],
  "feedback": {
    "comparisonType": "pairwise",
    "leftGenerationJobId": "00000000-0000-0000-0000-000000000202",
    "rightGenerationJobId": "00000000-0000-0000-0000-000000000201",
    "outcome": "left_preferred",
    "submittedAtUtc": "2026-08-01T18:22:10Z"
  }
}
```

Allowed explicit pairwise outcomes should include:

- `left_preferred`
- `right_preferred`
- `neither`
- `no_preference`

No feedback should be stored as `feedback: null`, not as a loss for either candidate.

`selectionProbability` means the probability that the experiment policy selected that candidate from the eligible pool under the recorded policy version. It is not the heuristic ranking score.

## Example Pairwise Training Row

A learning-to-rank dataset can flatten the exposure into one row per explicit comparison while retaining the full context.

```json
{
  "comparisonId": "cmp_01JEXAMPLE",
  "analysisJobId": "00000000-0000-0000-0000-000000000101",
  "telemetrySchemaVersion": 2,
  "rankerVersion": "style-mapper-v1",
  "catalogVersion": "2026-08-01",
  "jawWidthRatio": 0.82,
  "foreheadHeightRatio": 0.61,
  "faceElongation": 0.54,
  "hairDensityEstimate": 0.73,
  "analysisConfidence": 0.90,
  "yaw": 0.03,
  "pitch": -0.02,
  "leftStyleId": "textured-bob-v1",
  "leftSelectionProbability": 0.25,
  "leftGenerationQuality": "valid",
  "rightStyleId": "layered-lob-v1",
  "rightSelectionProbability": 1.0,
  "rightGenerationQuality": "valid",
  "shownOrderRandomized": true,
  "outcome": "left_preferred"
}
```

For model training, join each style ID to its versioned style attributes. Exclude comparisons with failed or invalid generations from hairstyle-preference labels, but retain them in generation-reliability data.

## Research Questions for Copilot

Investigate existing research and datasets that could inform telemetry features, style attributes, priors, evaluation design, or safety constraints. Do not assume common beauty or salon advice is scientifically validated.

1. Is there peer-reviewed evidence connecting continuous facial geometry measurements to perceived suitability of hairstyle properties such as fringe, length, top volume, side volume, part direction, or jawline coverage?
2. What evidence supports or contradicts common face-shape-to-hairstyle rules? Identify whether studies measure preference, attractiveness, professional judgment, or only repeat conventional guidance.
3. Are there public datasets containing the same person with multiple hairstyles and preference, attractiveness, or comparative annotations?
4. Are there datasets for hair segmentation, hairstyle classification, hair length/texture, face landmarks, or synthetic hairstyle transfer that can improve measurement or catalog attributes without supplying preference labels?
5. What pairwise-preference or learning-to-rank methods work well with contextual continuous features, sparse style exposure, and selection propensities?
6. Which contextual-bandit or off-policy evaluation methods are appropriate when the primary result is deterministic but experimental challengers are randomized?
7. How should inverse propensity weighting, doubly robust estimation, calibration, uncertainty, and out-of-distribution detection be applied here?
8. What sample-size or power-analysis methods can determine whether a telemetry region/style pair has enough observations to evaluate?
9. What fairness, demographic coverage, privacy, and consent risks apply to face-derived telemetry and aesthetic preference labels?
10. Which claims or features should be avoided because they infer sensitive traits, encode cultural beauty standards as universal truth, or lack reproducible evidence?

## Required Research Output

Return an evidence matrix with one row per source:

| Field | Required content |
|---|---|
| Citation | Title, authors, year, venue, stable URL/DOI |
| Source type | Peer-reviewed study, benchmark dataset, preprint, technical report, or practitioner guidance |
| Population/data | Sample size, demographics/geography if reported, image source, and annotation process |
| Inputs | Facial, hair, image, or context features used |
| Outcomes | Preference, pairwise choice, attractiveness, expert suitability, segmentation accuracy, etc. |
| Method | Study design or modeling method |
| Finding | Direction and effect size when available |
| Relevance | Exact way it could inform this project |
| Limitations | Bias, confounding, small sample, weak labels, unavailable data, licensing, or noncausal design |
| Evidence strength | High, medium, low, or unsupported, with justification |
| Recommended action | Use as prior, use for feature design, use for evaluation only, investigate further, or do not use |

Also return:

- A list of candidate public datasets with license and commercial-use constraints.
- A mapping from supported research variables to the current telemetry fields above.
- Proposed new telemetry or style attributes, clearly separated into evidence-backed and exploratory fields.
- A list of common hairstyle rules for which no reliable evidence was found.
- A recommended baseline model and offline evaluation protocol.
- Coverage and uncertainty criteria for retaining the heuristic fallback.

## Research Standards

- Prefer peer-reviewed and primary sources.
- Verify that cited sources exist and that URLs/DOIs resolve.
- Quote no more than necessary; summarize findings.
- Separate association from causation.
- Do not generalize a result beyond the study population.
- Do not present face-shape categories as biological ground truth.
- Do not infer race, ethnicity, health, sexuality, or other sensitive traits from portraits.
- Treat attractiveness and style preference as subjective, culturally dependent outcomes.
- Flag datasets that lack consent, clear provenance, or suitable licensing.
- State explicitly when evidence is weak or absent.
