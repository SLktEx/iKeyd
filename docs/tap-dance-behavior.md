# Bounded tap-dance behavior

`TD(...)` maps a bounded number of taps on one physical key to different key outputs without introducing an unbounded scheduler or helper-specific platform runtime.

```ikeyd
keymap BASE {
    A = TD(X, Y, Z) {
        tapping_term = 175ms
    }
}
```

This example means:

- one tap emits `X`;
- two taps emit `Y`;
- three taps emit `Z`.

## Bounds

`TD` accepts between 2 and 8 output keys.

The inter-tap window uses `tapping_term`:

- default: `200ms`;
- must be a non-negative millisecond duration;
- `0ms` resolves the current tap count immediately on release.

A released sequence may remain alive only while it has a finite next deadline. The generic `BehaviorRuntime` rejects post-release retention without a deadline, so tap dance cannot become an unbounded background task.

## Sequence lifecycle

For `TD(X, Y, Z)`:

```text
A down
A up
  -> retain count=1 until tapping deadline

A down before deadline
A up
  -> retain count=2 until the new tapping deadline

A down before deadline
A up
  -> maximum configured count reached
  -> emit Z immediately
  -> sequence ends
```

If no next tap arrives, the deadline resolves the current count. The deadline is inclusive: a wake-up at exactly the deadline resolves the old sequence.

A new same-source key-down at or after the old deadline therefore first resolves the expired sequence and then starts a fresh sequence.

## Interruption

An unrelated physical key-down resolves a pending/current tap count before that other key is routed.

For example:

```text
A tap      -> count=1 retained
B down     -> emit A's single-tap output first
             then process B
```

If a second `A` is currently held when `B` interrupts, the sequence resolves as two taps. Releasing that `A` afterward does not emit a second result.

## Physical repeat

Physical auto-repeat while the source key is held does not increment the tap count. Only a new physical press after a release can add another tap.

## Cancellation and reset

`Reset Input State`, router disposal, and generic behavior cancellation discard a pending tap-dance sequence without emitting its current tap result.

Cancellation is recovery, not a synthetic timeout.

## Layer ownership

A retained sequence belongs to the behavior runtime/keymap that created it.

This matters for transient layers. If the first tap starts `TD` from a one-shot layer, that one-shot layer may expire when the first physical key is released. A second tap within the tap-dance deadline still resumes the original retained sequence before current layer lookup; it does not silently switch to a different BASE-layer binding.

## Backend model

`TD` itself is target-neutral. The state machine emits only ordinary `BehaviorAction.SendKey` actions.

The generic runtime owns:

- tap count;
- bounded post-release retention;
- interruption semantics;
- absolute next deadline;
- resolution/cancellation.

A platform backend only provides the already-defined bounded deadline wake-up and executes primitive key output. The Windows reference backend uses the keyboard hook message loop's `WM_TIMER` path introduced for generic behavior deadlines.

This keeps the semantics reusable by the planned Rust, ZMK, and QMK backends and suitable for cross-backend conformance tests.

## Initial scope

The first `TD` helper deliberately maps tap counts to key outputs only. Arbitrary per-count action blocks, typed local counters, and reusable/composed user-defined behavior fragments remain separate #99 work rather than being hidden inside a Windows-specific tap-dance implementation.
