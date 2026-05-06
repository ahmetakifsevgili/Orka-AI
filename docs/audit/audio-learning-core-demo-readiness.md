# Audio Learning Core Demo Readiness

## Summary

Orka has an existing audio learning core. It is demo-safe when described as AI Sesli Ders / AI Audio Lesson. It is not a live Zoom-like classroom, not WebRTC, and not microphone/STT.

## Backend Surfaces

| Area | File / Endpoint | Status | Notes |
|---|---|---|---|
| Audio Overview | `Orka.API/Controllers/AudioController.cs` | ALREADY_WORKING | Creates topic/session audio overview jobs. |
| Job status | `GET /api/audio/overview/{jobId}` | ALREADY_WORKING | Exposes status/script/speakers/fallback metadata. |
| Audio stream | `GET /api/audio/overview/{jobId}/stream` | DEMO_READY_WITH_POLISH | Streams MP3 when EdgeTTS produces audio. |
| Classroom session | `POST /api/classroom/session` | ALREADY_WORKING | Starts a contextual classroom session. |
| Classroom ask | `POST /api/classroom/{id}/ask` | ALREADY_WORKING | User can ask about active segment. |
| Interaction audio | `GET /api/classroom/interaction/{interactionId}/audio` | SCRIPT_ONLY_FALLBACK | Returns MP3 if generated; otherwise frontend browser TTS can continue. |
| EdgeTTS | `Orka.Infrastructure/Services/EdgeTtsService.cs` | SCRIPT_ONLY_FALLBACK | Uses Turkish voices; failures do not break script flow. |
| Learning signals | `ClassroomStarted`, `ClassroomQuestionAsked` | ALREADY_WORKING | Classroom interactions feed the learning loop. |

## Frontend Surfaces

| Area | File | Status | Notes |
|---|---|---|---|
| Listen button | `ChatMessage.tsx` | DEMO_READY_WITH_POLISH | Exposes "Sesli dinle" on assistant messages. |
| Audio player | `ClassroomAudioPlayer.tsx` | DEMO_READY_WITH_POLISH | Parses HOCA/ASISTAN/KONUK and plays via browser TTS fallback. |
| Ask segment | `ClassroomAudioPlayer.tsx` | DEMO_READY_WITH_POLISH | Can ask the backend about the active segment. |
| Localization | `ClassroomAudioPlayer.tsx` | DEMO_READY_WITH_POLISH | Labels now use the shared i18n foundation. |

## Phase UX Polish

- The audio player now says `AI Sesli Ders` / localized equivalent instead of framing the feature as a live classroom.
- Speaker labels are mapped through UI localization while preserving backend script tags.
- Browser TTS fallback remains intact.
- No microphone, STT, WebRTC, or live-room claim was added.

## Honest Demo Language

Safe:

- "Orka can turn learning content into an AI-supported audio lesson."
- "It uses a teacher-assistant explanation format."
- "If MP3 generation is unavailable, script/browser TTS fallback keeps the learning flow alive."

Do not claim:

- Live human classroom.
- Zoom/WebRTC room.
- Microphone conversation.
- STT/pronunciation checker.
- 3D voice classroom.

## Remaining Notes

| Note | Classification |
|---|---|
| Multilingual backend TTS voice coverage is not fully proven. | AUDIO_ROADMAP |
| STT/microphone input is not implemented. | AUDIO_ROADMAP |
| 3D classroom belongs to later V3 work. | PRODUCT_ROADMAP |
