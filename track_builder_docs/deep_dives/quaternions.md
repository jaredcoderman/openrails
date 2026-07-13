# Quaternions and Rotations

Understanding quaternion representation and conversion.

## Why Quaternions?

Euler angles (AX, AY, AZ) have limitations:
- Gimbal lock (lose a degree of freedom)
- Non-intuitive interpolation
- Accumulation errors over many rotations

Quaternions solve these by representing rotation as an axis + angle.

## Quaternion Structure

```
Quaternion (Qx, Qy, Qz, Qw)
```

- **Qx, Qy, Qz**: Rotation axis components (normalized)
- **Qw**: Scalar part (represents rotation magnitude)
- **Always normalized**: √(Qx² + Qy² + Qz² + Qw²) = 1

## Example Quaternions

### No Rotation
```
(0, 0, 0, 1)
```
Identity quaternion - no rotation applied.

### 90° Rotation Around Y-Axis (Heading)
```
(0, 0.707107, 0, 0.707107)
```
Qy = sin(90°/2) = sin(45°) = 0.707
Qw = cos(90°/2) = cos(45°) = 0.707

### 180° Rotation Around Y-Axis
```
(0, 1, 0, 0)
```
Qy = sin(180°/2) = sin(90°) = 1
Qw = cos(180°/2) = cos(90°) = 0

## Conversion from Euler to Quaternion

Using ZYX rotation order (roll-pitch-yaw):

```csharp
// Input: Euler angles in radians
float roll = az;    // Rotation around Z (roll/banking)
float pitch = ax;   // Rotation around X (pitch/forward tilt)
float yaw = ay;     // Rotation around Y (yaw/heading)

// Precompute sines/cosines
float cy = cos(yaw * 0.5f);
float sy = sin(yaw * 0.5f);
float cp = cos(pitch * 0.5f);
float sp = sin(pitch * 0.5f);
float cr = cos(roll * 0.5f);
float sr = sin(roll * 0.5f);

// Compute quaternion
float qx = sr * cp * cy - cr * sp * sy;
float qy = cr * sp * cy + sr * cp * sy;
float qz = cr * cp * sy - sr * sp * cy;
float qw = cr * cp * cy + sr * sp * sy;
```

## Step-by-Step Example

Convert heading of 90° North (π/2 radians) with no pitch or roll:

```
roll = 0    → cr = cos(0) = 1,  sr = sin(0) = 0
pitch = 0   → cp = cos(0) = 1,  sp = sin(0) = 0
yaw = π/2   → cy = cos(π/4) ≈ 0.707,  sy = sin(π/4) ≈ 0.707

qx = 0 * 1 * 0.707 - 1 * 0 * 0.707 = 0
qy = 1 * 0 * 0.707 + 0 * 1 * 0.707 = 0
qz = 1 * 1 * 0.707 - 0 * 0 * 0.707 = 0.707
qw = 1 * 1 * 0.707 + 0 * 0 * 0.707 = 0.707

Result: (0, 0, 0.707107, 0.707107)
```

Matches the 90° Y-rotation quaternion!

## Using Quaternions in World Files

World files store orientation as quaternion:

```
QDirection ( Qx Qy Qz Qw )
```

Example from dynamic track:

```
DyntrackObj (
    Position ( 500 100 0 )
    QDirection ( 0 0.707107 0 0.707107 )    ← 90° heading
)
```

## Quaternion Composition

To combine two rotations Q1 and Q2:

```csharp
Q_combined = Q1 * Q2   // Quaternion multiplication
```

Not simple addition!

```csharp
public static Quaternion Multiply(Quaternion q1, Quaternion q2)
{
    return new Quaternion(
        q1.w * q2.x + q1.x * q2.w + q1.y * q2.z - q1.z * q2.y,
        q1.w * q2.y - q1.x * q2.z + q1.y * q2.w + q1.z * q2.x,
        q1.w * q2.z + q1.x * q2.y - q1.y * q2.x + q1.z * q2.w,
        q1.w * q2.w - q1.x * q2.x - q1.y * q2.y - q1.z * q2.z
    );
}
```

## Normalization

Ensure quaternion is unit length:

```csharp
public static Quaternion Normalize(Quaternion q)
{
    float length = sqrt(q.x*q.x + q.y*q.y + q.z*q.z + q.w*q.w);
    return new Quaternion(q.x/length, q.y/length, q.z/length, q.w/length);
}
```

## TSRE5 Coordinate Transformation

TSRE5 expects specific quaternion adjustments. In `DynTrackObj::load()`:

```cpp
// Start with quaternion from track data
Quaternion q = trackQuaternion;

// Apply 180° Y-axis rotation
Quaternion yRotation(0, 1, 0, 0);  // 180° around Y
q = q * yRotation;  // Compose rotations

// Position also needs Z negation
position.z = -position.z;
quaternion.z = -quaternion.z;
```

This explains why TdbDump applies transformations when writing world files.

## Debugging Quaternions

### Verify Normalization

Check if quaternion length ≈ 1:

```
length = √(0² + 0.707107² + 0² + 0.707107²)
       = √(0 + 0.5 + 0 + 0.5)
       = √1 = 1 ✓
```

### Check for Gimbal Lock

Unlikely with quaternions, but verify Q not zero-vector:

```
NOT: (0, 0, 0, 0)
NOT: (0, 0, 0, -1)  // Invalid
```

### Verify Rotation Direction

Apply quaternion to vector and check result makes sense:

```
Rotate (1, 0, 0) by 90° around Y should give (0, 0, 1) or similar
```

## Common Conversions

### Heading Only (Pure Yaw)

```csharp
// Convert 90° heading to quaternion
float yaw = PI / 2;  // 90°

float qy = sin(yaw / 2) ≈ 0.707;
float qw = cos(yaw / 2) ≈ 0.707;

// Result: (0, qy, 0, qw)
```

### 45° Bank (Z rotation)

```csharp
float roll = PI / 4;  // 45°

float qz = sin(roll / 2) ≈ 0.383;
float qw = cos(roll / 2) ≈ 0.924;

// Result: (0, 0, qz, qw)
```

## Properties

- **Conjugate**: (−Qx, −Qy, −Qz, Qw) reverses rotation
- **Inverse**: For unit quaternions, inverse = conjugate
- **Interpolation**: SLERP (Spherical Linear Interpolation) for smooth transitions
- **Commutative**: Order matters! Q1*Q2 ≠ Q2*Q1

## References

- Quaternion math: https://en.wikipedia.org/wiki/Quaternion
- TSRE5 implementation: DynTrackObj.cpp
- Open Rails: Orts.Formats.Msts/WorldFile.cs (ConvertEulerToQuaternion comments)
