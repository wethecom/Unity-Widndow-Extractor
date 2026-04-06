# Extract Windows By Material

A Unity Editor tool that automatically extracts window geometry from a 3D model based on material assignment, splitting each contiguous window panel into a separate GameObject.

## Features

- **Material-based extraction** – Select which material represents the windows in your model
- **Automatic island detection** – Identifies connected window panels (separate windows become separate GameObjects)
- **Preserves UVs & normals** – Extracted meshes maintain texture mapping and lighting
- **Clean hierarchy** – Creates a parent GameObject containing each window as an individual child

## Installation

1. Place `ExtractWindowsByMaterial.cs` inside an `Editor` folder in your Unity project (e.g., `Assets/Editor/`)
2. The tool will appear under `Tools > Extract Windows By Material` in the Unity menu bar

## Usage

1. Select a GameObject with a `MeshFilter` and `MeshRenderer` in the scene
2. Go to **Tools > Extract Windows By Material**
3. A dialog will appear – choose which material index corresponds to the windows
4. The tool will:
   - Find all connected window geometry islands
   - Create a parent object named `[OriginalName]_Windows`
   - Generate individual child GameObjects (`Window_0`, `Window_1`, etc.) for each window panel
   - Assign the original window material to each extracted window

## Example

Before extraction:
```
Building
├── MeshFilter (entire building mesh)
└── MeshRenderer (materials: [Wall, Window, Roof])
```

After extraction:
```
Building
├── Building_Windows
│   ├── Window_0 (left window)
│   ├── Window_1 (center window)
│   └── Window_2 (right window)
├── MeshFilter
└── MeshRenderer
```

## Integration with Breakable Windows

The tool includes a commented line for adding a breakable window script:

```csharp
// windowObj.AddComponent<BreakWindow>();
```

Uncomment this line and replace `BreakWindow` with your own script to make extracted windows breakable automatically.

## Requirements

- Unity 2019.4 or later
- GameObject must have a `MeshFilter` and `MeshRenderer` with assigned materials

## Limitations

- The original window geometry remains in the source mesh (the tool does not modify or hide it automatically)
- Works with triangle-based meshes only
- Adjacency detection requires that window panels are not touching – connected geometry will be treated as a single island

## License

MIT License – free for commercial and personal use.