# Pin Connections

Pins define how track nodes connect to each other. Understanding pin semantics is critical for valid TDB structures.

## Overview

Pins are the mechanism by which track nodes link together, forming a continuous network.

```
TrEndNode (1) ──Pin──→ TrVectorNode (2) ──Pin──→ TrEndNode (3)
```

## TrPin Structure

Each pin contains:

```csharp
public class TrPin
{
    public int Node;       // ID of the linked node
    public int Direction;  // 0 or 1 (which side of that node)
}
```

## Direction Semantics (Crucial!)

**Direction refers to the *linked node's* side, NOT the current node's I/O nature.**

```
Current Node ──Pin(NodeB, Direction)──→ Node B's specific side
                              └─ Side 0 = input side
                              └─ Side 1 = output side
```

### Example

Node 1 has: `TrPin ( 2, 1 )` 
- "Connect to Node 2"
- "Use Node 2's side 1 (output side)"

Node 2 must have: `TrPin ( 1, 0 )`
- "Connect to Node 1"
- "Use Node 1's side 0 (input side)"
- ✓ This is reciprocal!

## trpins Block Header

The `trpins` line specifies how many pins are on each side of the **current node**:

```
trpins ( [inpins] [outpins]
    TrPin ( ... )
    TrPin ( ... )
)
```

- **inpins**: Number of pins on side 0 (input conceptually)
- **outpins**: Number of pins on side 1 (output conceptually)

### Example

```
trpins ( 1 1
    TrPin ( 1 0 )  ← This is one inpin (side 0)
    TrPin ( 3 1 )  ← This is one outpin (side 1)
)
```

Has 1 pin on side 0 and 1 pin on side 1.

## Node Types and Pin Requirements

### TrEndNode (Terminus)

Always has exactly **1 input pin, 0 output pins**:

```
trpins ( 1 0
    TrPin ( 2 1 )   ← Points to vector node's output side
)
```

- Validates with: `expectedPins = [1, 1, 0]` (total=1, in=1, out=0)

### TrVectorNode (Track Section)

Always has exactly **1 input pin, 1 output pin**:

```
trpins ( 1 1
    TrPin ( 1 0 )   ← Incoming from previous node's input side
    TrPin ( 3 1 )   ← Outgoing to next node's output side
)
```

- Validates with: `expectedPins = [2, 1, 1]` (total=2, in=1, out=1)

### TrJunctionNode (3-way split)

Has **3 total pins, configuration varies**:

```
trpins ( 3 1
    TrPin ( 2 0 )   ← Main line incoming
    TrPin ( 3 1 )   ← Branch 1 outgoing
    TrPin ( 4 1 )   ← Branch 2 outgoing
)
```

- Validates with: `expectedPins = [3, 1, 2]`

## Reciprocal Connection Rules

For valid pin connections:

1. **Reciprocity**: If A pins to B, then B must pin back to A
2. **Direction matching**: Directions must be opposites (0 ↔ 1)
3. **Side consistency**: Can't have both nodes pinning to same side

### Invalid Example ❌

```
Node 1: TrPin ( 2, 0 )  ← To node 2's input side
Node 2: TrPin ( 1, 0 )  ← To node 1's input side
            ❌ Both pinning to input sides! Not reciprocal
```

### Valid Example ✓

```
Node 1: TrPin ( 2, 1 )  ← To node 2's output side
Node 2: TrPin ( 1, 0 )  ← To node 1's input side
            ✓ Reciprocal!
```

## Simple Linear Track Structure

```
┌─────────────┐
│ TrEndNode 1 │
│ trpins (1 0)│
│TrPin(2,1)   │
└──────┬──────┘
       │
       ↓
┌─────────────────────────┐
│ TrVectorNode 2          │
│ trpins ( 1 1            │
│   TrPin ( 1, 0 )        │
│   TrPin ( 3, 1 )        │
│ )                       │
└──────┬────────┬─────────┘
       │        │
       │ (1,0)  │ (3,1)
       │        │
       ↓        ↓
     ┌─────────────┐
     │ TrEndNode 3 │
     │ trpins (1 0)│
     │TrPin(2,0)   │
     └─────────────┘
```

Reading the connections:
- Node 1 outputs to Node 2's input (side 0/1)
- Node 2 has input from Node 1, output to Node 3 (sides 0/1)
- Node 3 inputs from Node 2's input (side 0)

✓ All reciprocal!

## Pin Validation in Open Rails

Open Rails checks (`Signals.cs` lines 2735-2751):

```csharp
var expectedPins = 
    trJunctionNode != null ? new[] { 3, 1, 2 } :
    trVectorNode != null ? new[] { 2, 1, 1 } :
    trEndNode != null ? new[] { 1, 1, 0 } :
    new[] { 0, 0, 0 };

if (pin.Link > nodes.Length)
    warn: "pin link to invalid node";

if (pin.Link <= 0)
    warn: "pin link to node 0 (null)";
```

Common warnings:
```
Ignored invalid track node X pin [dir] link to track node Y
```

This happens when:
- Y (referenced node) is out of bounds
- Y is 0 (null node)
- Pin direction doesn't match validation rules

## Troubleshooting

### Error: "Ignored invalid track node pin"

**Check:**
1. [ ] All node IDs in pins are 1-based (not 0)
2. [ ] All referenced node IDs exist (not > nodes.Length)
3. [ ] Pin directions are 0 or 1 (not other values)
4. [ ] End nodes have `trpins ( 1 0 )` not `( 1 1 )`
5. [ ] Vector nodes have `trpins ( 1 1 )` not `( 2 0 )`

### Track appears disconnected

**Check:**
1. [ ] All pin connections are reciprocal
2. [ ] No vector nodes pinning directly to other vector nodes
3. [ ] End nodes properly bound to start/end of track

### MapViewer crashes with NullReferenceException

**Cause:** Vector nodes pinning directly to other vector nodes

**Fix:** Use single multi-section `TrVectorNode` or add junctions

## Key Takeaways

- **Direction = linked node's side**, not current node
- **trpins header = current node's sides**
- **All connections must be reciprocal**
- **Validate against expected pin counts**
- **Single multi-section vector node is preferred over multiple single-section nodes**
