# Layer gesture timing sweep

The timing differential treats the first `Space` down as t=10ms, then varies three gaps:

- `pressGapMs`: `Space down -> NonConvert down`
- `overlapMs`: `NonConvert down -> first release`
- `releaseGapMs`: first release -> final release

For `Space -> NonConvert -> NonConvert up -> Space up`, Space is held for `pressGapMs + overlapMs + releaseGapMs` and NonConvert is held for `overlapMs`.

For `Space -> NonConvert -> Space up -> NonConvert up`, Space is held for `pressGapMs + overlapMs` and NonConvert is held for `overlapMs + releaseGapMs`.

The matrix crosses 39/40/41ms independently in all three phases and also tests 100ms/500ms delays independently in all three phases. This keeps threshold failures attributable to press-gap, overlap, or final-release timing instead of conflating multiple long delays in one profile.
