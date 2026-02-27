## Product Requirements Document (PRD)

### Product name
Tech901 AI-Assisted ID Picture (WPF Kiosk App)

### Document status
- **Status**: Active
- **Last updated**: 2026-02-26
- **Primary stakeholders**: Tech901 staff (operators/admins), students (end users)

---

## Summary
Build a modern, responsive WPF kiosk application that guides a student through capturing a high-quality ID photo. The app detects when a person is in view, prompts for the student’s name using speech (TTS/STT), matches against a pre-loaded roster CSV (with a unique student ID), guides positioning, captures and reviews the photo, then intelligently crops (face/landmarks; center nose/face with padding) and saves the final image to a configured local folder using a configurable filename template derived from roster columns. After saving, that student is removed from the remaining roster to prevent duplicates.

An alternate workflow supports processing a directory of pre-taken photos using the same crop + naming + save rules.

---

## Problem statement
Collecting and preparing student photos for ingestion into a student information system (SIS) is slow and error-prone. Staff spend time on manual cropping, consistent naming, and rework due to poor framing. Students often need real-time guidance to achieve consistent results.

---

## Goals
- **Fast throughput**: Enable quick, repeatable capture and save of properly cropped ID photos.
- **High consistency**: Produce images with consistent framing, crop, and dimensions.
- **Low friction**: Self-serve experience with speech + on-screen guidance; minimal staff intervention.
- **Modern showcase**: A “cool”, modern WPF app that reflects Tech901 branding and high UX craft.
- **Roster-driven**: Import a roster from CSV (including unique student ID) and prevent duplicate processing.

### Success metrics (initial targets; refine during discovery)
- **Median time per student**: ≤ 45 seconds from detection to saved output (excluding retries).
- **Retake rate**: ≤ 15% (after lighting/positioning is tuned).
- **Match confirmation accuracy**: ≥ 98% confirmed correct (via confirmation step).
- **Output correctness**: 0% naming-template failures; 0% missing required fields.
- **Reliability**: App recovers gracefully from transient camera or network issues.

---

## Non-goals (v1)
- **Direct SIS write-back**: No direct integration into SIS databases in v1 (export-to-folder only).
- **Identity verification**: Not a security/biometric verification product; matching is roster-based with user confirmation.
- **Cross-platform**: Windows-only WPF application.
- **Photo editing suite**: No manual Photoshop-style editing; only guided capture + automated crop.

---

## Users & personas
- **Student (primary end user)**: Walks up to kiosk, follows voice/text prompts, confirms identity, confirms photo.
- **Staff admin/operator (secondary user)**: Imports roster CSV, configures outputs, monitors progress, handles exceptions.

---

## Operating assumptions & constraints
- **Environment**: Self-serve kiosk, basic USB webcam, Windows 10/11.
- **Roster source**: CSV represents the official roster for the session and includes a stable unique identifier (e.g., `StudentId`).
- **Speech**: Cloud-only speech is acceptable (Azure Speech). If speech is unavailable, the experience should still be usable with on-screen input (see requirements).
- **Face/landmarks**: Prefer Azure-based face landmark detection (or equivalent) to center on nose/face for crop.
- **Data handling**: Cloud processing is allowed if secured; final images saved locally (configurable folder).
- **Branding**: Must incorporate Tech901 branding assets in `images/branding`.

---

## UX / interaction principles (inspiration)
Use the work of Billy Hollis, Laurent Bugnion, Alan Cooper, Bret Victor, and John (Jared) Gossman as inspiration:
- **Direct manipulation**: Students immediately see the camera feed and what the system is “asking for”.
- **Calm, clear guidance**: Short instructions, progressive disclosure, minimal cognitive load.
- **Delight + polish**: Smooth transitions, modern typography, obvious affordances, responsive UI.
- **Error-tolerant flow**: Clear recovery paths, don’t dead-end students.

---

## Primary user journey (self-serve capture)
### End-to-end flow
1. **Idle attract mode**: UI shows Tech901-branded welcome screen and camera preview (optional) with “Step closer to begin”.
2. **Person detected**: When a person enters view, the app transitions into guided mode.
3. **Name capture (speech + UI fallback)**:
   - App asks for name (TTS).
   - Student responds (STT).
   - App displays recognized text and asks for confirmation or correction (speech and/or touch/keyboard).
4. **Roster match**:
   - App searches the imported roster for the best match(es).
   - If ambiguous, app asks a disambiguation question (speech) and/or presents a short list.
   - App confirms the selected student identity.
5. **Positioning guidance**:
   - App shows camera preview and overlays (e.g., face box, alignment guides).
   - App provides short corrective prompts (TTS + on-screen text): “Move closer”, “Center your face”, “Look at the camera”.
6. **Capture**:
   - App counts down and captures a photo.
7. **Review + retake**:
   - Student sees the captured image and can confirm or retake.
8. **Auto-crop + save**:
   - App computes face landmarks, crops to configured dimensions with padding.
   - App saves file to configured local folder and naming template.
9. **Roster update**:
   - The student is marked “completed” and removed from the remaining possibilities for this session.
10. **Return to idle**.

---

## Alternate workflow (batch process existing photos)
1. Admin selects an input directory of images.
2. App processes each image:
   - Detect face/landmarks, crop to configured output dimensions.
   - Prompt (or auto-match) the student identity for each image based on filename/metadata rules (TBD).
   - Save using the configured naming template.
3. Produces a processing report (success/failure per file).

---

## Functional requirements (FR)

### FR-001 — Person detection triggers the flow
- **Description**: The app detects a person entering the camera’s view and begins the guided workflow automatically.
- **Acceptance criteria**:
  - Detects presence within 1 second under normal lighting.
  - Does not trigger repeatedly while the same person remains in view (debounce / state machine).

### FR-002 — Roster import from CSV
- **Description**: Admin imports a CSV roster used to match students during the session.
- **Requirements**:
  - Must include a unique student key column (e.g., `StudentId`) and a display name (e.g., `FirstName`, `LastName`).
  - Allow mapping CSV headers to expected fields (if headers vary) (TBD if needed in v1).
- **Acceptance criteria**:
  - Invalid/missing required columns produce a clear error and prevent starting a session.

### FR-003 — Name capture using speech with UI fallback
- **Description**: App asks for student name via TTS and captures response via STT; UI shows the recognized text and allows correction.
- **Acceptance criteria**:
  - Student can confirm, retry speaking, or type/select using the UI.
  - If speech service is unavailable, the app automatically falls back to UI input without blocking the session.

### FR-004 — Matching and confirmation against roster
- **Description**: App matches the student’s stated name to the roster and confirms identity.
- **Behavior**:
  - If a single strong match exists, present it for confirmation.
  - If multiple plausible matches exist, ask a disambiguation question via speech (e.g., spelling last name or selecting from short list).
- **Acceptance criteria**:
  - Student identity is explicitly confirmed before saving any photo.

### FR-005 — Positioning guidance (visual + TTS)
- **Description**: While the camera preview is visible, provide guidance to reach a good pose/framing.
- **Acceptance criteria**:
  - On-screen text mirrors the spoken instruction.
  - Guidance avoids rapid “nagging” (rate limit prompts).

### FR-006 — Capture with review/retake
- **Description**: App captures a still image from the webcam and shows a review screen with “Use photo” or “Retake”.
- **Acceptance criteria**:
  - Retake returns to guidance mode with minimal friction.
  - “Use photo” proceeds to crop + save.

### FR-007 — Intelligent crop based on face landmarks (center nose/face)
- **Description**: Automatically crop the image so the face is centered (nose/face center) with configurable padding and output dimensions.
- **Acceptance criteria**:
  - Crop uses a face/landmark detection method (Azure Face/landmarks preferred).
  - If landmarks cannot be detected, falls back to center-crop strategy; retake is also available.

### FR-008 — Configurable output format, location, and filename template
- **Description**: Save the final image in a configured image format, in a configured local directory, with a filename template using CSV columns.
- **Examples**:
  - `{FirstName}_{LastName}.jpg`
  - `{StudentId}_{LastName}_{FirstName}.png`
- **Acceptance criteria**:
  - Template tokens correspond to CSV columns; unknown tokens cause validation errors before session starts.
  - Supports collision strategy (e.g., add numeric suffix) (TBD).

### FR-009 — Student completion removes them from remaining possibilities
- **Description**: After a successful save, the student should not appear as a match candidate again in the current session.
- **Acceptance criteria**:
  - A “completed” list is visible to admins (TBD UI) and persisted for the session.

### FR-010 — Admin controls and session management
- **Description**: Provide an admin mode to import roster, configure outputs, start/stop a session, and view progress.
- **Acceptance criteria**:
  - Admin mode is protected by PIN (9019), triggered via Ctrl+Shift+A or gear icon.

### FR-011 — Logging and basic reporting
- **Description**: Log key events (imports, matches, saves, failures) and produce a simple session report.
- **Acceptance criteria**:
  - Report includes counts (captured, saved, retakes, failures) and a failure list with reasons.

---

## Data & configuration

### Roster CSV (proposed minimum schema)
- **Required**: `StudentId`, `FirstName`, `LastName`
- **Optional**: `PreferredName`, `DOB`, `Email`, any other fields used for disambiguation or filename templates (stored in `ExtraFields` dictionary)

### Output configuration (implemented)
- Output directory: configurable local path (default: `./output`)
- Image format: `jpg` or `png` (configurable, default: `jpg`)
- Output dimensions: 600x600 (configurable via `OutputWidth`/`OutputHeight`)
- Crop padding: multiplier-based (default: 1.6x, configurable via `CropPaddingMultiplier`)
- Filename template: uses CSV columns (default: `{StudentId}_{LastName}_{FirstName}`)
- Camera selection: default camera via OpenCvSharp4 device index
- Speech voice: configurable (default: `en-US-GuyNeural`)

---

## External services & dependencies (preferred)
- **Azure Speech**: STT + TTS for kiosk prompts and name capture.
- **Face landmarks / detection**:
  - Preferred: Azure face landmark detection (or equivalent Azure service capable of returning landmarks).
  - Alternative: on-device detection (e.g., OpenCV) if needed later (not required for v1 given current constraints).

---

## Quality attributes (non-functional requirements)
- **Performance**:
  - Camera preview remains smooth (target 24–30 FPS on typical kiosk hardware).
  - UI remains responsive during cloud calls (async; no UI thread blocking).
- **Reliability**:
  - Graceful handling of camera disconnects and speech service timeouts.
  - Clear, actionable error states with recovery steps.
- **Accessibility**:
  - Large touch-friendly controls, high contrast mode option (TBD), captions for spoken prompts.
- **Privacy & security**:
  - Minimize cloud data sent: send only what’s needed for speech and landmarks.
  - Avoid storing audio unless explicitly enabled.
  - Protect roster data at rest (TBD: encryption/Windows DPAPI).

---

## Branding & UI requirements
- Use Tech901 colors/logos and visual identity from `images/branding`.
- Kiosk-first UI: large tap targets, minimal text, guided step-by-step screens.
- Camera preview prominently displayed with overlays during guidance.

---

## Risks & mitigations
- **Speech recognition errors**: Provide immediate on-screen confirmation and easy retry/typing fallback.
- **Lighting variability**: Add guidance prompts and (optionally) a “Lighting check” screen (TBD).
- **Face landmark failures**: Provide retake loop and define fallback behavior.
- **Ambiguous names**: Use roster fields for disambiguation (e.g., preferred name, DOB) and ask clarifying questions.

---

## Resolved decisions
- **Output dimensions**: 600x600, configurable via appsettings.json
- **Crop rules**: Face-aware crop centered on nose tip with 1.6x padding multiplier; center-crop fallback when no face detected
- **Admin access**: PIN-protected (9019) via Ctrl+Shift+A or gear icon
- **Image format**: JPEG (quality 95) or PNG, configurable
- **Speech fallback**: NullSpeechService when Azure Speech key not configured; UI input always available
- **Face detection fallback**: NullFaceDetectionService when Azure Face API not configured; center-crop used instead

## Open questions / decisions (to finalize)
- What is the desired **collision policy** for filenames (overwrite vs suffix vs block)?
- Do we need **offline mode** beyond UI input (i.e., fully offline landmarks), or is “speech cloud-only” sufficient while landmarks remain cloud-backed?
- Should images be saved **immediately** upon confirmation, or queued for later save/retry if IO fails?

