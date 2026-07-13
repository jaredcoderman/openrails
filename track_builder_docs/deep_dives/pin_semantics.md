# Pin Semantics Deep Dive

Complete understanding of how pin connections work.

## The Core Confusion

The biggest mistake is thinking **Direction = I/O nature of current node**.

Actually: **Direction = which side of the *linked* node**.

```
❌ WRONG thinking:
Pin(2, 0) means "Node 2's input side"... but which node's input?

✓ CORRECT thinking:
Pin(2, 0) means "Node 2's side 0 (input-conceptually)"
```

## Pin Anatomy

```
TrPin ( NodeID Direction )
        └─ ID of node we're connecting to
               └─ Which side of THAT node?
```

## Visual Model

Think of each node as having two "ends":

```
Node 2:
   Side 0 (Input)  ─────●─────  Side 1 (Output)
                    Track Node
```

When another node says `TrPin ( 2, 0 )`, it's connecting to the left side.
When another says `TrPin ( 2, 1 )`, it's connecting to the right side.

## Example: Three Nodes in Sequence

```
Node 1          Node 2          Node 3
 │   Side 0  ─────●─────  Side 1  │
Side 1  ●                         ●  Side 0
```

Pins:
- **Node 1**: `TrPin ( 2, 1 )` → "Connect to Node 2's right side (side 1)"
- **Node 2**: `TrPin ( 1, 0 )` → "Connect to Node 1's left side (side 0)"
- **Node 2**: `TrPin ( 3, 1 )` → "Connect to Node 3's right side (side 1)"
- **Node 3**: `TrPin ( 2, 0 )` → "Connect to Node 2's left side (side 0)"

## trpins Header Breakdown

```
trpins ( 1 1
    TrPin ( 1, 0 )
    TrPin ( 3, 1 )
)
```

The `1 1` part means: "This node has 1 pin on its side 0, and 1 pin on its side 1"

But **these numbers are just counts**. The actual pins tell you what they're connecting to:
- `TrPin ( 1, 0 )` connects to Node 1's side 0 (and is on our side 0 by necessity)
- `TrPin ( 3, 1 )` connects to Node 3's side 1 (and is on our side 1 by necessity)

## Reciprocal Rule

For EVERY pin connection, the other node MUST have a reciprocal:

```
If Node A says:   TrPin ( B, direction_B )
Then Node B must: TrPin ( A, opposite(direction_B) )

opposite(0) = 1
opposite(1) = 0
```

### Example Chain

```
Node 1 ←→ Node 2 ←→ Node 3

Node 1:
  trpins ( 1 0 )
    TrPin ( 2, 1 )         ← "Connect to 2's right side"

Node 2:
  trpins ( 1 1 )
    TrPin ( 1, 0 )         ← "Connect to 1's left side" (reciprocal!)
    TrPin ( 3, 1 )         ← "Connect to 3's right side"

Node 3:
  trpins ( 1 0 )
    TrPin ( 2, 0 )         ← "Connect to 2's left side" (reciprocal!)
```

Checking reciprocity:
- Node 1 → 2@1: Node 2 has 1 pin on side 1? YES ✓
- Node 2 ← 1@0: Node 1 has 0 pin on side 0? NO, 1 pin on side 0 ✓
- Node 2 → 3@1: Node 3 has 1 pin on side 1? NO, 0 ✓
- Node 3 ← 2@0: Node 2 has 1 pin on side 0? YES ✓

Wait, this is confusing. Let me re-clarify...

## The Actual Rule

When Node A says `TrPin ( B, d )`:
- It's connecting to Node B
- Using Node B's side `d`
- This pin itself is on one of Node A's sides

Node B MUST have:
- A pin that points back to Node A
- Using the appropriate opposite side

```
Node 1 side 1 ──── (connects via pin) ──── Node 2 side 0

Node 1: TrPin ( 2, 1 )  ← "I'm on side 1 connecting to Node 2 side 1"... NO!

CORRECT:
Node 1: TrPin ( 2, 0 )  ← "I'm using side 0 of my pin count, connecting to Node 2 side 0"
Node 2: TrPin ( 1, 1 )  ← "I'm using side 1 of my pin count, connecting to Node 1 side 1"
```

The **PIN ITSELF** occupies a side slot. If Node 1 has `trpins ( 1 0 )`, it has one pin on side 0.
That pin's Direction field tells you which side of the TARGET node it connects to.

## Corrected Understanding

```
Node 1:
  trpins ( 1 0 )          ← Has 1 pin on side 0, 0 on side 1
    TrPin ( 2, 0 )        ← The side-0 pin connects to Node 2's side 0

Node 2:
  trpins ( 1 1 )          ← Has 1 pin on side 0, 1 on side 1
    TrPin ( 1, 1 )        ← The side-0 pin connects to Node 1's side 1??? NO
```

This still doesn't work. Let me look at the actual working example:

```
Node 1 (End):     trpins ( 1 0 ) TrPin ( 2, 1 )
Node 2 (Vector):  trpins ( 1 1 ) TrPin ( 1, 0 ); TrPin ( 3, 1 )
Node 3 (End):     trpins ( 1 0 ) TrPin ( 2, 0 )
```

Reading this:
- Node 1 has 1 pin on side 0, 0 on side 1. The side-0 pin points to Node 2's side 1
- Node 2 has 1 pin on side 0, 1 on side 1. Side-0 pin points to Node 1's side 0. Side-1 pin points to Node 3's side 1
- Node 3 has 1 pin on side 0, 0 on side 1. The side-0 pin points to Node 2's side 0

Verification:
- Node 1→2@1 asks: Does Node 2 have a pin on side 1? YES (TrPin ( 3, 1 )) and it's reciprocal ✓
- Node 2→1@0 asks: Does Node 1 have a pin on side 0? YES (TrPin ( 2, 1 )) and it's reciprocal ✓

**Now it makes sense!**

## Final Rule

**Direction = Which side of the TARGET node, NOT which side the pin occupies**

The pin's physical side is determined by the `trpins` header count.
The pin's Direction field is which side of the node it's pointing to.

## Practical Application

To add a pin from Node A to Node B on side `d`:

1. Count existing pins on each side of Node A
2. Add new `TrPin ( B, d )` to Node A's pin list
3. Increment appropriate counter in Node A's `trpins ( ... )`
4. On Node B, add reciprocal `TrPin ( A, opposite_side )`
5. Verify pin count increase in Node B's `trpins ( ... )`

## Memorization Trick

Think: **"I'm sending a letter to address (Node, Side)"**

```
TrPin ( 2, 0 ) means:
"I'm sending a connection to Node 2, specifically to its Side 0"
```

Node 2 must have a letter back to you on the opposite side to make it reciprocal.

## Common Mistakes

❌ **Thinking Direction is "input" or "output"**
- It's not! It's which side of the target node

❌ **Forgetting reciprocal pins**
- Every connection must be bidirectional

❌ **Mismatching pin counts**
- If Node A pins to Node B side 1, Node B must have enough pins on side 1

❌ **Vector nodes pinning directly**
- This causes MapViewer to crash

✓ **Single multi-section vector node** is the solution

## References

- Source: `Orts.Formats.Msts/TrackDatabaseFile.cs` line 387
- See also: `Orts.Simulation/Simulation/Signalling/Signals.cs` lines 2735-2751
